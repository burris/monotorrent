//
// RejectRequestMessage.cs
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

namespace MonoTorrent.Client.PeerMessages
{
    public class RejectRequestMessage : IPeerMessageInternal, IPeerMessage
    {
        public const byte MessageId = 0x10;
        public readonly int messageLength = 13;

        #region Member Variables
        /// <summary>
        /// The offset in bytes of the block of data
        /// </summary>
        public int StartOffset
        {
            get { return this.startOffset; }
        }
        private int startOffset;

        /// <summary>
        /// The index of the piece
        /// </summary>
        public int PieceIndex
        {
            get { return this.pieceIndex; }
        }
        private int pieceIndex;

        /// <summary>
        /// The length of the block of data
        /// </summary>
        public int RequestLength
        {
            get { return this.requestLength; }
        }
        private int requestLength;
        #endregion


        #region Constructors
        public RejectRequestMessage()
        {
        }


        public RejectRequestMessage(PieceMessage message)
            :this(message.PieceIndex, message.StartOffset, message.RequestLength)
        {
        }


        public RejectRequestMessage(int pieceIndex, int startOffset, int requestLength)
        {
            this.pieceIndex = pieceIndex;
            this.startOffset = startOffset;
            this.requestLength = requestLength;
        }
        #endregion

        
        #region Methods
        internal int Encode(ArraySegment<byte> buffer, int offset)
        {
            if (!ClientEngine.SupportsFastPeer)
                throw new ProtocolException("Message encoding not supported");

            buffer.Array[buffer.Offset + offset + 4] = MessageId;
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(this.messageLength)), 0, buffer.Array, buffer.Offset + offset, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(this.pieceIndex)), 0, buffer.Array, buffer.Offset + offset + 5, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(this.startOffset)), 0, buffer.Array, buffer.Offset + offset + 9, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(this.requestLength)), 0, buffer.Array, buffer.Offset + offset + 13, 4);
            return this.messageLength + 4;
        }


        internal void Decode(ArraySegment<byte> buffer, int offset, int length)
        {
            if (!ClientEngine.SupportsFastPeer)
                throw new ProtocolException("Message decoding not supported");

            this.pieceIndex = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer.Array, buffer.Offset + offset));
            offset += 4;
            this.startOffset = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer.Array, buffer.Offset + offset));
            offset += 4;
            this.requestLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer.Array, buffer.Offset + offset));
            offset += 4;
        }


        internal void Handle(PeerIdInternal id)
        {
            if (!id.Peer.Connection.SupportsFastPeer)
                throw new MessageException("Peer shouldn't support fast peer messages");

            id.TorrentManager.PieceManager.ReceivedRejectRequest(id, this);
        }


        public int ByteLength
        {
            get { return this.messageLength + 4; }
        }
        #endregion


        #region Overidden Methods
        public override bool Equals(object obj)
        {
            RejectRequestMessage msg = obj as RejectRequestMessage;
            if (msg == null)
                return false;

            return (this.pieceIndex == msg.pieceIndex
                && this.startOffset == msg.startOffset
                && this.requestLength == msg.requestLength);
        }


        public override int GetHashCode()
        {
            return (this.pieceIndex.GetHashCode()
                    ^ this.requestLength.GetHashCode()
                    ^ this.startOffset.GetHashCode());
        }


        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(24);
            sb.Append("Reject Request");
            sb.Append(" Index: ");
            sb.Append(this.pieceIndex);
            sb.Append(" Offset: ");
            sb.Append(this.startOffset);
            sb.Append(" Length " );
            sb.Append(this.requestLength);
            return sb.ToString();
        }
        #endregion


        #region IPeerMessageInternal Explicit Calls

        int IPeerMessageInternal.Encode(ArraySegment<byte> buffer, int offset)
        {
            return this.Encode(buffer, offset);
        }

        void IPeerMessageInternal.Decode(ArraySegment<byte> buffer, int offset, int length)
        {
            this.Decode(buffer, offset, length);
        }

        void IPeerMessageInternal.Handle(PeerIdInternal id)
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
