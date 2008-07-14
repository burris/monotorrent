//
// DhtEngine.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2008 Alan McGovern
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
using System.Net;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;

using MonoTorrent;
using MonoTorrent.Common;
using MonoTorrent.Client;
using MonoTorrent.BEncoding;
using System.IO;
using MonoTorrent.Dht.Listeners;
using MonoTorrent.Client.Tasks;
using MonoTorrent.Dht.Messages;
using MonoTorrent.Client.Messages;

namespace MonoTorrent.Dht
{
    public enum eErrorCode : int
    {
        GenericError = 201,
        ServerError = 202,
        ProtocolError = 203,// malformed packet, invalid arguments, or bad token
        MethodUnknown = 204//Method Unknown
    }
    
	public class DhtEngine
	{
        internal static MainLoop MainLoop = new MainLoop();
        
        public event EventHandler StateChanged;
        
        public event EventHandler<PeersFoundEventArgs> PeersFound;
        
        int port = 6881;
        State state = State.NotReady;
        MessageLoop messageLoop;
        RoutingTable table = new RoutingTable();
        int timeout;
        Dictionary<NodeId, List<Node>> torrents = new Dictionary<NodeId, List<Node>>();
        TokenManager tokenManager;

        public int Port
        {
            get { return port; }
            set { port = value; }
        }
        
        internal MessageLoop MessageLoop
        {
            get { return messageLoop; }
        }

        internal TokenManager TokenManager
        {
            get { return tokenManager; }
        }
        
        internal RoutingTable RoutingTable
        {
            get { return table; }
        }

        public State State
        {
            get { return state; }
        }

        internal int TimeOut
        {
            get { return timeout; }
            set { timeout = value; }
        }

        internal Dictionary<NodeId, List<Node>> Torrents
        {
            get { return torrents; }
        }

        public DhtEngine(IListener listener)
        {
            messageLoop = new MessageLoop(this, listener);
            timeout = 20 * 1000; // 20 second message timeout by default
            tokenManager = new TokenManager();
        }

        public void Add(IEnumerable<Node> nodes)
        {
            if (nodes == null)
                throw new ArgumentNullException("nodes");

            foreach (Node n in nodes)
                Add(n);
        }

        public void Add(Node node)
        {
            if (node == null)
                throw new ArgumentNullException("node");

            messageLoop.EnqueueSend(new Ping(node.Id), node);
        }

        public void Start()
        {
            if (this.RoutingTable.Buckets.Count == 1 && RoutingTable.Buckets[0].Nodes.Count == 1)
            {
                Node utorrent = new Node(NodeId.Create(), new IPEndPoint(Dns.GetHostEntry("router.bittorrent.com").AddressList[0], 6881));
                Node node2 = new Node(NodeId.Create(), new IPEndPoint(Dns.GetHostEntry("router.utorrent.com").AddressList[0], 6881));
                Add(utorrent);
                Add(node2);
            }
            RaiseStateChanged(State.Initialising);
        }
        
        #region event
        
        private void RaiseStateChanged(State newState)
        {
            state = newState;

            if (StateChanged != null)
                StateChanged(this, EventArgs.Empty);
        }
        
        internal void RaisePeersFound(NodeId infoHash, List<Node> peers)
        {
            if (PeersFound != null)
                PeersFound(this, new PeersFoundEventArgs(infoHash.Bytes, peers));
        }

        #endregion
        
        #region Load/save
        
        public byte[] SaveNodes()
        {
            BEncodedList details = new BEncodedList();

            MainLoop.QueueWait(delegate {
                foreach (Bucket b in RoutingTable.Buckets)
                {
                    foreach (Node n in b.Nodes)
                        if (n != RoutingTable.LocalNode && n.State != NodeState.Bad)
                            details.Add(n.CompactNode());

                    if (b.Replacement != null)
                        if (b.Replacement != RoutingTable.LocalNode && b.Replacement.State != NodeState.Bad)
                            details.Add(b.Replacement.CompactNode());
                }
            });

            return details.Encode();
        }

        public void LoadNodes(byte[] nodes)
        {
            MainLoop.QueueWait(delegate {
                BEncodedList list = (BEncodedList)BEncodedValue.Decode(nodes);
                foreach (BEncodedString s in list)
                    Add(Node.FromCompactNode(s.TextBytes, 0));
            });
        }
        
        #endregion
        
        public void Announce(byte[] infoHash)
        {
            NodeId target = new NodeId(infoHash);
            IList<Node> nodes = table.GetClosest(target);
            foreach(Node n in nodes)
            {
                messageLoop.EnqueueSend(new GetPeers(RoutingTable.LocalNode.Id, target), n);
            }            
        }
    }
}
