//
// QueryMessage.cs
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

using MonoTorrent.BEncoding;
using System.Net;

namespace MonoTorrent.Dht.Messages
{
    internal abstract class QueryMessage : Message
    {
        private static readonly BEncodedString IdKey = "id";
        private static readonly BEncodedString QueryArgumentsKey = "a";
        private static readonly BEncodedString QueryNameKey = "q";
        internal static readonly BEncodedString QueryType = "q";
        private ResponseCreator responseCreator;
		private IMessageTask task;
        
        internal IMessageTask Task
        {
            get {return task;}
            set {task = value;}
        }
        
        internal NodeId Id
        {
            get { return new NodeId(new BigInteger(((BEncodedString)Parameters[IdKey]).TextBytes)); }
        }

        internal ResponseCreator ResponseCreator
        {
            get { return responseCreator; }
            private set { responseCreator = value; }
        }

        protected BEncodedDictionary Parameters
        {
            get { return (BEncodedDictionary)properties[QueryArgumentsKey]; }
        }

        protected QueryMessage(NodeId id, BEncodedString queryName, ResponseCreator responseCreator)
            : this(id, queryName, new BEncodedDictionary(), responseCreator)
        {

        }

        protected QueryMessage(NodeId id, BEncodedString queryName, BEncodedDictionary queryArguments, ResponseCreator responseCreator)
            : base(QueryType)
        {
            properties.Add(QueryNameKey, queryName);
            properties.Add(QueryArgumentsKey, queryArguments);

            Parameters.Add(IdKey, id.BencodedString());
            ResponseCreator = responseCreator;
        }

        protected QueryMessage(BEncodedDictionary d, ResponseCreator responseCreator)
            : base(d)
        {
            ResponseCreator = responseCreator;
        }

        
        public override bool HandleInternal(DhtEngine engine, IPEndPoint source)
        {
            Node node = engine.RoutingTable.FindNode(Id);
            if (node == null)
            {
                //FIXME: I shouldn't use this endpoint! I can't add a peer using this because the port which they
                // *send* the message on does not have to be the same as the port they're listening from messages on.
                // Generally speaking, the send port is always different.

                // When i receive a query message from a peer i don't know, how do i get their correct listen port for DHT messages? 
                node = new Node(Id, source);
                engine.RoutingTable.Add(engine, node);

            }
            node.Seen();
            
            return Handle(engine, node);
        }
        
        public virtual bool TimedOut(DhtEngine engine, Node node)
        {
            return true;
        }

        public virtual bool TimedOutInternal(DhtEngine engine)
        {
            Node node = engine.RoutingTable.FindNode(Id);
            if (node == null)
                return false;

            node.FailedCount++;

            if (node.FailedCount < Node.MaxFailures)
            {
                engine.MessageLoop.EnqueueSend(this, node);
            }
            else
            {
                if (!MessageFactory.UnregisterSend(this))
                    return false;
                
                if (!TimedOut(engine, node))
                    return false;
                    
                if (Task != null)
                        Task.MessageTimedout(this);
            }

            return true;
        }
    }
}
