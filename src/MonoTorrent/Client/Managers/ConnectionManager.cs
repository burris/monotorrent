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
using System.Collections.Generic;
using System.Net;

namespace MonoTorrent.Client
{
    internal delegate void MessagingCallback(PeerIdInternal id);

    /// <summary>
    /// Main controller class for all incoming and outgoing connections
    /// </summary>
    public class ConnectionManager
    {
        #region Events

        public event EventHandler<PeerConnectionEventArgs> PeerConnected;


        public event EventHandler<PeerConnectionEventArgs> PeerDisconnected;

        /// <summary>
        /// Event that's fired every time a message is sent or Received from a Peer
        /// </summary>
        public event EventHandler<PeerMessageEventArgs> PeerMessageTransferred;

        #endregion


        #region Member Variables
        private ClientEngine engine;
        public const int ChunkLength = 2096;   // Download in 2kB chunks to allow for better rate limiting

        // Create the callbacks and reuse them. Reduces ongoing allocations by a fair few megs
        private MessagingCallback bitfieldSentCallback;
        private MessagingCallback handshakeReceievedCallback;
        private MessagingCallback handshakeSentCallback;
        private MessagingCallback messageLengthReceivedCallback;
        private MessagingCallback messageReceivedCallback;
        private MessagingCallback messageSentCallback;

        private AsyncCallback endCreateConnectionCallback;
        private AsyncCallback incomingConnectionAcceptedCallback;
        private AsyncCallback onEndReceiveMessageCallback;
        private AsyncCallback onEndSendMessageCallback;

        private EncryptorReadyHandler onEncryptorReadyHandler;
        private EncryptorIOErrorHandler onEncryptorIOErrorHandler;
        private EncryptorEncryptionErrorHandler onEncryptorEncryptionErrorHandler;

        private MonoTorrentCollection<TorrentManager> torrents;

        internal MessageHandler MessageHandler
        {
            get { return this.messageHandler; }
        }
        private MessageHandler messageHandler;
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
            get { return this.engine.Settings.GlobalMaxHalfOpenConnections; }
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
            get { return this.engine.Settings.GlobalMaxConnections; }
        }
        #endregion


        #region Constructors

        /// <summary>
        /// 
        /// </summary>
        /// <param name="settings"></param>
        public ConnectionManager(ClientEngine engine)
        {
            this.engine = engine;

            this.onEndReceiveMessageCallback = new AsyncCallback(EndReceiveMessage);
            this.onEndSendMessageCallback = new AsyncCallback(EndSendMessage);
            this.bitfieldSentCallback = new MessagingCallback(this.onPeerBitfieldSent);
            this.endCreateConnectionCallback = new AsyncCallback(this.EndCreateConnection);
            this.handshakeSentCallback = new MessagingCallback(this.onPeerHandshakeSent);
            this.handshakeReceievedCallback = new MessagingCallback(this.onPeerHandshakeReceived);
            this.incomingConnectionAcceptedCallback = new AsyncCallback(IncomingConnectionAccepted);
            this.messageLengthReceivedCallback = new MessagingCallback(this.onPeerMessageLengthReceived);
            this.messageReceivedCallback = new MessagingCallback(this.onPeerMessageReceived);
            this.messageSentCallback = new MessagingCallback(this.onPeerMessageSent);
            this.torrents = new MonoTorrentCollection<TorrentManager>();
            this.onEncryptorReadyHandler = new EncryptorReadyHandler(onEncryptorReady);
            this.onEncryptorIOErrorHandler = new EncryptorIOErrorHandler(onEncryptorError);
            this.onEncryptorEncryptionErrorHandler = new EncryptorEncryptionErrorHandler(onEncryptorError);
            this.messageHandler = new MessageHandler();
        }

        #endregion


        #region Async Connection Methods

        internal void ConnectToPeer(TorrentManager manager, PeerIdInternal id)
        {
            // Connect to the peer.
            lock (id)
            {
                Logger.Log(id, "Connecting");
                manager.Peers.AddPeer(id.Peer, PeerType.Active);
                id.TorrentManager.ConnectingToPeers.Add(id);
                System.Threading.Interlocked.Increment(ref this.halfOpenConnections);

                IEncryptorInternal encryptor;
                if(!ClientEngine.SupportsEncryption || id.Peer.EncryptionSupported == EncryptionMethods.NoEncryption)
                    encryptor = new NoEncryption();
                else
                    encryptor = new PeerAEncryption(manager.Torrent.InfoHash, this.engine.Settings.MinEncryptionLevel);

                encryptor.SetPeerConnectionID(id);
                encryptor.EncryptorReady += onEncryptorReadyHandler;
                encryptor.EncryptorIOError += onEncryptorIOErrorHandler;
                encryptor.EncryptorEncryptionError += onEncryptorEncryptionErrorHandler;

                id.Connection = new TCPConnection(id.Peer.Location, id.TorrentManager.Torrent.Pieces.Count, encryptor);
                id.Connection.ProcessingQueue = true;
                id.Peer.LastConnectionAttempt = DateTime.Now;
                id.Connection.LastMessageSent = DateTime.Now;
                id.Connection.LastMessageReceived = DateTime.Now;
                try
                {
                    id.Connection.BeginConnect(this.endCreateConnectionCallback, id);
                }
                catch (SocketException)
                {
                    // If there's a socket exception at this point, just drop the peer's details silently
                    // as they must be invalid.
                    manager.Peers.RemovePeer(id.Peer, PeerType.Active);
                    id.TorrentManager.ConnectingToPeers.Remove(id);
                    System.Threading.Interlocked.Decrement(ref this.halfOpenConnections);
                }
            }

            // Try to connect to another peer
            TryConnect();
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when we try to create a remote connection
        /// </summary>
        /// <param name="result"></param>
        private void EndCreateConnection(IAsyncResult result)
        {
            bool fireConnected = false;
            bool cleanUp = false;
            string reason = null;
            PeerIdInternal id = (PeerIdInternal)result.AsyncState;

            try
            {
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        // If the peer has been cleaned up, then don't continue processing the peer
                        if (id.Connection == null)
                        {
                            Logger.Log(id, "Connection null");
                            return;
                        }

                        id.Connection.EndConnect(result);
                        Logger.Log(id, "Connected");

                        // Remove the peer from the "connecting" list and put them in the "connected" list
                        // because we have now successfully connected to them
                        id.TorrentManager.ConnectingToPeers.Remove(id);
                        id.TorrentManager.ConnectedPeers.Add(id);

                        id.PublicId = new PeerId();
                        id.UpdatePublicStats();
                        fireConnected = true;

                        // If we have too many open connections, close the connection
                        if (this.openConnections > this.MaxOpenConnections)
                        {
                            Logger.Log(id, "Too many connections");
                            reason = "Too many connections";
                            cleanUp = true;
                            return;
                        }

                        // Increase the count of the "open" connections
                        System.Threading.Interlocked.Increment(ref this.openConnections);

                        // Create a handshake message to send to the peer
                        HandshakeMessage handshake = new HandshakeMessage(id.TorrentManager.Torrent.InfoHash, engine.PeerId, VersionInfo.ProtocolStringV100);

                        if (id.Connection.Encryptor is NoEncryption || !ClientEngine.SupportsEncryption)
                        {
                            SendMessage(id, handshake, this.handshakeSentCallback);
                        }
                        else
                        {
                            ClientEngine.BufferManager.GetBuffer(ref id.Connection.sendBuffer, handshake.ByteLength);

                            id.Connection.BytesSent = 0;
                            id.Connection.BytesToSend += handshake.Encode(id.Connection.sendBuffer, 0);

                            // Get a buffer to encode the handshake into, encode the message and send it

                            id.Connection.Encryptor.AddInitialData(id.Connection.sendBuffer.Array, id.Connection.sendBuffer.Offset, id.Connection.BytesToSend);
                            id.Connection.StartEncryption();
                        }
                    }
                }
            }

            catch (SocketException ex)
            {
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        fireConnected = false;
                        Logger.Log(id, "failed to connect " + ex.Message);
                        id.Peer.FailedConnectionAttempts++;

                        if (id.Connection != null)
                        {
                            ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);
                            id.Connection.Dispose();
                        }

                        id.Connection = null;
                        id.NulledAt = "1";
                        id.TorrentManager.Peers.RemovePeer(id.Peer, PeerType.Active);
                        id.TorrentManager.ConnectingToPeers.Remove(id);

                        if (id.Peer.FailedConnectionAttempts < 2)   // We couldn't connect this time, so re-add to available
                            id.TorrentManager.Peers.AddPeer(id.Peer, PeerType.Available);
                        else
                            id.TorrentManager.Peers.AddPeer(id.Peer, PeerType.Busy);
                    }
                }
            }
            catch (ArgumentException ex)
            {
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        fireConnected = false;
                        Logger.Log(id, "failed to connect " + ex.Message);
                        id.Peer.FailedConnectionAttempts++;

                        if (id.Connection != null)
                        {
                            ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);
                            id.Connection.Dispose();
                        }

                        id.Connection = null;
                        id.NulledAt = "2";
                        id.TorrentManager.Peers.RemovePeer(id.Peer, PeerType.Active);
                        id.TorrentManager.ConnectingToPeers.Remove(id);

                        if (id.Peer.FailedConnectionAttempts < 2)   // We couldn't connect this time, so re-add to available
                            id.TorrentManager.Peers.AddPeer(id.Peer, PeerType.Available);
                        else
                            id.TorrentManager.Peers.AddPeer(id.Peer, PeerType.Busy);
                    }
                }
            }
            finally
            {
                if(fireConnected)
                    RaisePeerConnected(new PeerConnectionEventArgs(id.TorrentManager, id, Direction.Outgoing));

                // Decrement the half open connections
                System.Threading.Interlocked.Decrement(ref this.halfOpenConnections);
                if (cleanUp)
                    CleanupSocket(id, reason);

                // Try to connect to another peer
                TryConnect();
            }
        }


        private void EndReceiveMessage(IAsyncResult result)
        {
            string reason = null;
            bool cleanUp = false;
            PeerIdInternal id = (PeerIdInternal)result.AsyncState;

            try
            {
                lock (id.TorrentManager.listLock)
                    lock (id)
                    {
                        // If the connection is null, just return
                        if (id.Connection == null)
                            return;

                        // If we receive 0 bytes, the connection has been closed, so exit
                        int bytesReceived = id.Connection.EndReceive(result, out id.ErrorCode);

						// Recycle the connection if we got an error
						if (id.ErrorCode != SocketError.Success)
						{
							Logger.Log(id, "EndReceiveMessage: Error from EndReceive: " + id.ErrorCode.ToString());
							CleanupSocket(id, "EndReceiveMessage: Error from EndReceive: " + id.ErrorCode.ToString());
							return;
						}

						// Recycle the connection if we get a zero length return - indicates a lost connection
						if (bytesReceived == 0)
						{
							Logger.Log(id, "EndReceiveMessage: EndReceive returned empty string");
							CleanupSocket(id, "EndReceiveMessage: EndReceive returned empty string");
							return;
						}

                        // If the first byte is '7' and we're receiving more than 256 bytes (a made up number)
                        // then this is a piece message, so we add it as "data", not protocol. 256 bytes should filter out
                        // any non piece messages that happen to have '7' as the first byte.
                        TransferType type = (id.Connection.recieveBuffer.Array[id.Connection.recieveBuffer.Offset] == PieceMessage.MessageId && id.Connection.BytesToRecieve > 256) ? TransferType.Data : TransferType.Protocol;
                        id.Connection.ReceivedBytes(bytesReceived, type);
                        id.TorrentManager.Monitor.BytesReceived(bytesReceived, type);

                        // If we don't have the entire message, recieve the rest
                        if (id.Connection.BytesReceived < id.Connection.BytesToRecieve)
                        {
                            id.TorrentManager.downloadQueue.Add(id);
                            id.TorrentManager.ResumePeers();
                            return;
                        }
                        else
                        {
                            // Invoke the callback we were told to invoke once the message had been received fully
                            id.Connection.MessageReceivedCallback(id);
                        }
                    }
            }

            catch (SocketException ex)
            {
                Logger.Log(id, "Exception recieving message" + ex.ToString());
                reason = "Socket Exception receiving";
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id, reason);
            }
        }


        private void EndSendMessage(IAsyncResult result)
        {
            string reason = null;
            bool cleanup = false;
            PeerIdInternal id = (PeerIdInternal)result.AsyncState;

            try
            {
                lock (id.TorrentManager.listLock)
                    lock (id)
                    {
                        // If the peer has disconnected, don't continue
                        if (id.Connection == null)
                            return;

                        // If we have sent zero bytes, that is a sign the connection has been closed
                        int bytesSent = id.Connection.EndSend(result, out id.ErrorCode);
                        if (id.ErrorCode != SocketError.Success || bytesSent == 0)
                        {
                            reason = "Sending error: " + id.ErrorCode.ToString();
                            Logger.Log(id, "Couldn't send message");
                            cleanup = true;
                            return;
                        }

                        // Log the data sent in both the peers and torrentmangers connection monitors
                        TransferType type = (id.Connection.CurrentlySendingMessage is PieceMessage) ? TransferType.Data : TransferType.Protocol;
                        id.Connection.SentBytes(bytesSent, type);
                        id.TorrentManager.Monitor.BytesSent(bytesSent, type);

                        // If we havn't sent everything, send the rest of the data
                        if (id.Connection.BytesSent != id.Connection.BytesToSend)
                        {
                            id.TorrentManager.uploadQueue.Add(id);
                            id.TorrentManager.ResumePeers();
                            return;
                        }
                        else
                        {
                            // Invoke the callback which we were told to invoke after we sent this message
                            id.Connection.MessageSentCallback(id);
                        }
                    }
            }
            catch (SocketException)
            {
                reason = "Exception EndSending";
                Logger.Log("Socket exception sending message");
                cleanup = true;
            }
            catch (ArgumentException ex)
            {
                reason = "FECKIN ARGUMENT EXCEPTIONS ENDSENDING!";
                cleanup = true;
                Logger.Log(id, ex.ToString());
            }
            finally
            {
                if (cleanup)
                    CleanupSocket(id, reason);
            }
        }


        private void onPeerHandshakeSent(PeerIdInternal id)
        {
            lock (id.TorrentManager.listLock)
            lock (id)
            {
                Logger.Log(id, "Sent Handshake");
                Logger.Log(id, "Recieving handshake");

                // Receive the handshake
                // FIXME: Will fail if protocol version changes. FIX THIS
                //ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);
                ReceiveMessage(id, 68, this.handshakeReceievedCallback);
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when we recieve a peer handshake
        /// </summary>
        /// <param name="result"></param>
        private void onPeerHandshakeReceived(PeerIdInternal id)
        {
            string reason = null;
            bool cleanUp = false;
            IPeerMessageInternal msg;

            try
            {
                lock (id.TorrentManager.listLock)
                lock (id)
                {
                    // If the connection is closed, just return
                    if (id.Connection == null)
                        return;

                    // Decode the handshake and handle it
                    msg = new HandshakeMessage();
                    msg.Decode(id.Connection.recieveBuffer, 0, id.Connection.BytesToRecieve);
                    msg.Handle(id);

                    Logger.Log(id, "Handshake recieved");
                    //HandshakeMessage handshake = msg as HandshakeMessage;

                    if (id.Connection.SupportsFastPeer && ClientEngine.SupportsFastPeer)
                    {
                        if (id.TorrentManager.Bitfield.AllFalse)
                            msg = new HaveNoneMessage();

                        else if (id.TorrentManager.Bitfield.AllTrue)
                            msg = new HaveAllMessage();

                        else
                            msg = new BitfieldMessage(id.TorrentManager.Bitfield);
                    }
                    else
                    {
                        msg = new BitfieldMessage(id.TorrentManager.Bitfield);
                    }

                    //ClientEngine.BufferManager.FreeBuffer(ref id.Connection.recieveBuffer);
                    SendMessage(id, msg, this.bitfieldSentCallback);
                }
            }
            catch (TorrentException)
            {
                reason = "Couldn't decode handshake";
                cleanUp = true;
                return;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id, reason);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="result"></param>
        private void onPeerBitfieldSent(PeerIdInternal id)
        {
            lock (id.TorrentManager.listLock)
            lock (id)
            {
                if (id.Connection == null)
                    return;

                // Free the old buffer and get a new one to recieve the length of the next message
                //ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);

                // Now we will enqueue a FastPiece message for each piece we will allow the peer to download
                // even if they are choked
                if (ClientEngine.SupportsFastPeer && id.Connection.SupportsFastPeer)
                    for (int i = 0; i < id.Connection.AmAllowedFastPieces.Count; i++)
                        id.Connection.Enqueue(new AllowedFastMessage(id.Connection.AmAllowedFastPieces[i]));

                // Allow the auto processing of the send queue to commence
                id.Connection.ProcessingQueue = false;

                ReceiveMessage(id, 4, this.messageLengthReceivedCallback);
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when we recieve a peer message length
        /// </summary>
        /// <param name="result"></param>
        private void onPeerMessageLengthReceived(PeerIdInternal id)
        {
            lock (id.TorrentManager.listLock)
            lock (id)
            {
                // If the connection is null, we just return
                if (id.Connection == null)
                    return;

                Logger.Log(id, "Recieved message length");

                // Decode the message length from the buffer. It is a big endian integer, so make sure
                // it is converted to host endianness.
                int messageBodyLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(id.Connection.recieveBuffer.Array, id.Connection.recieveBuffer.Offset));

                // Free the existing receive buffer and then get a new one which can
                // contain the amount of bytes we need to receive.
                //ClientEngine.BufferManager.FreeBuffer(ref id.Connection.recieveBuffer);

                // If bytes to receive is zero, it means we received a keep alive message
                // so we just start receiving a new message length again
                if (messageBodyLength == 0)
                {
                    id.Connection.LastMessageReceived = DateTime.Now;
                    ReceiveMessage(id, 4, this.messageLengthReceivedCallback);
                }

                // Otherwise queue the peer in the Receive buffer and try to resume downloading off him
                else
                {
                    Logger.Log(id, "Recieving message");
                    ReceiveMessage(id, messageBodyLength, this.messageReceivedCallback);
                }
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when we recieve a peer message
        /// </summary>
        /// <param name="result"></param>
        private void onPeerMessageReceived(PeerIdInternal id)
        {
            string reason = null;
            bool cleanUp = false;

            try
            {
                lock (id.TorrentManager.listLock)
                lock (id)
                {
                    if (id.Connection == null)
                        return;

                    this.messageHandler.EnqueueReceived(id, id.Connection.recieveBuffer, 0, id.Connection.BytesToRecieve);


                    // if the peer has sent us three bad pieces, we close the connection.
                    if (id.Peer.TotalHashFails == 3)
                    {
                        reason = "3 hashfails";
                        Logger.Log(id, "3 hashfails");
                        cleanUp = true;
                        return;
                    }

                    id.Connection.LastMessageReceived = DateTime.Now;

                    // Free the large buffer used to recieve the piece message and get a small buffer
                    //ClientEngine.BufferManager.FreeBuffer(ref id.Connection.recieveBuffer);

                    ReceiveMessage(id, 4, this.messageLengthReceivedCallback);
                }
            }
            catch (TorrentException ex)
            {
                reason = ex.Message;
                Logger.Log(id, "Invalid message recieved: " + ex.Message);
                cleanUp = true;
                return;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id, true, reason);
            }
        }


        /// <summary>
        /// This method is called as part of the AsyncCallbacks when a peer message is sent
        /// </summary>
        /// <param name="result"></param>
        private void onPeerMessageSent(PeerIdInternal id)
        {
            lock (id.TorrentManager.listLock)
            lock (id)
            {
                // If the peer has been cleaned up, just return.
                if (id.Connection == null)
                    return;

                // Fire the event to let the user know a message was sent
                RaisePeerMessageTransferred(new PeerMessageEventArgs(id.TorrentManager, (IPeerMessage)id.Connection.CurrentlySendingMessage, Direction.Outgoing, id));

                //ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);
                Logger.Log(id, "Sent message: " + id.Connection.CurrentlySendingMessage.ToString());
                id.Connection.LastMessageSent = DateTime.Now;
                this.ProcessQueue(id);
            }
        }


        /// <summary>
        /// Receives exactly length number of bytes from the specified peer connection and invokes the supplied callback if successful
        /// </summary>
        /// <param name="id">The peer to receive the message from</param>
        /// <param name="length">The length of the message to receive</param>
        /// <param name="callback">The callback to invoke when the message has been received</param>
        private void ReceiveMessage(PeerIdInternal id, int length, MessagingCallback callback)
        {
            ArraySegment<byte> newBuffer = BufferManager.EmptyBuffer;
            bool cleanUp = false;
            try
            {
                lock (id.TorrentManager.listLock)
                    lock (id)
                    {
                        if (id.Connection == null)
                            return;

                        if (length > RequestMessage.MaxSize)
                        {
                            Logger.Log("* * * * * *");
                            Logger.Log(id.Connection.ClientApp.PeerId + " tried to send too much data: " + length.ToString() + " byte");
                            Logger.Log("* * * * * *");

                            cleanUp = true;
                            return;
                        }

                        int alreadyReceived = id.Connection.BytesReceived - id.Connection.BytesToRecieve;
                        ClientEngine.BufferManager.GetBuffer(ref newBuffer, Math.Max(alreadyReceived, length));
                        ArraySegment<byte> buffer = id.Connection.recieveBuffer;

                        // Copy the extra data from the old buffer into the new buffer.
                        Buffer.BlockCopy(id.Connection.recieveBuffer.Array,
                            id.Connection.recieveBuffer.Offset + id.Connection.BytesToRecieve,
                            newBuffer.Array,
                            newBuffer.Offset,
                            alreadyReceived);

                        // Free the old buffer and set the new buffer
                        ClientEngine.BufferManager.FreeBuffer(ref id.Connection.recieveBuffer);
                        id.Connection.recieveBuffer = newBuffer;

                        id.Connection.BytesReceived = alreadyReceived;
                        id.Connection.BytesToRecieve = length;
                        id.Connection.MessageReceivedCallback = callback;

                        if (alreadyReceived < length)
                        {
                            id.TorrentManager.downloadQueue.Add(id);
                            id.TorrentManager.ResumePeers();
                        }
                        else
                        {
                            id.Connection.MessageReceivedCallback(id);
                        }
                    }
            }
            catch (SocketException)
            {
                cleanUp = true;
            }
            finally
            {
                if(cleanUp)
                    CleanupSocket(id, "Couldn't Receive Message");

            }
        }


        /// <summary>
        /// Sends the specified message to the specified peer and invokes the supplied callback if successful
        /// </summary>
        /// <param name="id">The peer to send the message to</param>
        /// <param name="message">The  message to send</param>
        /// <param name="callback">The callback to invoke when the message has been sent</param>
        private void SendMessage(PeerIdInternal id, IPeerMessageInternal message, MessagingCallback callback)
        {
            bool cleanup = false;

            try
            {
                lock (id.TorrentManager.listLock)
                    lock (id)
                    {
                        if (id.Connection == null)
                            return;
                        ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);
                        ClientEngine.BufferManager.GetBuffer(ref id.Connection.sendBuffer, message.ByteLength);
                        id.Connection.MessageSentCallback = callback;
                        id.Connection.CurrentlySendingMessage = message;
                        if (message is PieceMessage)
                            id.Connection.IsRequestingPiecesCount--;

                        id.Connection.BytesSent = 0;
                        id.Connection.BytesToSend = message.Encode(id.Connection.sendBuffer, 0);

                        id.TorrentManager.uploadQueue.Add(id);
                        id.TorrentManager.ResumePeers();
                    }
            }
            catch (SocketException)
            {
                Logger.Log("SocketException in SendMessage");
                cleanup = true;
            }
            finally
            {
                if (cleanup)
                    CleanupSocket(id, "Couldn't SendMessage");
            }
        }

        #endregion


        #region Methods

        internal void AsyncCleanupSocket(PeerIdInternal id, bool localClose, string message)
        {
            if (id == null) // Sometimes onEncryptoError will fire with a null id
                return;

            try
            {
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        // It's possible the peer could be in an async send *and* receive and so end up
                        // in this block twice. This check makes sure we don't try to double dispose.
                        if (id.Connection == null)
                            return;

                        Console.WriteLine("Cleaned Up: " + id.Peer.Location);
                        Logger.Log(id, "Cleanup Reason : " + message);
                        Logger.FlushToDisk(id);

                        Logger.Log(id, "*******Cleaning up*******");
                        System.Threading.Interlocked.Decrement(ref this.openConnections);
                        id.TorrentManager.PieceManager.RemoveRequests(id);
                        id.Peer.CleanedUpCount++;
                        id.Peer.ActiveReceive = false;
                        id.Peer.ActiveSend = false;
                        if (id.PublicId != null)
                            id.PublicId.IsValid = false;

                        ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);
                        ClientEngine.BufferManager.FreeBuffer(ref id.Connection.recieveBuffer);

                        if (!id.Connection.AmChoking)
                            id.TorrentManager.UploadingTo--;

                        id.Connection.Dispose();
                        id.Connection = null;
                        id.NulledAt = "3";
                        Console.WriteLine("Nulling: " + id.Peer.Location);

                        id.TorrentManager.uploadQueue.RemoveAll(delegate(PeerIdInternal other) { return id == other; });
                        id.TorrentManager.downloadQueue.RemoveAll(delegate(PeerIdInternal other) { return id == other; });
                        id.TorrentManager.ConnectingToPeers.RemoveAll(delegate(PeerIdInternal other) { return id == other; });
                        id.TorrentManager.ConnectedPeers.RemoveAll(delegate(PeerIdInternal other) { return id == other; });

                        if (id.TorrentManager.Peers.ActivePeers.Contains(id.Peer))
                            id.TorrentManager.Peers.RemovePeer(id.Peer, PeerType.Active);

                        // If we get our own details, this check makes sure we don't try connecting to ourselves again
                        if (id.Peer.PeerId != engine.PeerId)
                        {
                            if (!id.TorrentManager.Peers.AvailablePeers.Contains(id.Peer) && id.Peer.CleanedUpCount < 5)
                                id.TorrentManager.Peers.AvailablePeers.Insert(0, id.Peer);
                        }
                    }
                }
            }

            finally
            {
                RaisePeerDisconnected(new PeerConnectionEventArgs(id.TorrentManager, id, Direction.None));
                TryConnect();
            }
        }


        private void AsyncPeerConnected(object args)
        {
            if (this.PeerConnected != null)
                this.PeerConnected(null, (PeerConnectionEventArgs)args);
        }


        private void AsyncPeerMessageTransferred(object args)
        {
            if (this.PeerMessageTransferred != null)
                this.PeerMessageTransferred(null, (PeerMessageEventArgs)args);
        }


        private void AsyncPeerDisconnected(object args)
        {
            if (this.PeerDisconnected != null)
                PeerDisconnected(null, (PeerConnectionEventArgs)args);
        }


        /// <summary>
        /// This method is called when a connection needs to be closed and the resources for it released.
        /// </summary>
        /// <param name="id">The peer whose connection needs to be closed</param>
        internal void CleanupSocket(PeerIdInternal id, string message)
        {
            CleanupSocket(id, true, message);
        }


        internal void CleanupSocket(PeerIdInternal id, bool localClose, string message)
        {
            this.messageHandler.EnqueueCleanup(id);
        }


        /// <summary>
        /// This method is called when the ClientEngine recieves a valid incoming connection
        /// </summary>
        /// <param name="result"></param>
        internal void IncomingConnectionAccepted(IAsyncResult result)
        {
            string reason = null;
            int bytesSent;
            bool cleanUp = false;
            PeerIdInternal id = (PeerIdInternal)result.AsyncState;

            try
            {
                lock (id.TorrentManager.listLock)
                {
                    lock (id)
                    {
                        Interlocked.Increment(ref this.openConnections);
                        bytesSent = id.Connection.EndSend(result, out id.ErrorCode);
                        if (id.ErrorCode != SocketError.Success)
                        {
                            Logger.Log(id, "Sent 0 for incoming connection accepted");
                            reason = "IncomingConnectionAccepted: " + id.ErrorCode.ToString();
                            cleanUp = true;
                            return;
                        }

                        id.Connection.BytesSent += bytesSent;
                        if (bytesSent != id.Connection.BytesToSend)
                        {
                            id.Connection.BeginSend(id.Connection.sendBuffer, id.Connection.BytesSent, id.Connection.BytesToSend - id.Connection.BytesSent, SocketFlags.None, this.incomingConnectionAcceptedCallback, id, out id.ErrorCode);
                            return;
                        }

                        if (id.Peer.PeerId == engine.PeerId) // The tracker gave us our own IP/Port combination
                        {
                            Logger.Log(id, "Recieved myself");
                            reason = "Received myself";
                            cleanUp = true;
                            return;
                        }

                        if (id.TorrentManager.Peers.ActivePeers.Contains(id.Peer))
                        {
                            Logger.Log(id, "Already connected to peer");
                            id.Connection.Dispose();
                            return;
                        }

                        Logger.Log(id, "Peer accepted ok");
                        id.TorrentManager.Peers.RemovePeer(id.Peer, PeerType.Available);
                        id.TorrentManager.Peers.AddPeer(id.Peer, PeerType.Active);
                        id.TorrentManager.ConnectedPeers.Add(id);

                        //ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);

                        id.PublicId = new PeerId();
                        id.UpdatePublicStats();
                        RaisePeerConnected(new PeerConnectionEventArgs(id.TorrentManager, id, Direction.Incoming));

                        if (this.openConnections >= Math.Min(this.MaxOpenConnections, id.TorrentManager.Settings.MaxConnections))
                        {
                            reason = "Too many peers";
                            cleanUp = true;
                            return;
                        }
                        Logger.Log(id, "Recieving message length");
                        ClientEngine.BufferManager.GetBuffer(ref id.Connection.recieveBuffer, 68);
                        ReceiveMessage(id, 4, this.messageLengthReceivedCallback);
                    }
                }
            }
            catch (SocketException)
            {
                reason = "Exception for incoming connection";
                Logger.Log(id, "Exception when accepting peer");
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id, reason);
            }
        }


        internal void onEncryptorReady(PeerIdInternal id)
        {
            try
            {
                //ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);
                ReceiveMessage(id, 68, this.handshakeReceievedCallback);
            }
            catch (SocketException)
            {
                CleanupSocket(id, "Exception on encryptor");
            }
            catch (NullReferenceException)
            {
                CleanupSocket(id, "Null Ref for encryptor");
            }
        }


        internal void onEncryptorError(PeerIdInternal id)
        {
            try
            {
                //FIXME: #warning Terrible logic actually. I need to fix this.
                id.Peer.EncryptionSupported = EncryptionMethods.NoEncryption;
                CleanupSocket(id, "Encryptor error... somewhere");
                return;

                /*
                if (id.Connection != null)
                {
                    ClientEngine.BufferManager.FreeBuffer(ref id.Connection.sendBuffer);
                    id.Connection.Dispose();
                }

                //id.Connection = null;
                //id.TorrentManager.Peers.RemovePeer(id, PeerType.Connecting);

                if (this.engine.Settings.MinEncryptionLevel == EncryptionType.None)
                    id.TorrentManager.Peers.AddPeer(id.Peer, PeerType.Available);*/
            }
            catch (SocketException)
            {
                CleanupSocket(id, "Encryptor error");
            }
            catch (NullReferenceException)
            {
                CleanupSocket(id, "Null ref encryptor error");
            }
        }


        /// <summary>
        /// This method should be called to begin processing messages stored in the SendQueue
        /// </summary>
        /// <param name="id">The peer whose message queue you want to start processing</param>
        internal void ProcessQueue(PeerIdInternal id)
        {
            if (id.Connection.QueueLength == 0)
            {
                id.Connection.ProcessingQueue = false;
                return;
            }

            IPeerMessageInternal msg = id.Connection.Dequeue();
            if (msg is PieceMessage)
                id.Connection.PiecesSent++;

            id.Connection.ProcessingQueue = true;
            try
            {
                SendMessage(id, msg, this.messageSentCallback);
                Logger.Log(id, "Sending message from queue: " + msg.ToString());
            }
            catch (SocketException)
            {
                Logger.Log(id, "Exception dequeuing message");
                CleanupSocket(id, "Exception calling SendMessage");
            }
        }


        internal void RaisePeerDisconnected(PeerConnectionEventArgs peerConnectionEventArgs)
        {
            if (this.PeerDisconnected != null)
                ThreadPool.QueueUserWorkItem(new WaitCallback(AsyncPeerDisconnected), peerConnectionEventArgs);
        }


        private void RaisePeerConnected(PeerConnectionEventArgs args)
        {
            if (this.PeerConnected != null)
                ThreadPool.QueueUserWorkItem(new WaitCallback(AsyncPeerConnected), args);
        }


        internal void RaisePeerMessageTransferred(PeerMessageEventArgs peerMessageEventArgs)
        {
            if (this.PeerMessageTransferred != null)
                ThreadPool.QueueUserWorkItem(new WaitCallback(AsyncPeerMessageTransferred), peerMessageEventArgs);
        }


        internal void RegisterManager(TorrentManager torrentManager)
        {
            lock (this.torrents)
            {
                if (!this.messageHandler.IsActive)
                    this.messageHandler.Start();

                if (this.torrents.Contains(torrentManager))
                    throw new TorrentException("TorrentManager is already registered in the connection manager");

                this.torrents.Add(torrentManager);
            }
            TryConnect();
        }


        /// <summary>
        /// Makes a peer start downloading/uploading
        /// </summary>
        /// <param name="id">The peer to resume</param>
        /// <param name="downloading">True if you want to resume downloading, false if you want to resume uploading</param>
        internal int ResumePeer(PeerIdInternal id, bool downloading)
        {
            // We attempt to send/receive 'bytecount' number of bytes
            int byteCount;
            bool cleanUp = false;

            try
            {
                lock (id)
                {
                    if (id.Connection == null)
                    {
                        cleanUp = true;
                        return 0;
                    }
                    if (downloading)
                    {
                        byteCount = id.Connection.recieveBuffer.Count - id.Connection.BytesReceived > ChunkLength ? ChunkLength : id.Connection.recieveBuffer.Count - id.Connection.BytesReceived;
                        id.Connection.BeginReceive(id.Connection.recieveBuffer, id.Connection.BytesReceived, byteCount, SocketFlags.None, this.onEndReceiveMessageCallback, id, out id.ErrorCode);
                    }
                    else
                    {
                        byteCount = (id.Connection.BytesToSend - id.Connection.BytesSent) > ChunkLength ? ChunkLength : (id.Connection.BytesToSend - id.Connection.BytesSent);
                        id.Connection.BeginSend(id.Connection.sendBuffer, id.Connection.BytesSent, byteCount, SocketFlags.None, this.onEndSendMessageCallback, id, out id.ErrorCode);
                    }
                }

                return byteCount;
            }
            catch (SocketException)
            {
                Logger.Log("SocketException resuming");
                cleanUp = true;
            }
            finally
            {
                if (cleanUp)
                    CleanupSocket(id, "Exception resuming");
            }
            return 0;
        }


        internal void TryConnect()
        {
            int i;
            PeerIdInternal id;
            TorrentManager m = null;

            // If we have already reached our max connections globally, don't try to connect to a new peer
            if ((this.openConnections >= this.MaxOpenConnections) || this.halfOpenConnections >= this.MaxHalfOpenConnections)
                return;

            // Check each torrent manager in turn to see if they have any peers we want to connect to
            lock (this.torrents)
            {
                foreach (TorrentManager manager in this.torrents)
                {
                    // If we have reached the max peers allowed for this torrent, don't connect to a new peer for this torrent
                    if (manager.ConnectedPeers.Count >= manager.Settings.MaxConnections)
                        continue;

                    // If the torrent isn't active, don't connect to a peer for it
                    if (manager.State != TorrentState.Downloading && manager.State != TorrentState.Seeding)
                        continue;

                    // If we are not seeding, we can connect to anyone. If we are seeding, we should only connect to a peer
                    // if they are not a seeder.
                    lock (manager.listLock)
                    {
                        for (i = 0; i < manager.Peers.AvailablePeers.Count; i++)
                            if ((manager.State != TorrentState.Seeding ||
                               (manager.State == TorrentState.Seeding && !manager.Peers.AvailablePeers[i].IsSeeder))
                                && (!manager.Peers.AvailablePeers[i].ActiveReceive)
                                && (!manager.Peers.AvailablePeers[i].ActiveSend))
                                break;

                        // If this is true, there were no peers in the available list to connect to.
                        if (i == manager.Peers.AvailablePeers.Count)
                            continue;

                        // Remove the peer from the lists so we can start connecting to him
                        id = new PeerIdInternal(manager.Peers.AvailablePeers[i], manager);
                        manager.Peers.AvailablePeers.RemoveAt(i);

                        // Save the manager we're using so we can place it to the end of the list
                        m = manager;

                        // Connect to the peer
                        ConnectToPeer(manager, id);
                        break;
                    }
                }

                if (m == null)
                    return;

                // Put the manager at the end of the list so we try the other ones next
                this.torrents.Remove(m);
                this.torrents.Add(m);
            }
        }


        internal void UnregisterManager(TorrentManager torrentManager)
        {
            lock (this.torrents)
            {
                if (!this.torrents.Contains(torrentManager))
                    throw new TorrentException("TorrentManager is not registered in the connection manager");

                this.torrents.Remove(torrentManager);

                if (this.messageHandler.IsActive && this.torrents.Count == 0)
                    this.messageHandler.Stop();
            }
        }

        #endregion

        internal bool IsRegistered(TorrentManager torrentManager)
        {
            return this.torrents.Contains(torrentManager);
        }
    }
}
