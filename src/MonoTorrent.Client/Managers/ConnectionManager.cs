//
// ConnectionManager.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2006 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//



using System;
using MonoTorrent.Common;
using MonoTorrent.Client.PeerMessages;
using System.Net.Sockets;
using System.Threading;
using MonoTorrent.Client.Encryption;
using System.Diagnostics;

namespace MonoTorrent.Client
{
    /// <summary>
    /// Main controller class for all incoming and outgoing connections
    /// </summary>
    public class ConnectionManager
    {
        #region Events
        /// <summary>
        /// Event that's fired every time a Peer connects or disconnects
        /// </summary>
        public event EventHandler<PeerConnectionEventArgs> OnPeerConnectionChanged;


        /// <summary>
        /// Event that's fired every time a message is sent or Received from a Peer
        /// </summary>
        public event EventHandler<PeerMessageEventArgs> OnPeerMessages;
        #endregion


        #region Member Variables
        public const int ChunkLength = 2048;   // Download in 2kB chunks to allow for better rate limiting

        private EngineSettings settings;

        private AsyncCallback peerEndCreateConnection;
        private AsyncCallback peerMessageLengthReceived;
        private AsyncCallback peerMessageReceived;
        private AsyncCallback peerMessageSent;
        private AsyncCallback peerHandshakeReceieved;
        private AsyncCallback peerHandshakeSent;
        private AsyncCallback peerBitfieldSent;


        /// <summary>
        /// The number of half open connections
        /// </summary>
        public int HalfOpenConnections
        {
            get { return this.halfOpenConnections; }
        }
        private int halfOpenConnections;


        /// <summary>
        /// The maximum number of half open connections
        /// </summary>
        public int MaxHalfOpenConnections
        {
            get { return this.settings.GlobalMaxHalfOpenConnections; }
        }


        /// <summary>
        /// The number of open connections
        /// </summary>
        public int OpenConnections
        {
            get { return this.openConnections; }
        }
        private int openConnections;


        /// <summary>
        /// The maximum number of open connections
        /// </summary>
        public int MaxOpenConnections
        {
            get { return this.settings.GlobalMaxConnections; }
        }
        #endregion


        #region Constructors
        /// <summary>
        /// 
        /// </summary>
        /// <param name="settings"></param>
        public ConnectionManager(EngineSettings settings)
        {
            this.settings = settings;

            this.peerMessageReceived = new AsyncCallback(this.onPeerMessageReceived);
            this.peerMessageLengthReceived = new AsyncCallback(this.onPeerMessageLengthReceived);
            this.peerMessageSent = new AsyncCallback(this.onPeerMessageSent);
            this.peerHandshakeSent = new AsyncCallback(this.onPeerHandshakeSent);
            this.peerHandshakeReceieved = new AsyncCallback(this.onPeerHandshakeReceived);
            this.peerEndCreateConnection = new AsyncCallback(this.EndCreateConnection);
            this.peerBitfieldSent = new AsyncCallback(this.OnPeerBitfieldSent);
        }
        #endregion


        #region Async Connection Methods
        /// <summary>
        /// 
        /// </summary>
        /// <param name="manager"></param>
        internal void ConnectToPeer(TorrentManager manager)
        {
            if ((this.openConnections >= this.MaxOpenConnections) || this.halfOpenConnections >= this.MaxHalfOpenConnections)
                return;

            PeerConnectionID id;
            System.Threading.Interlocked.Increment(ref this.halfOpenConnections);

            id = manager.Available[0];
            manager.Available.Remove(0);
            manager.ConnectingTo.Add(id);
            lock (id)
            {
                //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + " ****Connecting****");
                id.Peer.Connection = new TCPConnection(id.Peer.Location, id.TorrentManager.Torrent.Pieces.Length, new NoEncryption());
                id.Peer.Connection.ProcessingQueue = true;
                id.Peer.Connection.LastMessageSent = DateTime.Now;
                id.Peer.Connection.BeginConnect(peerEndCreateConnection, id);
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when we try to create a remote connection
        /// </summary>
        /// <param name="result"></param>
        private void EndCreateConnection(IAsyncResult result)
        {
            bool cleanUp = false;
            PeerConnectionID id = (PeerConnectionID)result.AsyncState;
            result.AsyncWaitHandle.Close();
            
            try
            {
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        if (id.Peer.Connection == null)
                            return;

                        id.Peer.Connection.EndConnect(result);
                        //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + " ****Connected****");
                        id.TorrentManager.ConnectingTo.Remove(id);
                        id.TorrentManager.ConnectedPeers.Add(id);

                        if (this.OnPeerConnectionChanged != null)
                            this.OnPeerConnectionChanged(id, new PeerConnectionEventArgs(PeerConnectionEvent.OutgoingConnectionCreated));

                        if (this.openConnections > this.MaxOpenConnections)
                        {
                            cleanUp = true;
                            return;
                        }

                        //FIXME: Can this be optimised more? How many allocations is this responsible for?
                        HandshakeMessage handshake = new HandshakeMessage(id.TorrentManager.Torrent.InfoHash, ClientEngine.PeerId, VersionInfo.ProtocolStringV100);

                        id.Peer.Connection.sendBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.SmallMessageBuffer);
                        id.Peer.Connection.BytesToSend = 0;
                        id.Peer.Connection.BytesToSend += handshake.Encode(id.Peer.Connection.sendBuffer, 0);

                        id.Peer.Connection.BeginSend(id.Peer.Connection.sendBuffer, 0, id.Peer.Connection.BytesToSend, SocketFlags.None, peerHandshakeSent, id, out id.ErrorCode);
                        System.Threading.Interlocked.Increment(ref this.openConnections);
                        System.Threading.Interlocked.Decrement(ref this.halfOpenConnections);
                    }
                }
            }

            catch (SocketException ex)
            {
                Debug.WriteLine(ex.NativeErrorCode + ": " + ex.Message);
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        id.Peer.FailedConnectionAttempts++;
                        //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + " ****Connecting Failed****");

                        if (id.Peer.Connection != null)
                        {
                            ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.sendBuffer);
                            id.Peer.Connection.sendBuffer = null;
                            id.Peer.Connection.ProcessingQueue = false;
                            id.Peer.Connection.Dispose();
                        }
                        id.Peer.Connection = null;

                        id.TorrentManager.ConnectingTo.Remove(id);

                        if (id.Peer.FailedConnectionAttempts < 2)   // We couldn't connect this time, so re-add to available
                            id.TorrentManager.Available.Add(id);

                        System.Threading.Interlocked.Decrement(ref this.halfOpenConnections);
                    }
                }
            }
            catch (ArgumentException)
            {
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + " ****Connecting Failed****");
                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.sendBuffer);
                        id.Peer.Connection.sendBuffer = null;
                        id.Peer.FailedConnectionAttempts++;
                        id.Peer.Connection.ProcessingQueue = false;
                        if (id.Peer.Connection != null)
                            id.Peer.Connection.Dispose();
                        id.Peer.Connection = null;

                        id.TorrentManager.ConnectingTo.Remove(id);

                        if (id.Peer.FailedConnectionAttempts < 10)   // We couldn't connect this time, so re-add to available
                            id.TorrentManager.Available.Add(id);

                        System.Threading.Interlocked.Decrement(ref this.halfOpenConnections);
                    }
                }
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id);
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when we send our handshake message
        /// </summary>
        /// <param name="result"></param>
        private void onPeerHandshakeSent(IAsyncResult result)
        {
            bool cleanUp = false;
            PeerConnectionID id = (PeerConnectionID)result.AsyncState;
            result.AsyncWaitHandle.Close();
            
            try
            {
                lock (id.TorrentManager.listLock)
                    lock (id)
                    {
                        if (id.Peer.Connection == null)
                            return;

                        int bytesSent = id.Peer.Connection.EndSend(result, out id.ErrorCode);
                        if (id.ErrorCode != SocketError.Success)
                        {
                            cleanUp = true;
                            return;
                        }
                        if (bytesSent == 0)
                        {
                            cleanUp = true;
                            return;
                        }

                        id.Peer.Connection.BytesSent += bytesSent;
                        id.Peer.Connection.Monitor.BytesSent(bytesSent);
                        id.TorrentManager.ProtocolBytesUploaded += bytesSent;

                        if (id.Peer.Connection.BytesSent != id.Peer.Connection.BytesToSend)     // If we havn't sent everything
                        {
                            id.Peer.Connection.BeginSend(id.Peer.Connection.sendBuffer, id.Peer.Connection.BytesSent, id.Peer.Connection.BytesToSend - id.Peer.Connection.BytesSent, SocketFlags.None, peerHandshakeSent, id, out id.ErrorCode);
                            return;
                        }
                        //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + "Handshake");
                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.sendBuffer);
                        id.Peer.Connection.sendBuffer = null;
                        id.Peer.Connection.recieveBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.SmallMessageBuffer);
                        id.Peer.Connection.BytesReceived = 0;
                        id.Peer.Connection.BytesToRecieve = 68;       // FIXME: Will fail if protocol version changes. FIX THIS
                        id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, 0, id.Peer.Connection.BytesToRecieve, SocketFlags.None, peerHandshakeReceieved, id, out id.ErrorCode);
                    }
            }

            catch (SocketException ex)
            {
                Debug.WriteLine(ex.NativeErrorCode + ": " + ex.Message);
                cleanUp = true;
            }
            catch (ArgumentException)
            {
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id);
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when we recieve a peer handshake
        /// </summary>
        /// <param name="result"></param>
        private void onPeerHandshakeReceived(IAsyncResult result)
        {
            bool cleanUp = false;
            IPeerMessageInternal msg;
            PeerConnectionID id = (PeerConnectionID)result.AsyncState;
            result.AsyncWaitHandle.Close();
            
            try
            {
                lock (id.TorrentManager.listLock)
                    lock (id)
                    {
                        if (id.Peer.Connection == null)
                            return;

                        int bytesReceived = id.Peer.Connection.EndReceive(result, out id.ErrorCode);
                        if (id.ErrorCode != SocketError.Success)
                        {
                            cleanUp = true;
                            return;
                        }
                        if (bytesReceived == 0)
                        {
                            cleanUp = true;
                            return;
                        }

                        id.Peer.Connection.BytesReceived += bytesReceived;
                        id.Peer.Connection.Monitor.BytesReceived(bytesReceived);
                        id.TorrentManager.ProtocolBytesDownloaded += bytesReceived;

                        if (id.Peer.Connection.BytesReceived != id.Peer.Connection.BytesToRecieve)
                        {
                            id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, id.Peer.Connection.BytesReceived, id.Peer.Connection.BytesToRecieve - id.Peer.Connection.BytesReceived, SocketFlags.None, peerHandshakeReceieved, id, out id.ErrorCode);
                            return;
                        }

                        //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + "\tHandshake");
                        msg = new HandshakeMessage();
                        msg.Decode(id.Peer.Connection.recieveBuffer, 0, id.Peer.Connection.BytesToRecieve);
                        HandshakeMessage handshake = msg as HandshakeMessage;
                        if (!ToolBox.ByteMatch(handshake.infoHash, id.TorrentManager.Torrent.InfoHash))
                        {
                            cleanUp = true;
                            return;
                        }

                        if (string.IsNullOrEmpty(id.Peer.PeerId))
                            id.Peer.PeerId = handshake.PeerId;

                        id.Peer.Connection.ClientApp = new PeerID(handshake.PeerId);
                        id.Peer.Connection.SupportsFastPeer = handshake.SupportsFastPeer;

                        if (((HandshakeMessage)msg).SupportsFastPeer)
                        {
                            id.Peer.Connection.SupportsFastPeer = true;
                            if (id.TorrentManager.PieceManager.MyBitField.AllFalse())
                                msg = new HaveNoneMessage();

                            else if (id.TorrentManager.Progress == 100.0)
                                msg = new HaveAllMessage();

                            else
                                msg = new BitfieldMessage(id.TorrentManager.PieceManager.MyBitField);
                        }
                        else
                        {
                            msg = new BitfieldMessage(id.TorrentManager.PieceManager.MyBitField);
                        }

                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.recieveBuffer);
                        id.Peer.Connection.recieveBuffer = null;
                        id.Peer.Connection.sendBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.LargeMessageBuffer);

                        id.Peer.Connection.BytesSent = 0;
                        id.Peer.Connection.BytesToSend = msg.Encode(id.Peer.Connection.sendBuffer, 0);
                        id.Peer.Connection.BeginSend(id.Peer.Connection.sendBuffer, 0, id.Peer.Connection.BytesToSend, SocketFlags.None, this.peerBitfieldSent, id, out id.ErrorCode);
                    }
            }

            catch (SocketException ex)
            {
                Debug.WriteLine(ex.NativeErrorCode + ": " + ex.Message);
                cleanUp = true;
            }
            catch (ArgumentException)
            {
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="result"></param>
        private void OnPeerBitfieldSent(IAsyncResult result)
        {
            bool cleanUp = false;
            PeerConnectionID id = (PeerConnectionID)result.AsyncState;
            result.AsyncWaitHandle.Close(); 
            try
            {
                lock (id.TorrentManager.listLock)
                    lock (id)
                    {
                        if (id.Peer.Connection == null)
                            return;

                        int bytesSent = id.Peer.Connection.EndSend(result, out id.ErrorCode);
                        if (id.ErrorCode != SocketError.Success)
                        {
                            cleanUp = true;
                            return;
                        }
                        if (bytesSent == 0)
                        {
                            cleanUp = true;
                            return;
                        }

                        id.Peer.Connection.BytesSent += bytesSent;
                        id.Peer.Connection.Monitor.BytesSent(bytesSent);
                        id.TorrentManager.ProtocolBytesUploaded += bytesSent;

                        if (id.Peer.Connection.BytesSent != id.Peer.Connection.BytesToSend)
                        {
                            id.Peer.Connection.BeginSend(id.Peer.Connection.sendBuffer, id.Peer.Connection.BytesSent, id.Peer.Connection.BytesToSend - id.Peer.Connection.BytesSent, SocketFlags.None, peerBitfieldSent, id, out id.ErrorCode);
                            return;
                        }
                        //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + "BitfieldMessage");
                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.sendBuffer);
                        id.Peer.Connection.sendBuffer = null;
                        id.Peer.Connection.recieveBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.SmallMessageBuffer);

                        id.Peer.Connection.BytesReceived = 0;
                        id.Peer.Connection.BytesToRecieve = 4;
                        id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, 0, id.Peer.Connection.BytesToRecieve, SocketFlags.None, peerMessageLengthReceived, id, out id.ErrorCode);
                        id.Peer.Connection.ProcessingQueue = false;
                    }
            }
            catch (SocketException ex)
            {
                Debug.WriteLine(ex.ToString());
                cleanUp = true;
            }
            catch (ArgumentException)
            {
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id);
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when we recieve a peer message length
        /// </summary>
        /// <param name="result"></param>
        private void onPeerMessageLengthReceived(IAsyncResult result)
        {
            bool cleanUp = false;
            PeerConnectionID id = (PeerConnectionID)result.AsyncState;
            result.AsyncWaitHandle.Close();
            
            try
            {
                lock (id.TorrentManager.listLock)
                    lock (id)
                    {
                        if (id.Peer.Connection == null)
                            return;

                        int bytesReceived = id.Peer.Connection.EndReceive(result, out id.ErrorCode);
                        if (id.ErrorCode != SocketError.Success)
                        {
                            cleanUp = true;
                            return;
                        }
                        if (bytesReceived == 0)
                        {
                            cleanUp = true;
                            return;
                        }

                        id.Peer.Connection.BytesReceived += bytesReceived;
                        id.Peer.Connection.Monitor.BytesReceived(bytesReceived);
                        id.TorrentManager.ProtocolBytesDownloaded += bytesReceived;

                        if (id.Peer.Connection.BytesReceived != id.Peer.Connection.BytesToRecieve)
                        {
                            id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, id.Peer.Connection.BytesReceived, id.Peer.Connection.BytesToRecieve - id.Peer.Connection.BytesReceived, SocketFlags.None, peerMessageLengthReceived, id, out id.ErrorCode);
                            return;
                        }

                        int networkOrderLength = BitConverter.ToInt32(id.Peer.Connection.recieveBuffer, 0);
                        id.Peer.Connection.BytesReceived = 0;
                        id.Peer.Connection.BytesToRecieve = System.Net.IPAddress.NetworkToHostOrder(networkOrderLength);

                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.recieveBuffer);
                        id.Peer.Connection.recieveBuffer = null;

                        if (id.Peer.Connection.BytesToRecieve > BufferManager.SmallMessageBufferSize)
                            id.Peer.Connection.recieveBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.LargeMessageBuffer);
                        else
                            id.Peer.Connection.recieveBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.SmallMessageBuffer);

                        id.Peer.Connection.LastMessageReceived = DateTime.Now;
                        if (id.Peer.Connection.BytesToRecieve == 0)     // We Received a KeepAlive
                        {
                            id.Peer.Connection.BytesToRecieve = 4;
                            id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, 0, id.Peer.Connection.BytesToRecieve, SocketFlags.None, peerMessageLengthReceived, id, out id.ErrorCode);
                        }
                        else
                        {
                            if (id.TorrentManager.Settings.MaxDownloadSpeed == 0)   // No rate limiting needed
                            {
                                id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, 0, id.Peer.Connection.BytesToRecieve, SocketFlags.None, onPeerMessageReceived, id, out id.ErrorCode);
                                return;
                            }
                            else    // Apply rate limiting
                            {
                                id.TorrentManager.downloadQueue.Enqueue(id);
                                id.TorrentManager.ResumePeers();
                            }
                        }
                    }
            }
            catch (SocketException ex)
            {
                Debug.WriteLine(ex.NativeErrorCode + ": " + ex.Message);
                cleanUp = true;
            }
            catch (ArgumentException)
            {
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id);
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when we recieve a peer message
        /// </summary>
        /// <param name="result"></param>
        private void onPeerMessageReceived(IAsyncResult result)
        {
            bool cleanUp = false;
            PeerConnectionID id = (PeerConnectionID)result.AsyncState;
            result.AsyncWaitHandle.Close();
            
            try
            {
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        if (id.Peer.Connection == null)
                            return;

                        int bytesReceived = id.Peer.Connection.EndReceive(result, out id.ErrorCode);
                        if (id.ErrorCode != SocketError.Success)
                        {
                            cleanUp = true;
                            return;
                        }
                        if (bytesReceived == 0)
                        {
                            cleanUp = true;
                            return;
                        }

                        id.Peer.Connection.BytesReceived += bytesReceived;
                        id.TorrentManager.DataBytesDownloaded += bytesReceived;
                        id.Peer.Connection.Monitor.BytesReceived(bytesReceived);

                        if (id.Peer.Connection.BytesReceived != id.Peer.Connection.BytesToRecieve)
                        {
                            if (id.TorrentManager.Settings.MaxDownloadSpeed == 0)
                            {
                                id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, id.Peer.Connection.BytesReceived, id.Peer.Connection.BytesToRecieve - id.Peer.Connection.BytesReceived, SocketFlags.None, onPeerMessageReceived, id, out id.ErrorCode);
                            }
                            else
                            {
                                id.TorrentManager.downloadQueue.Enqueue(id);
                                id.TorrentManager.ResumePeers();
                            }
                            return;
                        }

                        IPeerMessageInternal message = PeerwireEncoder.Decode(id.Peer.Connection.recieveBuffer, 0, id.Peer.Connection.BytesToRecieve, id.TorrentManager);
                        if (this.OnPeerMessages != null)
                            this.OnPeerMessages(id, new PeerMessageEventArgs((IPeerMessage)message, Direction.Incoming));
                        message.Handle(id);
                        //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + "\t" + message.ToString());
                        if (!(message is PieceMessage))
                        {   // The '-4' is because of the messagelength int which has already been counted in a different method
                            id.TorrentManager.DataBytesDownloaded -= (message.ByteLength - 4);
                            id.TorrentManager.ProtocolBytesDownloaded += (message.ByteLength - 4);
                            if (id.Peer.HashFails == 3)
                                cleanUp = true;
                        }

                        id.Peer.Connection.LastMessageReceived = DateTime.Now;
                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.recieveBuffer);
                        id.Peer.Connection.recieveBuffer = null;
                        id.Peer.Connection.recieveBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.SmallMessageBuffer);

                        id.Peer.Connection.BytesReceived = 0;
                        id.Peer.Connection.BytesToRecieve = 4;
                        id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, 0, id.Peer.Connection.BytesToRecieve, SocketFlags.None, peerMessageLengthReceived, id, out id.ErrorCode);
                    }
                }
            }
            catch (SocketException ex)
            {
                Debug.WriteLine(ex.NativeErrorCode + ": " + ex.Message);
                cleanUp = true;
            }
            catch (ArgumentException)
            {
#warning should be unneccessary
                cleanUp = true;
            }
            catch (NullReferenceException)
            {
#warning should be unneccessary
                cleanUp = true;
            }
            catch (Exception)
            {
#warning remove this.
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id);
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when a peer message is sent
        /// </summary>
        /// <param name="result"></param>
        private void onPeerMessageSent(IAsyncResult result)
        {
            bool cleanUp = false;
            PeerConnectionID id = (PeerConnectionID)result.AsyncState;
            result.AsyncWaitHandle.Close();
            
            try
            {
                lock (id.TorrentManager.listLock)
                    lock (id)
                    {
                        if (id.Peer.Connection == null)
                            return;

                        int bytesSent = id.Peer.Connection.EndSend(result, out id.ErrorCode);
                        if (id.ErrorCode != SocketError.Success)
                        {
                            cleanUp = true;
                            return;
                        }
                        if (bytesSent == 0)
                        {
                            cleanUp = true;
                            return;
                        }

                        id.Peer.Connection.BytesSent += bytesSent;
                        if (id.Peer.Connection.CurrentlySendingMessage is PieceMessage)
                            id.TorrentManager.DataBytesUploaded += bytesSent;
                        else
                            id.TorrentManager.ProtocolBytesUploaded += bytesSent;

                        id.Peer.Connection.Monitor.BytesSent(bytesSent);

                        if (id.Peer.Connection.BytesSent != id.Peer.Connection.BytesToSend)
                        {
                            if (id.TorrentManager.Settings.MaxUploadSpeed == 0)
                            {
                                id.Peer.Connection.BeginSend(id.Peer.Connection.sendBuffer, id.Peer.Connection.BytesSent, id.Peer.Connection.BytesToSend - id.Peer.Connection.BytesSent, SocketFlags.None, onPeerMessageSent, id, out id.ErrorCode);
                            }
                            else
                            {
                                id.TorrentManager.uploadQueue.Enqueue(id);
                                id.TorrentManager.ResumePeers();
                            }
                            return;
                        }
                        if (this.OnPeerMessages != null)
                            this.OnPeerMessages(id, new PeerMessageEventArgs((IPeerMessage)id.Peer.Connection.CurrentlySendingMessage, Direction.Outgoing));
                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.sendBuffer);
                        id.Peer.Connection.sendBuffer = null;
                        //Console.WriteLine("SENT " + id.Peer.Connection.CurrentlySendingMessage.ToString());
                        id.Peer.Connection.LastMessageSent = DateTime.Now;
                        this.ProcessQueue(id);
                    }
            }
            catch (SocketException ex)
            {
                Debug.WriteLine(ex.NativeErrorCode + ": " + ex.Message);
                cleanUp = true;
            }
            catch (ArgumentException)
            {
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id);
            }
        }
        #endregion


        #region Helper Methods
        /// <summary>
        /// This method should be called to begin processing messages stored in the SendQueue
        /// </summary>
        /// <param name="id">The peer whose message queue you want to start processing</param>
        internal void ProcessQueue(PeerConnectionID id)
        {
            if (id.Peer.Connection.QueueLength == 0)
            {
                id.Peer.Connection.ProcessingQueue = false;
                return;
            }

            IPeerMessageInternal msg = id.Peer.Connection.DeQueue();
            if (msg is PieceMessage)
                id.Peer.Connection.PiecesSent++;

            //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + " " + msg.ToString());

            id.Peer.Connection.ProcessingQueue = true;
            try
            {
                if (msg.ByteLength > BufferManager.SmallMessageBufferSize)
                    id.Peer.Connection.sendBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.LargeMessageBuffer);
                else
                    id.Peer.Connection.sendBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.SmallMessageBuffer);

                id.Peer.Connection.BytesSent = 0;
                id.Peer.Connection.BytesToSend = msg.Encode(id.Peer.Connection.sendBuffer, 0);
                id.Peer.Connection.CurrentlySendingMessage = msg;

                if (id.TorrentManager.Settings.MaxUploadSpeed == 0)
                {
                    id.Peer.Connection.BeginSend(id.Peer.Connection.sendBuffer, 0, id.Peer.Connection.BytesToSend, SocketFlags.None, onPeerMessageSent, id, out id.ErrorCode);
                }
                else
                {
                    id.TorrentManager.uploadQueue.Enqueue(id);
                    id.TorrentManager.ResumePeers();
                }
                return;
            }
            catch (SocketException ex)
            {
                Debug.WriteLine(ex.NativeErrorCode + ": " + ex.Message);
                CleanupSocket(id);
            }
        }


        /// <summary>
        /// Makes a peer start downloading/uploading
        /// </summary>
        /// <param name="id">The peer to resume</param>
        /// <param name="downloading">True if you want to resume downloading, false if you want to resume uploading</param>
        internal int ResumePeer(PeerConnectionID id, bool downloading)
        {
            int bytesRemaining;
            bool cleanUp = false;

            try
            {
                lock (id)
                {
                    if (id.Peer.Connection == null)
                    {
                        cleanUp = true;
                        return 0;
                    }
                    if (downloading)
                    {
                        bytesRemaining = (id.Peer.Connection.BytesToRecieve - id.Peer.Connection.BytesReceived) > ChunkLength ? ChunkLength : (id.Peer.Connection.BytesToRecieve - id.Peer.Connection.BytesReceived);
                        id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, id.Peer.Connection.BytesReceived, bytesRemaining, SocketFlags.None, peerMessageReceived, id, out id.ErrorCode);
                    }
                    else
                    {
                        bytesRemaining = (id.Peer.Connection.BytesToSend - id.Peer.Connection.BytesSent) > ChunkLength ? ChunkLength : (id.Peer.Connection.BytesToSend - id.Peer.Connection.BytesSent);
                        id.Peer.Connection.BeginSend(id.Peer.Connection.sendBuffer, id.Peer.Connection.BytesSent, bytesRemaining, SocketFlags.None, peerMessageSent, id, out id.ErrorCode);
                    }
                }

                return bytesRemaining;
            }
            catch (SocketException)
            {
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id);
            }
            return 0;
        }


        /// <summary>
        /// This method is called when the ClientEngine recieves a valid incoming connection
        /// </summary>
        /// <param name="result"></param>
        internal void IncomingConnectionAccepted(IAsyncResult result)
        {
            int bytesSent;
            bool cleanUp = false;
            PeerConnectionID id = (PeerConnectionID)result.AsyncState;

            try
            {
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        bytesSent = id.Peer.Connection.EndSend(result, out id.ErrorCode);
                        if (bytesSent == 0)
                        {
                            cleanUp = true;
                            return;
                        }

                        id.Peer.Connection.BytesSent += bytesSent;
                        if (bytesSent != id.Peer.Connection.BytesToSend)
                        {
                            id.Peer.Connection.BeginSend(id.Peer.Connection.sendBuffer, id.Peer.Connection.BytesSent, id.Peer.Connection.BytesToSend - id.Peer.Connection.BytesSent, SocketFlags.None, new AsyncCallback(this.IncomingConnectionAccepted), id, out id.ErrorCode);
                            return;
                        }

                        if (id.Peer.PeerId == ClientEngine.PeerId) // The tracker gave us our own IP/Port combination
                        {
                            CleanupSocket(id);
                            return;
                        }

                        if (id.TorrentManager.ConnectedPeers.Contains(id) || id.TorrentManager.ConnectingTo.Contains(id))
                        {
                            id.Peer.Connection.Dispose();
                            return;
                        }

                        id.TorrentManager.Available.Remove(id);
                        id.TorrentManager.ConnectedPeers.Add(id);

                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.sendBuffer);
                        id.Peer.Connection.sendBuffer = null;

                        id.Peer.Connection.recieveBuffer = ClientEngine.BufferManager.GetBuffer(BufferType.SmallMessageBuffer);
                        id.Peer.Connection.BytesReceived = 0;
                        id.Peer.Connection.BytesToRecieve = 4;
                        id.Peer.Connection.BeginReceive(id.Peer.Connection.recieveBuffer, id.Peer.Connection.BytesReceived, id.Peer.Connection.BytesToRecieve, SocketFlags.None, this.peerMessageLengthReceived, id, out id.ErrorCode);
                        if (this.OnPeerConnectionChanged != null)
                            this.OnPeerConnectionChanged(id, new PeerConnectionEventArgs(PeerConnectionEvent.IncomingConnectionReceived));
                    }
                }
            }
            catch (SocketException ex)
            {
                Debug.WriteLine(ex.NativeErrorCode + ": " + ex.Message);
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id);
            }
        }


        /// <summary>
        /// This method is called when a connection needs to be closed and the resources for it released.
        /// </summary>
        /// <param name="id">The peer whose connection needs to be closed</param>
        internal void CleanupSocket(PeerConnectionID id)
        {
            lock (id.TorrentManager.listLock)
            {
                lock (id)
                {
                    System.Threading.Interlocked.Decrement(ref openConnections);
                    id.TorrentManager.PieceManager.RemoveRequests(id);
                    //id.Peer.MessageHistory.AppendLine(DateTime.Now.ToShortTimeString() + " ****Cleaning Up****");

                    if (id.Peer.Connection != null)
                    {
                        if (this.OnPeerConnectionChanged != null)
                            this.OnPeerConnectionChanged(id, new PeerConnectionEventArgs(PeerConnectionEvent.Disconnected));

                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.sendBuffer);
                        ClientEngine.BufferManager.FreeBuffer(id.Peer.Connection.recieveBuffer);
                        id.Peer.Connection.sendBuffer = null;
                        id.Peer.Connection.recieveBuffer = null;


                        if (!id.Peer.Connection.AmChoking)
                            id.TorrentManager.UploadingTo--;

                        id.Peer.Connection.Dispose();
                        id.Peer.Connection = null;
                    }

                    id.TorrentManager.ConnectedPeers.Remove(id);
                    id.TorrentManager.ConnectingTo.Remove(id);
                    if (id.Peer.PeerId != ClientEngine.PeerId)
                        id.TorrentManager.Available.Add(id);
                }
            }
        }
        #endregion
    }
}
