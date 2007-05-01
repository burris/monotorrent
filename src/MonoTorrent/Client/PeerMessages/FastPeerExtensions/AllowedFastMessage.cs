//
// AllowedFastMessage.cs
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
using MonoTorrent.Client.Encryption;

namespace MonoTorrent.Client.PeerMessages
{
    public class AllowedFastMessage : IPeerMessageInternal, IPeerMessage
    {
        public const int MessageId = 0x11;
        private readonly int messageLength = 5;

        #region Member Variables
        public int PieceIndex
        {
            get { return this.pieceIndex; }
        }
        private int pieceIndex;
        #endregion


        #region Constructors
        internal AllowedFastMessage()
        {
        }

        internal AllowedFastMessage(uint pieceIndex)
        {
            this.pieceIndex = (int)pieceIndex;
        }
        #endregion


        #region Methods
        internal int Encode(byte[] buffer, int offset)
        {
            if (!ClientEngine.SupportsFastPeer)
                throw new ProtocolException("Message encoding not supported");

            buffer[offset + 4] = MessageId;
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(messageLength)), 0, buffer, offset, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(this.pieceIndex)), 0, buffer, offset+5, 4);
            return this.messageLength + 4;
        }


        internal void Decode(byte[] buffer, int offset, int length)
        {
            if (!ClientEngine.SupportsFastPeer)
                throw new ProtocolException("Message decoding not supported");

            this.pieceIndex = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer, offset));
        }


        internal void Handle(PeerId id)
        {
            if (!id.Peer.Connection.SupportsFastPeer)
                throw new MessageException("Peer shouldn't support fast peer messages");

            id.Peer.Connection.IsAllowedFastPieces.Add((uint)this.pieceIndex);
        }


        public int ByteLength
        {
            get { return this.messageLength + 4; }
        }
        #endregion


        #region Overidden Methods
        public override bool Equals(object obj)
        {
            AllowedFastMessage msg = obj as AllowedFastMessage;
            if (msg == null)
                return false;

            return this.pieceIndex == msg.pieceIndex;
        }


        public override int GetHashCode()
        {
            return this.pieceIndex.GetHashCode();
        }


        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(24);
            sb.Append("AllowedFast");
            sb.Append(" Index: ");
            sb.Append(this.pieceIndex);
            return sb.ToString();
        }
        #endregion


        #region IPeerMessageInternal Explicit Calls

        int IPeerMessageInternal.Encode(byte[] buffer, int offset)
        {
            return this.Encode(buffer, offset);
        }

        void IPeerMessageInternal.Decode(byte[] buffer, int offset, int length)
        {
            this.Decode(buffer, offset, length);
        }

        void IPeerMessageInternal.Handle(PeerId id)
        {
            this.Handle(id);
        }

        int IPeerMessageInternal.ByteLength
        {
            get { return this.ByteLength; }
        }

        #endregion
    }
}
