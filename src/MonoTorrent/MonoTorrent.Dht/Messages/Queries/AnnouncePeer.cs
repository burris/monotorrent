//
// AnnouncePeer.cs
//
// Authors:
//   Alan McGovern <alan.mcgovern@gmail.com>
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
using System.Collections.Generic;
using System.Text;

using MonoTorrent.BEncoding;
using System.Net;

namespace MonoTorrent.Dht.Messages
{
    class AnnouncePeer : QueryMessage
    {
        private static BEncodedString InfoHashKey = "info_hash";
        private static BEncodedString QueryName = "announce_peer";
        private static BEncodedString PortKey = "port";
        private static BEncodedString TokenKey = "token";
        private static ResponseCreator responseCreator = delegate(BEncodedDictionary d, QueryMessage m) { return new AnnouncePeerResponse(d, m); };

        internal NodeId InfoHash
        {
            get { return new NodeId((BEncodedString)Parameters[InfoHashKey]); }
        }

        internal BEncodedNumber Port
        {
            get { return (BEncodedNumber)Parameters[PortKey]; }
        }

        internal BEncodedString Token
        {
            get { return (BEncodedString)Parameters[TokenKey]; }
        }

        public AnnouncePeer(NodeId id, NodeId infoHash, BEncodedNumber port, BEncodedString token)
            : base(id, QueryName, responseCreator)
        {
            Parameters.Add(InfoHashKey, infoHash.BencodedString());
            Parameters.Add(PortKey, port);
            Parameters.Add(TokenKey, token);
        }

        public AnnouncePeer(BEncodedDictionary d)
            : base(d, responseCreator)
        {

        }

        public override void Handle(DhtEngine engine, Node node)
        {
            base.Handle(engine, node);

            if (!engine.Torrents.ContainsKey(InfoHash))
                engine.Torrents.Add(InfoHash, new List<Node>());

            Message response;
            if (engine.TokenManager.VerifyToken(node, Token))
			{
                engine.Torrents[InfoHash].Add(node);
				response = new AnnouncePeerResponse(engine.RoutingTable.LocalNode.Id, TransactionId);
		    }
			else
			    response = new ErrorMessage(ErrorCode.ProtocolError, "Invalid or expired token received");
				
			engine.MessageLoop.EnqueueSend(response, node.EndPoint);
        }
    }
}
