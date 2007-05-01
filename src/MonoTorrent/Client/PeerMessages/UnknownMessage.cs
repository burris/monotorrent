//
// UnknownMessage.cs
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

namespace MonoTorrent.Client.PeerMessages
{
    public class UnknownMessage : IPeerMessageInternal, IPeerMessage
    {
        #region Constructors
        public UnknownMessage()
        {
        }
        #endregion


        #region Methods
        internal int Encode(byte[] buffer, int offset)
        {
            return 0;
        }

        internal void Decode(byte[] buffer, int offset, int length)
        {
            throw new ProtocolException("Unknown message received");
        }

        internal void Handle(PeerId id)
        {
            throw new ProtocolException("Unknown message");
        }

        public int ByteLength
        {
            get { return 0; }
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
