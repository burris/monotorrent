//
// Peer.cs
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
using System.Text;
using System.Net;
using MonoTorrent.Common;
using MonoTorrent.BEncoding;
using MonoTorrent.Client.Encryption;

namespace MonoTorrent.Client
{
    public class Peer
    {
        Uri connectionUri;
        IEncryptor encryptor;

        public Uri ConnectionUri
        {
            get { return connectionUri; }
        }

        public IEncryptor Encryptor
        {
            get { return encryptor; }
        }

        #region Private Fields

        private bool activeReceive;
        private bool activeSend;
        private int cleanedUpCount;
        private EncryptionMethods encryptionSupported = EncryptionMethods.RC4Encryption;
        private int failedConnectionAttempts;
        private int totalHashFails;
        private bool isSeeder;
        private string peerId;
        private int repeatedHashFails;
        private DateTime lastConnectionAttempt;

        #endregion Private Fields


        #region Properties

        internal bool ActiveReceive
        {
            get { return this.activeReceive; }
            set { this.activeReceive = value; }
        }

        internal bool ActiveSend
        {
            get { return this.activeSend; }
            set { this.activeSend = value; }
        }



        internal int CleanedUpCount
        {
            get { return this.cleanedUpCount; }
            set { this.cleanedUpCount = value; }
        }


        /// <summary>
        /// Returns the number of times the peer has sent us a piece which failed a hashcheck
        /// </summary>
        internal int TotalHashFails
        {
            get { return this.totalHashFails; }
        }


        /// <summary>
        /// The ID of the peer
        /// </summary>
        internal string PeerId
        {
            get { return peerId; }
            set { peerId = value; }
        }


        /// <summary>
        /// True if the peer is a seeder
        /// </summary>
        internal bool IsSeeder
        {
            get { return this.isSeeder; }
            set { this.isSeeder = value; }
        }


        /// <summary>
        /// The number of times we tried to connect to the peer and failed
        /// </summary>
        internal int FailedConnectionAttempts
        {
            get { return this.failedConnectionAttempts; }
            set { this.failedConnectionAttempts = value; }
        }


        internal DateTime LastConnectionAttempt
        {
            get { return this.lastConnectionAttempt; }
            set { this.lastConnectionAttempt = value; }
        }


        /// <summary>
        /// The highest level of encryption that should be attempted with this peer
        /// </summary>
        internal EncryptionMethods EncryptionSupported
        {
            get { return this.encryptionSupported; }
            set { this.encryptionSupported = value; }
        }

        internal int RepeatedHashFails
        {
            get { return this.repeatedHashFails; }
        }

        #endregion Properties


        #region Constructors
        public Peer(string peerId, Uri connectionUri)
            : this (peerId, connectionUri, new NoEncryption())
        {

        }

        public Peer(string peerId, Uri connectionUri, IEncryptor encryptor)
        {
            if (peerId == null)
                throw new ArgumentNullException("peerId");
            if (connectionUri == null)
                throw new ArgumentNullException("connectionUri");
            if (encryptor == null)
                throw new ArgumentNullException("encryptor");

            this.connectionUri = connectionUri;
            this.encryptor = encryptor;
            this.peerId = peerId;
        }

        #endregion


        public override bool Equals(object obj)
        {
            Peer peer = obj as Peer;
            if(peer ==null)
                return false;

            return this.connectionUri.Equals(peer.connectionUri);
        }


        public override int GetHashCode()
        {
            return this.connectionUri.GetHashCode();
        }


        public override string ToString()
        {
            return this.connectionUri.ToString();
        }


        internal byte[] CompactPeer()
        {
            byte[] data = new byte[6];
            // FIXME: This probably isn't right
            string[] peer = this.connectionUri.ToString().Split(':');
            Buffer.BlockCopy(IPAddress.Parse(peer[0]).GetAddressBytes(), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(short.Parse(peer[1]))), 0, data, 4, 2);

            return data;
        }


        internal void HashedPiece(bool succeeded)
        {
            if (succeeded && repeatedHashFails > 0)
                repeatedHashFails--;
            
            if (!succeeded)
            {
                repeatedHashFails++;
                totalHashFails++;
            }
        }


        internal static MonoTorrentCollection<Peer> Decode(BEncodedList peers)
        {
            MonoTorrentCollection<Peer> list = new MonoTorrentCollection<Peer>(peers.Count);
            foreach (BEncodedDictionary dict in peers)
            {
                string peerId;

                if (dict.ContainsKey("peer id"))
                    peerId = dict["peer id"].ToString();
                else if (dict.ContainsKey("peer_id"))       // HACK: Some trackers return "peer_id" instead of "peer id"
                    peerId = dict["peer_id"].ToString();
                else
                    peerId = string.Empty;

                Uri connectionUri = new Uri("tcp://" + IPAddress.Parse(dict["ip"].ToString() + int.Parse(dict["port"].ToString())));
                list.Add(new Peer(peerId, connectionUri, new NoEncryption()));
            }

            return list;
        }

        internal static MonoTorrentCollection<Peer> Decode(BEncodedString peers)
        {
            // "Compact Response" peers are encoded in network byte order. 
            // IP's are the first four bytes
            // Ports are the following 2 bytes
            byte[] byteOrderedData = peers.TextBytes;
            int i = 0;
            UInt16 port;
            StringBuilder sb = new StringBuilder(16);
            MonoTorrentCollection<Peer> list = new MonoTorrentCollection<Peer>((byteOrderedData.Length / 6) + 1);
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

                Uri uri = new Uri("tcp://" + sb.ToString());
                list.Add(new Peer("", uri, new NoEncryption()));
            }

            return list;
        }
    }
}