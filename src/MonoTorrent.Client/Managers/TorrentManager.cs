//
// TorrentManager.cs
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
using System.Collections.Generic;
using System.Text;
using System.Net;
using MonoTorrent.Client.PeerMessages;
using MonoTorrent.Common;
using System.Threading;

namespace MonoTorrent.Client
{
    /// <summary>
    /// 
    /// </summary>
    public class TorrentManager : ITorrentManager, IDisposable
    {
        #region Events
        /// <summary>
        /// Event that's fired every time new peers are added from a tracker update
        /// </summary>
        public event EventHandler<PeersAddedEventArgs> OnPeersAdded;


        /// <summary>
        /// Event that's fired every time a piece is hashed
        /// </summary>
        public event EventHandler<PieceHashedEventArgs> OnPieceHashed;


        /// <summary>
        /// Event that's fired every time the TorrentManagers state changes
        /// </summary>
        public event EventHandler<TorrentStateChangedEventArgs> OnTorrentStateChanged;
        #endregion


        #region Member Variables
        internal Queue<PeerConnectionID> downloadQueue;
        internal Queue<PeerConnectionID> uploadQueue;

#warning TorrentState should now be enough to check if we are hashing
        /// <summary>
        /// True if we are currently hashchecking
        /// </summary>
        public bool AmHashing
        {
            get { return this.amHashing; }
        }
        private bool amHashing;


        /// <summary>
        /// The Torrent contained within this TorrentManager
        /// </summary>
        public ITorrent Torrent
        {
            get { return this.torrent; }
        }
        private Torrent torrent;


        /// <summary>
        /// The settings for with this TorrentManager
        /// </summary>
        public TorrentSettings Settings
        {
            get { return this.settings; }
            set { this.settings = value; }
        }
        private TorrentSettings settings;


        /// <summary>
        /// The current state of the TorrentManager
        /// </summary>
        public TorrentState State
        {
            get { return this.state; }
        }
        private TorrentState state;


        /// <summary>
        /// The tracker connection associated with this TorrentManager
        /// </summary>
        public TrackerManager TrackerConnection
        {
            get { return this.trackerConnection; }
        }
        private TrackerManager trackerConnection;


        /// <summary>
        /// The piecemanager for this TorrentManager
        /// </summary>
        public PieceManager PieceManager
        {
            get { return this.pieceManager; }
            //set { this.pieceManager = (PieceManager) value; }
        }
        private PieceManager pieceManager;


        IPieceManager ITorrentManager.PieceManager
        {
            get { return this.pieceManager; }
        }


        /// <summary>
        /// The DiskManager associated with this torrent
        /// </summary>
        public FileManager DiskManager
        {
            get { return this.diskManager; }
        }
        private FileManager diskManager;


        /// <summary>
        /// The object we use to syncronize list access
        /// </summary>
        internal object listLock = new object();
        /// <summary>
        /// The list of peers that are available to be connected to
        /// </summary>
        internal Peers Available
        {
            get { return this.available; }
        }
        private Peers available;


        /// <summary>
        /// The list of peers that we are currently connected to
        /// </summary>
        internal Peers ConnectedPeers
        {
            get { return this.connectedPeers; }
        }
        private Peers connectedPeers;


        /// <summary>
        /// The list of peers that we are currently trying to connect to
        /// </summary>
        internal Peers ConnectingTo
        {
            get { return this.connectingTo; }
        }
        private Peers connectingTo;


        /// <summary>
        /// The number of peers that this torrent instance is connected to
        /// </summary>
        public int OpenConnections
        {
            get { return this.connectedPeers.Count; }
        }


        /// <summary>
        /// True if this file has been hashchecked
        /// </summary>
        public bool HashChecked
        {
            get { return this.hashChecked; }
        }
        private bool hashChecked;


        /// <summary>
        /// The number of bytes downloaded this session
        /// </summary>
        public long BytesDownloaded
        {
            get { return this.bytesDownloaded; }
            internal set { this.bytesDownloaded = value; }
        }
        private long bytesDownloaded;


        /// <summary>
        /// The number of bytes uploaded this session
        /// </summary>
        public long BytesUploaded
        {
            get { return this.bytesUploaded; }
            internal set { this.bytesUploaded = value; }
        }
        private long bytesUploaded;


        /// <summary>
        /// The directory to download the files to
        /// </summary>
        public string SavePath
        {
            get { return this.savePath; }
            set { this.savePath = value; }
        }
        private string savePath;


        /// <summary>
        /// The number of peers that we are currently uploading to
        /// </summary>
        public int UploadingTo
        {
            get { return this.uploadingTo; }
            internal set { this.uploadingTo = value; }
        }
        private int uploadingTo;


        /// <summary>
        /// The current download speed in bytes per second
        /// </summary>
        /// <returns></returns>
        public double DownloadSpeed()
        {
            double total = 0;
            for (int i = 0; i < this.connectedPeers.Count; i++)
                if (!this.connectedPeers[i].Peer.IsChoking)
                    total += this.connectedPeers[i].Peer.Connection.Monitor.DownloadSpeed();

            return total;
        }


        /// <summary>
        /// The current upload speed in bytes per second
        /// </summary>
        /// <returns></returns>
        public double UploadSpeed()
        {
            double total = 0;

            for (int i = 0; i < this.connectedPeers.Count; i++)
                if (!this.connectedPeers[i].Peer.AmChoking)
                    total += this.connectedPeers[i].Peer.Connection.Monitor.UploadSpeed();

            return total;
        }
        #endregion


        #region Constructors
        /// <summary>
        /// Creates a new TorrentManager instance.
        /// </summary>
        /// <param name="torrent">The torrent to load in</param>
        /// <param name="savePath">The directory to save downloaded files to</param>
        /// <param name="settings">The settings to use for controlling connections</param>
        public TorrentManager(Torrent torrent, string savePath, TorrentSettings settings)
        {
            this.torrent = torrent;
            this.settings = settings;

            this.trackerConnection = new TrackerManager(this);
            this.trackerConnection.UpdateRecieved += new EventHandler<TrackerUpdateEventArgs>(TrackerUpdateRecieved);

            this.connectedPeers = new Peers(16);
            this.available = new Peers(16);
            this.uploadQueue = new Queue<PeerConnectionID>(16);
            this.downloadQueue = new Queue<PeerConnectionID>(16);
            this.connectingTo = new Peers(ClientEngine.connectionManager.MaxHalfOpenConnections);

            this.savePath = savePath;

            if (string.IsNullOrEmpty(savePath))
                throw new TorrentException("Torrent savepath cannot be null");

            this.diskManager = new FileManager(this.torrent.Files, this.torrent.Name, this.savePath, this.torrent.PieceLength, System.IO.FileAccess.ReadWrite);
            this.pieceManager = new PieceManager(new BitField(this.torrent.Pieces.Length), (TorrentFile[])this.torrent.Files);
        }
        #endregion


        #region Torrent controlling methods
        /// <summary>
        /// Hash checks the supplied torrent
        /// </summary>
        /// <param name="state">The TorrentManager to hashcheck</param>
        private void HashCheck(object state)
        {
            bool result;
            TorrentManager manager = state as TorrentManager;

            if (manager == null)
                throw new InvalidCastException("Error: object passed to HashCheck was not an ITorrentManager");

            for (int i = 0; i < manager.torrent.Pieces.Length; i++)
            {
                result = ToolBox.ByteMatch(manager.torrent.Pieces[i], manager.diskManager.GetHash(i));
                lock (manager.pieceManager.MyBitField)
                    manager.pieceManager.MyBitField[i] = result;

                if (manager.OnPieceHashed != null)
                    manager.OnPieceHashed(this, new PieceHashedEventArgs(i, result));
            }

            manager.hashChecked = true;

#warning Don't *always* start the torrent in the future.
            if (manager.state == TorrentState.Stopped || (manager.state == TorrentState.Paused) || manager.state == TorrentState.Hashing)
                manager.Start();
        }

        /// <summary>
        /// The current progress of the torrent in percent
        /// </summary>
        public double Progress()
        {
            double complete = 0;
            for (int i = 0; i < this.PieceManager.MyBitField.Length; i++)
                if (this.PieceManager.MyBitField[i])
                    complete++;

            complete -= this.pieceManager.CurrentRequestCount;
            return (complete * 100.0 / this.PieceManager.MyBitField.Length);
        }

        /// <summary>
        /// Starts the TorrentManager
        /// </summary>
        internal void Start()
        {
            TorrentStateChangedEventArgs args;
            if (!this.hashChecked && !this.amHashing)
            {
                this.amHashing = true;
                ThreadPool.QueueUserWorkItem(new WaitCallback(HashCheck), this);

                args = new TorrentStateChangedEventArgs(this.state, TorrentState.Hashing);
                this.state = TorrentState.Hashing;
                if (this.OnTorrentStateChanged != null)
                    OnTorrentStateChanged(this, args);
                
                return;
            }

            else if (!this.hashChecked)
            {
                return;
            }

            if (this.state == TorrentState.Seeding || this.state == TorrentState.SuperSeeding || this.state == TorrentState.Downloading)
                throw new TorrentException("Torrent is already running");

            if (this.Progress() == 100.0)
            {
                args = new TorrentStateChangedEventArgs(this.state, TorrentState.Seeding);
                this.state = TorrentState.Seeding;

                if (this.OnTorrentStateChanged != null)
                    this.OnTorrentStateChanged(this, args);
            }

            else
            {
                args = new TorrentStateChangedEventArgs(this.state, TorrentState.Downloading);
                this.state = TorrentState.Downloading;
                if (this.OnTorrentStateChanged != null)
                    this.OnTorrentStateChanged(this, args);
            }

            this.trackerConnection.SendUpdate(0, 0, (long)((1.0 - this.Progress() / 100.0) * this.torrent.Size), TorrentEvent.Started); // Tell server we're starting
        }

        /// <summary>
        /// Stops the TorrentManager
        /// </summary>
        internal WaitHandle Stop()
        {
            WaitHandle handle;
            TorrentStateChangedEventArgs args;

            args = new TorrentStateChangedEventArgs(this.state, TorrentState.Stopped);
            this.state = TorrentState.Stopped;
            if (this.OnTorrentStateChanged != null)
                this.OnTorrentStateChanged(this, args);

            this.diskManager.FlushAll();
            handle = this.trackerConnection.SendUpdate(this.bytesDownloaded, this.bytesUploaded, (long)((1.0 - this.Progress() / 100.0) * this.torrent.Size), TorrentEvent.Stopped);
            lock (this.listLock)
            {
                while(this.connectingTo.Count > 0)
                    lock (this.connectingTo[0])
                        ClientEngine.connectionManager.CleanupSocket(this.connectingTo[0]);

                    while (this.connectedPeers.Count > 0)
                        lock(this.connectedPeers[0])
                            ClientEngine.connectionManager.CleanupSocket(this.connectedPeers[0]);
            }

            this.downloadQueue.Clear();
            this.uploadQueue.Clear();
            this.connectedPeers = new Peers();
            this.available = new Peers();
            this.connectingTo = new Peers();

            return handle;
        }

        /// <summary>
        /// Pauses the TorrentManager
        /// </summary>
        internal void Pause()
        {
            TorrentStateChangedEventArgs args;
            lock (this.listLock)
            {
                args = new TorrentStateChangedEventArgs(this.state, TorrentState.Paused);
                this.state = TorrentState.Paused;
                if (this.OnTorrentStateChanged != null)
                    this.OnTorrentStateChanged(this, args);


                foreach (PeerConnectionID id in this.connectingTo)
                    lock (id)
                        ClientEngine.connectionManager.CleanupSocket(id);

                foreach (PeerConnectionID id in this.connectedPeers)
                    lock(id)
                        ClientEngine.connectionManager.CleanupSocket(id);

                lock (this.downloadQueue)
                    this.downloadQueue.Clear();

                lock (this.uploadQueue)
                    this.uploadQueue.Clear();
            }
        }

        int counter = 0;
        internal void DownloadLogic()
        {
            IPeerMessage msg;
            PeerConnectionID id;

            if (this.state == TorrentState.Downloading && this.Progress() == 100.0)
                this.state = TorrentState.Seeding;

            lock (this.downloadQueue)
            {
                if (this.settings.MaxDownloadSpeed > 0)
                    while ((this.DownloadSpeed() < this.settings.MaxDownloadSpeed * 1024) && this.downloadQueue.Count > 0)
                        ClientEngine.connectionManager.ResumePeer(this.downloadQueue.Dequeue(), true);
                else
                    while (this.downloadQueue.Count > 0)
                        ClientEngine.connectionManager.ResumePeer(this.downloadQueue.Dequeue(), true);
            }

            lock (this.uploadQueue)
            {
                if (this.settings.MaxUploadSpeed > 0)
                    while ((this.UploadSpeed() < this.settings.MaxUploadSpeed * 1024) && (this.uploadQueue.Count > 0))
                        ClientEngine.connectionManager.ResumePeer(this.uploadQueue.Dequeue(), false);
                else
                    while (this.uploadQueue.Count > 0)
                        ClientEngine.connectionManager.ResumePeer(this.uploadQueue.Dequeue(), false);
            }

            lock (this.listLock)
            {
                // If we havn't reached our max connected peers, connect to another one.
                if ((this.available.Count > 0) && (this.connectedPeers.Count < this.settings.MaxConnections))
                    ClientEngine.connectionManager.ConnectToPeer(this);

                for(int i =0; i < this.connectedPeers.Count; i++)
                {
                    id = this.connectedPeers[i];
                    lock(id)
                    {
                        if (id.Peer.Connection == null) // Shouldn't really happen...
                            continue;

                        if (counter++ % 20 == 0)     // Call it every second... ish
                            id.Peer.Connection.Monitor.TimePeriodPassed();

                        bool isInteresting = this.pieceManager.IsInteresting(id);
                        if (isInteresting && (!id.Peer.AmInterested))
                        {
                            // If we used to be not interested but now we are, send a message.
                            id.Peer.AmInterested = true;
                            id.Peer.Connection.EnQueue(new InterestedMessage());
                        }

                        else if (!isInteresting && id.Peer.AmInterested)
                        {
                            // If we used to be interested but now we're not, send a message
                            // We only become uninterested once we've recieved all our requested bits.
                            id.Peer.AmInterested = false;
                            id.Peer.Connection.EnQueue(new NotInterestedMessage());
                        }

                        if (id.Peer.AmChoking && id.Peer.IsInterested && this.uploadingTo < this.settings.UploadSlots)
                        {
                            this.uploadingTo++;
                            id.Peer.AmChoking = false;
                            id.Peer.Connection.EnQueue(new UnchokeMessage());
                            Console.WriteLine("UnChoking: " + this.uploadingTo);
                        }

                        if (id.Peer.PiecesSent > 20)  // Send 20 blocks before moving on
                        {
                            for (int j = 0; j < id.Peer.Connection.QueueLength; j++)
                            {
                                msg = id.Peer.Connection.DeQueue();
                                if (!(msg is PieceMessage))
                                    id.Peer.Connection.EnQueue(msg);
                            }

                            this.uploadingTo--;
                            id.Peer.PiecesSent = 0;
                            id.Peer.IsRequestingPiecesCount = 0;
                            id.Peer.Connection.EnQueue(new ChokeMessage());
                            Console.WriteLine("ReChoking: " + this.uploadingTo);
                        }

                        while (!id.Peer.IsChoking && id.Peer.AmRequestingPiecesCount < 6 && id.Peer.AmInterested)
                        {
                            msg = this.pieceManager.RequestPiece(id);
                            if (msg == null)
                                break;

                            id.Peer.Connection.EnQueue(msg);
                            id.Peer.AmRequestingPiecesCount++;
                        }

                        if (!(id.Peer.ProcessingQueue) && id.Peer.Connection.QueueLength > 0)
                            ClientEngine.connectionManager.ProcessQueue(id);
                    }

                    // If the last connection succeeded, then update at the regular interval
                    if (this.trackerConnection.UpdateSucceeded)
                    {
                        if (DateTime.Now > (this.trackerConnection.LastUpdated.AddSeconds(this.trackerConnection.UpdateInterval)))
                        {
                            this.trackerConnection.SendUpdate(this.bytesDownloaded, this.bytesUploaded, (long)((1.0 - this.Progress() / 100.0) * this.torrent.Size), TorrentEvent.None);
                        }
                    }
                    // Otherwise update at the min interval
                    else if (DateTime.Now > (this.trackerConnection.LastUpdated.AddSeconds(this.trackerConnection.MinUpdateInterval)))
                    {
                        this.trackerConnection.SendUpdate(this.bytesDownloaded, this.bytesUploaded, (long)((1.0 - this.Progress() / 100.0) * this.torrent.Size), TorrentEvent.None);
                    }
                }
            }
        }

        internal void SeedingLogic()
        {
            DownloadLogic();
        }

        internal void SuperSeedingLogic()
        {
            SeedingLogic();     // Initially just seed as per normal. This could be a V2.0 feature.
        }

        internal void PieceRecieved(int p)
        {
            // Only send a "have" message if the peer needs the piece.
            // This is "Have Suppression" as defined in the spec.

            // At the moment the only way to enter this method is
            // when a Piece message is recieved and it gets handled.
            // Therefore this is theadsafe access to the ConnectedPeers
            // list as a lock as already been acquired.
            foreach (PeerConnectionID id in this.connectedPeers)
            {
#warning Shouldn't be possible to have a null connection here. Fix this
                if (Monitor.TryEnter(id, 5))    // The peer who we recieved the piece off is already locked on
                {
                    if (!id.Peer.BitField[p])
                        if(id.Peer.Connection != null) // this shouldn't be null ever, but it is. Figure out why.
                        id.Peer.Connection.EnQueue(new HaveMessage(p)); // It's being disposed of but not removed from the list...

                    Monitor.Exit(id);
                }
            }
        }
        #endregion


        #region Methods to handle Tracker Update events
        void TrackerUpdateRecieved(object sender, TrackerUpdateEventArgs e)
        {
            int peersAdded = 0;
            BEncodedDictionary dict = null;
            if (e.State == TrackerState.Updating)
                Console.WriteLine("Tracker update begun");

            if (e.State == TrackerState.UpdateFailed || e.State == TrackerState.Inactive)
                Console.WriteLine("Tracker update failed. The tracker failed to return a response");

            if (e.State != TrackerState.Active) // Data only returned if it's active.
                return;

            try
            {
                dict = (BEncodedDictionary)BEncode.Decode(e.Response);
            }
            catch (BEncodingException ex)
            {
                Console.WriteLine("Tracker update failed: The tracker returned a corrupt or incomplete response");
                return;
            }

            foreach (KeyValuePair<BEncodedString, IBEncodedValue> keypair in dict)
            {
                switch (keypair.Key.Text)
                {
                    case ("tracker id"):
                        this.trackerConnection.TrackerId = keypair.Value.ToString();
                        break;

                    case ("min interval"):
                        this.trackerConnection.MinUpdateInterval = int.Parse(keypair.Value.ToString());
                        break;

                    case ("interval"):
                        this.trackerConnection.UpdateInterval = int.Parse(keypair.Value.ToString());
                        break;

                    case ("peers"):
                        if (keypair.Value is BEncodedList)          // Non-compact response
                            peersAdded = this.AddPeers(((BEncodedList)keypair.Value));
                        else if (keypair.Value is BEncodedString)   // Compact response
                            peersAdded = this.AddPeers(((BEncodedString)keypair.Value).TextBytes);

                        if (this.OnPeersAdded != null)
                            this.OnPeersAdded((ITorrentManager)this, new PeersAddedEventArgs(peersAdded));
                        break;

                    case("failure reason"):
                        Console.Write("Failure reason detected");
                        Console.Write(keypair.Value.ToString());
                        break;


                    default:
                        break;  //FIXME Log these
                }
            }
        }


        /// <summary>
        /// Adds an individual peer to the list
        /// </summary>
        /// <param name="peer">The peer to add</param>
        /// <returns>The number of peers added</returns>
        public int AddPeers(PeerConnectionID peer)
        {
            lock (this.listLock)
            {
                if (this.available.Contains(peer) || this.connectedPeers.Contains(peer) || this.connectingTo.Contains(peer))
                    return 0;

                this.available.Add(peer);
                return 1;
            }
        }


        /// <summary>
        /// Adds an array of peers to the list
        /// </summary>
        /// <param name="peers">The array of peers to add</param>
        /// <returns>The number of peers added</returns>
        public int AddPeers(Peers peers)
        {
            int i = 0;

            foreach (PeerConnectionID id in peers)
                i += this.AddPeers(id);

            return i;
        }


        /// <summary>
        /// Adds a non-compact tracker response of peers to the list
        /// </summary>
        /// <param name="list">The list of peers to add</param>
        /// <returns>The number of peers added</returns>
        public int AddPeers(BEncodedList list)
        {
            PeerConnectionID id;
            int peersAdded = 0;
            foreach (BEncodedDictionary dict in list)
            {
                try
                {
                    id = new PeerConnectionID(new ClientPeer(dict["ip"].ToString(),
                                    int.Parse(dict["port"].ToString()),
                                    dict["peer id"].ToString()), this);
                    peersAdded += this.AddPeers(id);
                }
                catch
                {
                }
            }
            return peersAdded;
        }


        /// <summary>
        /// Adds a compact tracker response of peers to the list
        /// </summary>
        /// <param name="byteOrderedData">The byte[] containing the peers to add</param>
        /// <returns>The number of peers added</returns>
        public int AddPeers(byte[] byteOrderedData)
        {
            // "Compact Response" peers are encoded in network byte order. 
            // IP's are the first four bytes
            // Ports are the following 2 bytes

            int i = 0;
            int added = 0;
            UInt16 port;
            PeerConnectionID id;
            StringBuilder sb = new StringBuilder(16);

            while (i < byteOrderedData.Length)
            {
                sb.Remove(0, sb.Length);

                sb.Append(byteOrderedData[i++]);
                sb.Append('.');
                sb.Append(byteOrderedData[i++]);
                sb.Append('.');
                sb.Append(byteOrderedData[i++]);
                sb.Append('.');
                sb.Append(byteOrderedData[i++]);

                port = (UInt16)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(byteOrderedData, i));
                i += 2;
                id = new PeerConnectionID(new ClientPeer(sb.ToString(), port, null), this);

                added += this.AddPeers(id);
            }

            return added;
        }

        #endregion


        #region Misc
        /// <summary>
        /// Returns the number of Seeds we are currently connected to
        /// </summary>
        /// <returns></returns>
        public int Seeds()
        {
            int seeds = 0;
            lock (this.listLock)
                foreach (PeerConnectionID id in this.connectedPeers)
                    lock (id)
                        if (id.Peer.PeerType == PeerType.Seed)
                            seeds++;
            return seeds;
        }


        /// <summary>
        /// Returns the number of Leechs we are currently connected to
        /// </summary>
        /// <returns></returns>
        public int Leechs()
        {
            int leechs = 0;
            lock (this.listLock)
                foreach (PeerConnectionID id in this.connectedPeers)
                    lock (id)
                        if (id.Peer.PeerType == PeerType.Leech || id.Peer.PeerType== PeerType.Unknown)
                            leechs++;
#warning Also counting unknowns for the moment. The spec has been updated, so I need to change the way i initialize bitfields. There should  never be unknowns

            return leechs;
        }


        /// <summary>
        /// Returns the total number of peers available (including ones already connected to)
        /// </summary>
        public int AvailablePeers
        {
            get { return this.available.Count + this.connectedPeers.Count + this.connectingTo.Count; }
        }
        #endregion


        #region  Dispose methods
        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        public void Dispose(bool disposing)
        {
            this.diskManager.Dispose();
        }
        #endregion


        /// <summary>
        /// Called when a Piece has been hashed by the FileManager
        /// </summary>
        /// <param name="pieceHashedEventArgs">The event args for the event</param>
        internal void HashedPiece(PieceHashedEventArgs pieceHashedEventArgs)
        {
            if (this.OnPieceHashed != null)
                this.OnPieceHashed((ITorrentManager)this, pieceHashedEventArgs);
        }
    }
}
