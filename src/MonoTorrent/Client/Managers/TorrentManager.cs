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
using System.IO;
using System.Xml.Serialization;
using System.Diagnostics;
using MonoTorrent.BEncoding;

namespace MonoTorrent.Client
{
    /// <summary>
    /// 
    /// </summary>
    public class TorrentManager : IDisposable, IEquatable<TorrentManager>
    {
        #region Events

        /// <summary>
        /// Event that's fired every time new peers are added from a tracker update
        /// </summary>
        public event EventHandler<PeersAddedEventArgs> PeersFound;

        /// <summary>
        /// Event that's fired every time a piece is hashed
        /// </summary>
        public event EventHandler<PieceHashedEventArgs> PieceHashed;

        /// <summary>
        /// Event that's fired every time the TorrentManagers state changes
        /// </summary>
        public event EventHandler<TorrentStateChangedEventArgs> TorrentStateChanged;

        #endregion


        #region Member Variables

        private BitField bitfield;              // The bitfield representing the pieces we've downloaded and have to download
        private ClientEngine engine;            // The engine that this torrent is registered with
        private FileManager fileManager;        // Controls all reading/writing to/from the disk
        internal Queue<int> finishedPieces;     // The list of pieces which we should send "have" messages for
        private bool hashChecked;               // True if the manager has been hash checked
        private int hashFails;                  // The total number of pieces receieved which failed the hashcheck
        internal readonly object listLock = new object();// The object we use to syncronize list access
        internal bool loadedFastResume;         // Used to fire the "PieceHashed" event if fast resume data was loaded
        private ConnectionMonitor monitor;      // Calculates download/upload speed
        private PeerList peers;                 // Stores all the peers we know of in a list
        private PieceManager pieceManager;      // Tracks all the piece requests we've made and decides what pieces we can request off each peer
        private RateLimiter rateLimiter;        // Contains the logic to decide how many chunks we can download
        internal readonly object resumeLock;    // Used to control access to the upload and download queues 
        private TorrentSettings settings;       // The settings for this torrent
        private DateTime startTime;             // The time at which the torrent was started at.
        private TorrentState state;             // The current state (seeding, downloading etc)
        private Torrent torrent;                // All the information from the physical torrent that was loaded
        private TrackerManager trackerManager;  // The class used to control all access to the tracker
        private int uploadingTo;                // The number of peers which we're currently uploading to

        #endregion Member Variables


        #region Properties

        internal BitField Bitfield
        {
            get { return this.bitfield; }
        }


        internal ClientEngine Engine
        {
            get { return this.engine; }
            set { this.engine = value; }
        }


        /// <summary>
        /// The DiskManager associated with this torrent
        /// </summary>
        public FileManager FileManager
        {
            get { return this.fileManager; }
        }


        /// <summary>
        /// True if this file has been hashchecked
        /// </summary>
        public bool HashChecked
        {
            get { return this.hashChecked; }
            internal set { this.hashChecked = value; }
        }


        /// <summary>
        /// The number of times we recieved a piece that failed the hashcheck
        /// </summary>
        public int HashFails
        {
            get { return this.hashFails; }
        }

        
        /// <summary>
        /// Records statistics such as Download speed, Upload speed and amount of data uploaded/downloaded
        /// </summary>
        public ConnectionMonitor Monitor
        {
            get { return this.monitor; }
        }


        /// <summary>
        /// The number of peers that this torrent instance is connected to
        /// </summary>
        public int OpenConnections
        {
            get { return this.peers.ConnectedPeers.Count; }
        }


        /// <summary>
        /// 
        /// </summary>
        public PeerList Peers
        {
            get { return this.peers; }
        }


        /// <summary>
        /// The piecemanager for this TorrentManager
        /// </summary>
        public PieceManager PieceManager
        {
            get { return this.pieceManager; }
        }


        /// <summary>
        /// The current progress of the torrent in percent
        /// </summary>
        public double Progress
        {
            get { return (this.bitfield.PercentComplete); }
        }


        /// <summary>
        /// The directory to download the files to
        /// </summary>
        public string SavePath
        {
            get { return this.fileManager.SavePath; }
        }


        /// <summary>
        /// The settings for with this TorrentManager
        /// </summary>
        public TorrentSettings Settings
        {
            get { return this.settings; }
        }


        /// <summary>
        /// The current state of the TorrentManager
        /// </summary>
        public TorrentState State
        {
            get { return this.state; }
        }


        /// <summary>
        /// The time the torrent manager was started at
        /// </summary>
        public DateTime StartTime
        {
            get { return this.startTime; }
        }


        /// <summary>
        /// The tracker connection associated with this TorrentManager
        /// </summary>
        public TrackerManager TrackerManager
        {
            get { return this.trackerManager; }
        }


        /// <summary>
        /// The Torrent contained within this TorrentManager
        /// </summary>
        public Torrent Torrent
        {
            get { return this.torrent; }
        }


        /// <summary>
        /// The number of peers that we are currently uploading to
        /// </summary>
        public int UploadingTo
        {
            get { return this.uploadingTo; }
            internal set { this.uploadingTo = value; }
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
            if (torrent == null)
                throw new ArgumentNullException("torrent");

            if (savePath == null)
                throw new ArgumentNullException("savePath");

            if (settings == null)
                throw new ArgumentNullException("settings");

            this.bitfield = new BitField(torrent.Pieces.Count);
            this.fileManager = new FileManager(this, torrent.Files, torrent.PieceLength,  savePath, torrent.Files.Length == 1 ? "" : torrent.Name);
            this.finishedPieces = new Queue<int>();
            this.monitor = new ConnectionMonitor();
            this.resumeLock = listLock;
            this.settings = settings;
            this.peers = new PeerList(this);
            this.pieceManager = new PieceManager(bitfield, torrent.Files);
            this.torrent = torrent;
            this.trackerManager = new TrackerManager(this);
        }

        #endregion


        #region Public Methods

        public void Dispose()
        {
            this.fileManager.Dispose();
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            TorrentManager m = obj as TorrentManager;
            return (m == null) ? false : this.Equals(m);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(TorrentManager other)
        {
            return (other == null) ? false : Toolbox.ByteMatch(this.torrent.infoHash, other.torrent.infoHash);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return Toolbox.HashCode(this.torrent.infoHash);
        }


        /// <summary>
        /// Starts a hashcheck. If forceFullScan is false, the library will attempt to load fastresume data
        /// before performing a full scan, otherwise fast resume data will be ignored and a full scan will be started
        /// </summary>
        /// <param name="forceFullScan">True if a full hash check should be performed ignoring fast resume data</param>
        public void HashCheck(bool forceFullScan)
        {
            HashCheck(forceFullScan, false);
        }

        /// <summary>
        /// Starts a hashcheck. If forceFullScan is false, the library will attempt to load fastresume data
        /// before performing a full scan, otherwise fast resume data will be ignored and a full scan will be started
        /// </summary>
        /// <param name="forceFullScan">True if a full hash check should be performed ignoring fast resume data</param>
        /// <param name="autoStart">True if the manager should start downloading immediately after hash checking is complete</param>
        internal void HashCheck(bool forceFullScan, bool autoStart)
        {
            if (this.state != TorrentState.Stopped)
                throw new TorrentException("A hashcheck can only be performed when the manager is stopped");

            UpdateState(TorrentState.Hashing);
            ThreadPool.QueueUserWorkItem(new WaitCallback(PerformHashCheck), new bool[] { forceFullScan, autoStart });
        }


        /// <summary>
        /// Pauses the TorrentManager
        /// </summary>
        public void Pause()
        {
            lock (this.listLock)
            {
                // By setting the state to "paused", peers will not be dequeued from the either the
                // sending or receiving queues, so no traffic will be allowed.
                UpdateState(TorrentState.Paused);
                this.SaveFastResume();
            }
        }


        /// <summary>
        /// Starts the TorrentManager
        /// </summary>
        public void Start()
        {
            // If the torrent was "paused", then just update the state to Downloading and forcefully
            // make sure the peers begin sending/receiving again
            if (this.state == TorrentState.Paused)
            {
                UpdateState(TorrentState.Downloading);
                lock (this.resumeLock)
                    this.ResumePeers();
                return;
            }

            if (!this.fileManager.StreamsOpen)
                this.FileManager.OpenFileStreams(FileAccess.ReadWrite);


            // If the torrent needs to be hashed, hash it. If it's already in the process of being hashed
            // just return
            if (this.fileManager.InitialHashRequired)
            {
                if (!this.hashChecked && !(this.state == TorrentState.Hashing))
                {
                    HashCheck(false, true);
                    return;
                }

                else if (!this.hashChecked)
                {
                    return;
                }
            }

            if (this.state == TorrentState.Seeding || this.state == TorrentState.SuperSeeding || this.state == TorrentState.Downloading)
                throw new TorrentException("Torrent is already running");

            // If we loaded the fast resume data, we fire the piece hashed event as if we had read
            //  the pieces from the harddrive.
            if (this.loadedFastResume)
            {
                for (int i = 0; i < this.bitfield.Length; i++)
                    RaisePieceHashed(new PieceHashedEventArgs(i, this.bitfield[i]));

                this.loadedFastResume = false;
            }

            this.TrackerManager.Scrape();
            this.trackerManager.Announce(TorrentEvent.Started); // Tell server we're starting
            this.startTime = DateTime.Now;

            if (this.Progress == 100.0)
                UpdateState(TorrentState.Seeding);
            else
                UpdateState(TorrentState.Downloading);

            engine.ConnectionManager.RegisterManager(this);
            this.pieceManager.Reset();
            this.engine.Start();
        }


        /// <summary>
        /// Stops the TorrentManager
        /// </summary>
        public WaitHandle Stop()
        {
            if (this.state == TorrentState.Stopped)
                throw new TorrentException("Torrent already stopped");

            WaitHandle handle;

            UpdateState(TorrentState.Stopped);

            handle = this.trackerManager.Announce(TorrentEvent.Stopped);
            lock (this.listLock)
            {
                while (this.peers.ConnectingToPeers.Count > 0)
                    lock (this.peers.ConnectingToPeers[0])
                        engine.ConnectionManager.AsyncCleanupSocket(this.peers.ConnectingToPeers[0], true, "Called stop");

                while (this.peers.ConnectedPeers.Count > 0)
                    lock (this.peers.ConnectedPeers[0])
                        engine.ConnectionManager.AsyncCleanupSocket(this.peers.ConnectedPeers[0], true, "Called stop");
            }

            if (this.fileManager.StreamsOpen)
                this.FileManager.CloseFileStreams();

            this.SaveFastResume();
            this.peers.ClearAll();
            this.monitor.Reset();
            this.pieceManager.Reset();
            this.engine.ConnectionManager.UnregisterManager(this);
            this.engine.Stop();

            return handle;
        }

        #endregion


        #region Internal Methods

        /// <summary>
        /// Adds an individual peer to the list
        /// </summary>
        /// <param name="peer">The peer to add</param>
        /// <returns>The number of peers added</returns>
        internal int AddPeers(PeerIdInternal peer)
        {
            try
            {
                lock (this.listLock)
                {
                    if (this.peers.AvailablePeers.Contains(peer) ||
                        this.peers.ConnectedPeers.Contains(peer) ||
                        this.peers.ConnectingToPeers.Contains(peer))
                        return 0;

                    this.peers.AvailablePeers.Add(peer);

                    // When we successfully add a peer we try to connect to the next available peer
                    return 1;
                }
            }
            finally
            {
                engine.ConnectionManager.TryConnect();
            }
        }


        /// <summary>
        /// Adds a non-compact tracker response of peers to the list
        /// </summary>
        /// <param name="list">The list of peers to add</param>
        /// <returns>The number of peers added</returns>
        internal int AddPeers(BEncodedList list)
        {
            PeerIdInternal id;
            int added = 0;
            foreach (BEncodedDictionary dict in list)
            {
                try
                {
                    string peerId;

                    if (dict.ContainsKey("peer id"))
                        peerId = dict["peer id"].ToString();
                    else if (dict.ContainsKey("peer_id"))       // HACK: Some trackers return "peer_id" instead of "peer id"
                        peerId = dict["peer_id"].ToString();
                    else
                        peerId = string.Empty;

                    id = new PeerIdInternal(new Peer(peerId, dict["ip"].ToString() + ':' + dict["port"].ToString()), this);
                    added += this.AddPeers(id);
                }
                catch (Exception ex)
                {
                    Logger.Log((PeerId)null, ex.ToString());
                }
            }

            RaisePeersFound(new PeersAddedEventArgs(added));
            return added;
        }


        /// <summary>
        /// Adds a compact tracker response of peers to the list
        /// </summary>
        /// <param name="byteOrderedData">The byte[] containing the peers to add</param>
        /// <returns>The number of peers added</returns>
        internal int AddPeers(byte[] byteOrderedData)
        {
            // "Compact Response" peers are encoded in network byte order. 
            // IP's are the first four bytes
            // Ports are the following 2 bytes

            int i = 0;
            int added = 0;
            UInt16 port;
            PeerIdInternal id;
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
                sb.Append(':');
                sb.Append(port);
                id = new PeerIdInternal(new Peer(null, sb.ToString()), this);

                added += this.AddPeers(id);
            }

            RaisePeersFound(new PeersAddedEventArgs(added));
            return added;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="counter"></param>
        internal void DownloadLogic(int counter)
        {
            PeerIdInternal id;

            // First attempt to resume downloading (just in case we've stalled for whatever reason)
            lock (this.resumeLock)
                if (this.peers.DownloadQueue.Count > 0 || this.peers.UploadQueue.Count > 0)
                    this.ResumePeers();

            DateTime nowTime = DateTime.Now;
            DateTime nintySecondsAgo = nowTime.AddSeconds(-90);
            DateTime onhundredAndEightySecondsAgo = nowTime.AddSeconds(-180);

            engine.ConnectionManager.TryConnect();

            lock (this.listLock)
            {
                if (counter % (1000 / ClientEngine.TickLength) == 0)     // Call it every second... ish
                    this.monitor.TimePeriodPassed();

                while (this.finishedPieces.Count > 0)
                    this.SendHaveMessageToAll(this.finishedPieces.Dequeue());

                for (int i = 0; i < this.peers.ConnectedPeers.Count; i++)
                {
                    id = this.peers.ConnectedPeers[i];
                    lock (id)
                    {
                        id.UpdatePublicStats();
                        if (id.Peer.Connection == null)
                            continue;

                        if (counter % (1000 / ClientEngine.TickLength) == 0)     // Call it every second... ish
                            id.Peer.Connection.Monitor.TimePeriodPassed();

                        //if (counter % 500 == 0)
                        //    DumpStats(id, counter);


                        // If he is not interested and i am not choking him
                        if (!id.Peer.Connection.IsInterested && !id.Peer.Connection.AmChoking)
                            SetChokeStatus(id, true);

                        // If i am choking the peer, and he is interested in downloading from us, and i haven't reached my maximum upload slots
                        if (id.Peer.Connection.AmChoking && id.Peer.Connection.IsInterested && this.uploadingTo < this.settings.UploadSlots)
                            SetChokeStatus(id, false);

                        // If i have sent 50 pieces to the peer, choke him to let someone else download
                        if (id.Peer.Connection.PiecesSent > 50)
                            SetChokeStatus(id, true);

                        // If the peer is interesting, try to queue up some piece requests off him
                        if(id.Peer.Connection.AmInterested)
                            while (this.pieceManager.AddPieceRequest(id)) { }

                        if (nintySecondsAgo > id.Peer.Connection.LastMessageSent)
                        {
                            id.Peer.Connection.LastMessageSent = DateTime.Now;
                            id.Peer.Connection.Enqueue(new KeepAliveMessage());
                        }

                        if (onhundredAndEightySecondsAgo > id.Peer.Connection.LastMessageReceived)
                        {
                            engine.ConnectionManager.CleanupSocket(id, true, "Inactivity");
                            continue;
                        }

                        if (!id.Peer.Connection.ProcessingQueue && id.Peer.Connection.QueueLength > 0)
                        {
                            id.Peer.Connection.ProcessingQueue = true;
                            engine.ConnectionManager.MessageHandler.EnqueueSend(id);
                        }
                    }
                }

                if (counter % 100 == 0)
                {
                    if (this.Progress == 100.0 && this.state != TorrentState.Seeding)
                    {
                        //this.Stop();
                        //this.Start();
                        //this.hashChecked = false;
                        //this.fileManager.InitialHashRequired = true;
                        UpdateState(TorrentState.Seeding);
                    }
                    // If the last connection succeeded, then update at the regular interval
                    if (this.trackerManager.UpdateSucceeded)
                    {
                        if (DateTime.Now > (this.trackerManager.LastUpdated.AddSeconds(this.trackerManager.TrackerTiers[0].Trackers[0].UpdateInterval)))
                        {
                            this.trackerManager.Announce(TorrentEvent.None);
                        }
                    }
                    // Otherwise update at the min interval
                    else if (DateTime.Now > (this.trackerManager.LastUpdated.AddSeconds(this.trackerManager.TrackerTiers[0].Trackers[0].MinUpdateInterval)))
                    {
                        this.trackerManager.Announce(TorrentEvent.None);
                    }
                }

#warning Convert this to work in kB/sec for consistency
                if (counter % (1000 / ClientEngine.TickLength) == 0)
                    this.rateLimiter.UpdateDownloadChunks((int)(this.settings.MaxDownloadSpeed * 1024 * 1.1),
                                                          (int)(this.settings.MaxUploadSpeed * 1024 * 1.1),
                                                          (int)(this.monitor.DownloadSpeed * 1024),
                                                          (int)(this.monitor.UploadSpeed * 1024));
            }
        }


        /// <summary>
        /// Called when a Piece has been hashed by the FileManager
        /// </summary>
        /// <param name="pieceHashedEventArgs">The event args for the event</param>
        internal void HashedPiece(PieceHashedEventArgs pieceHashedEventArgs)
        {
            if (!pieceHashedEventArgs.HashPassed)
                Interlocked.Increment(ref this.hashFails);

            RaisePieceHashed(pieceHashedEventArgs);
        }


        internal void RaisePeersFound(PeersAddedEventArgs peersAddedEventArgs)
        {
            if (this.PeersFound != null)
                ThreadPool.QueueUserWorkItem(new WaitCallback(AsyncPeersFound), peersAddedEventArgs);
        }

        internal void RaisePieceHashed(PieceHashedEventArgs pieceHashedEventArgs)
        {
            int index = pieceHashedEventArgs.PieceIndex;
            for (int i = 0; i < this.torrent.Files.Length; i++)
                if (index >= this.torrent.Files[i].StartPieceIndex && index <= this.torrent.Files[i].EndPieceIndex)
                    this.torrent.Files[i].BitField[index - this.torrent.Files[i].StartPieceIndex] = true;
                else if (index < this.torrent.Files[i].StartPieceIndex)
                    break;

            if (this.PieceHashed != null)
                ThreadPool.QueueUserWorkItem(new WaitCallback(AsyncPieceHashed), pieceHashedEventArgs);
        }

        internal void RaiseTorrentStateChanged(TorrentStateChangedEventArgs e)
        {
            if (this.TorrentStateChanged != null)
                ThreadPool.QueueUserWorkItem(new WaitCallback(AsyncTorrentStateChanged), e);
        }

        /// <summary>
        /// Restarts peers which have been suspended from downloading/uploading due to rate limiting
        /// </summary>
        /// <param name="downloading"></param>
        internal void ResumePeers()
        {
            if (this.state == TorrentState.Paused)
                return;

            // While there are peers queued in the list and i haven't used my download allowance, resume downloading
            // from that peer. Don't resume if there are more than 20 queued writes in the download queue.
            while (this.peers.DownloadQueue.Count > 0 &&
                    this.fileManager.QueuedWrites < 20 &&
                    ((this.rateLimiter.DownloadChunks > 0) || this.settings.MaxDownloadSpeed == 0))
                if (engine.ConnectionManager.ResumePeer(this.peers.Dequeue(PeerType.DownloadQueue), true) > ConnectionManager.ChunkLength / 2.0)
                    Interlocked.Decrement(ref this.rateLimiter.DownloadChunks);

            while (this.peers.UploadQueue.Count > 0 && ((this.rateLimiter.UploadChunks > 0) || this.settings.MaxUploadSpeed == 0))
                if (engine.ConnectionManager.ResumePeer(this.peers.Dequeue(PeerType.UploadQueue), false) > ConnectionManager.ChunkLength / 2.0)
                    Interlocked.Decrement(ref this.rateLimiter.UploadChunks);

        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="counter"></param>
        internal void SeedingLogic(int counter)
        {
            DownloadLogic(counter);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="counter"></param>
        internal void SuperSeedingLogic(int counter)
        {
            SeedingLogic(counter);     // Initially just seed as per normal. This could be a V2.0 feature.
        }

        #endregion Internal Methods


        #region Private Methods

        private void AsyncPeersFound(object args)
        {
            if (this.PeersFound != null)
                this.PeersFound(this, (PeersAddedEventArgs)args);
        }

        private void AsyncPieceHashed(object args)
        {
            if (this.PieceHashed != null)
                this.PieceHashed(this, (PieceHashedEventArgs)args);
        }

        private void AsyncTorrentStateChanged(object args)
        {
            if (this.TorrentStateChanged != null)
                this.TorrentStateChanged(this, (TorrentStateChangedEventArgs)args);
        }

        /// <summary>
        /// Hash checks the supplied torrent
        /// </summary>
        /// <param name="state">The TorrentManager to hashcheck</param>
        private void PerformHashCheck(object state)
        {
            bool streamsOpen = this.fileManager.StreamsOpen;

            bool[] data = (bool[])state;
            bool forceCheck = data[0];
            bool autoStart = data[1];

            // If we are performing a forced scan OR we aren't forcing a full scan but can't load the fast resume data
            // perform a full scan.

            if (!streamsOpen)
                this.fileManager.OpenFileStreams(FileAccess.Read);

            if (forceCheck || (!forceCheck && !MonoTorrent.Client.FileManager.LoadFastResume(this)))
                for (int i = 0; i < this.torrent.Pieces.Count; i++)
                {
                    bool temp = this.torrent.Pieces.IsValid(this.fileManager.GetHash(i), i);
                    this.pieceManager.MyBitField[i] = temp;
                    RaisePieceHashed(new PieceHashedEventArgs(i, temp));

                }

            if (!streamsOpen)
                this.fileManager.CloseFileStreams();

            this.fileManager.InitialHashRequired = false;
            this.hashChecked = true;
            SaveFastResume();

            if (autoStart)
                Start();
            else
                UpdateState(TorrentState.Stopped);
        }


        /// <summary>
        /// Checks the send queue of the peer to see if there are any outstanding pieces which they requested
        /// and rejects them as necessary
        /// </summary>
        /// <param name="id"></param>
        private void RejectPendingRequests(PeerIdInternal id)
        {
            IPeerMessageInternal message;
            PieceMessage pieceMessage;
            int length = id.Peer.Connection.QueueLength;

            for (int i = 0; i < length; i++)
            {
                message = id.Peer.Connection.Dequeue();
                if (!(message is PieceMessage))
                {
                    id.Peer.Connection.Enqueue(message);
                    continue;
                }

                pieceMessage = (PieceMessage)message;

                // If the peer doesn't support fast peer, then we will never requeue the message
                if (!(id.Peer.Connection.SupportsFastPeer && ClientEngine.SupportsFastPeer))
                {
                    id.Peer.Connection.IsRequestingPiecesCount--;
                    continue;
                }

                // If the peer supports fast peer, queue the message if it is an AllowedFast piece
                // Otherwise send a reject message for the piece
                if (id.Peer.Connection.AmAllowedFastPieces.Contains((uint)pieceMessage.PieceIndex))
                    id.Peer.Connection.Enqueue(pieceMessage);
                else
                {
                    id.Peer.Connection.IsRequestingPiecesCount--;
                    id.Peer.Connection.Enqueue(new RejectRequestMessage(pieceMessage));
                }
            }
        }


        /// <summary>
        /// Saves data to allow fastresumes to the disk
        /// </summary>
        private void SaveFastResume()
        {
            // Do not create fast-resume data if we do not support it for this TorrentManager object
            if (!Settings.FastResumeEnabled)
                return;

            XmlSerializer fastResume = new XmlSerializer(typeof(int[]));

            using (FileStream file = File.Open(this.torrent.TorrentPath + ".fresume", FileMode.Create))
                fastResume.Serialize(file, this.pieceManager.MyBitField.Array);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="p"></param>
        private void SendHaveMessageToAll(int pieceIndex)
        {
            // This is "Have Suppression" as defined in the spec.

            lock (this.listLock)
                for (int i = 0; i < this.peers.ConnectedPeers.Count; i++)
                    lock (this.peers.ConnectedPeers[i])
                        if (this.peers.ConnectedPeers[i].Peer.Connection != null)
                        {
                            // If the peer has the piece already, we need to recalculate his "interesting" status.
                            bool hasPiece = this.peers.ConnectedPeers[i].Peer.Connection.BitField[pieceIndex];
                            if (hasPiece)
                            {
                                bool isInteresting = this.pieceManager.IsInteresting(this.peers.ConnectedPeers[i]);
                                SetAmInterestedStatus(this.peers.ConnectedPeers[i], isInteresting);
                            }

                            // Check to see if have supression is enabled and send the have message accordingly
                            if (!hasPiece || (hasPiece && !this.engine.Settings.HaveSupressionEnabled))
                                this.peers.ConnectedPeers[i].Peer.Connection.Enqueue(new HaveMessage(pieceIndex));
                        }
        }





        /// <summary>
        /// Sets the "AmChoking" status of the peer to the new value and enqueues the relevant peer message
        /// </summary>
        /// <param name="id">The peer to update the choke status for</param>
        /// <param name="amChoking">The new status for "AmChoking"</param>
        private void SetChokeStatus(PeerIdInternal id, bool amChoking)
        {
            if (id.Peer.Connection.AmChoking == amChoking)
                return;

            id.Peer.Connection.PiecesSent = 0;
            id.Peer.Connection.AmChoking = amChoking;
            if (amChoking)
            {
                Interlocked.Decrement(ref this.uploadingTo);
                RejectPendingRequests(id);
                id.Peer.Connection.EnqueueAt(new ChokeMessage(), 0);
                Logger.Log("Choking: " + this.uploadingTo);
            }
            else
            {
                Interlocked.Increment(ref this.uploadingTo);
                id.Peer.Connection.Enqueue(new UnchokeMessage());
                Logger.Log("UnChoking: " + this.uploadingTo);
            }
        }


        /// <summary>
        /// Fires the TorrentStateChanged event
        /// </summary>
        /// <param name="newState">The new state for the torrent manager</param>
        private void UpdateState(TorrentState newState)
        {
            if (this.state == newState)
                return;

            TorrentStateChangedEventArgs e = new TorrentStateChangedEventArgs(this.state, newState);
            this.state = newState;

            RaiseTorrentStateChanged(e);

        }

        #endregion Private Methods

        internal void SetAmInterestedStatus(PeerIdInternal id, bool interesting)
        {
            bool enqueued = false;
            if (interesting && !id.Peer.Connection.AmInterested)
            {
                id.Peer.Connection.AmInterested = true;
                id.Peer.Connection.Enqueue(new InterestedMessage());

                // He's interesting, so attempt to queue up any FastPieces (if that's possible)
                while (id.TorrentManager.pieceManager.AddPieceRequest(id)) { }
                enqueued = true;
            }
            else if (!interesting && id.Peer.Connection.AmInterested)
            {
                id.Peer.Connection.AmInterested = false;
                id.Peer.Connection.Enqueue(new NotInterestedMessage());
                enqueued = true;
            }

            if (enqueued && !id.Peer.Connection.ProcessingQueue)
            {
                id.Peer.Connection.ProcessingQueue = true;
                id.ConnectionManager.MessageHandler.EnqueueSend(id);
            }
        }
    }
}
