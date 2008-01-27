using System;
using System.Collections.Generic;
using System.Text;
using MonoTorrent.Client.Messages;

namespace MonoTorrent.Client.Tracker.UdpTrackerMessages
{
    class PeerDetailsMessage : UdpTrackerMessage
    {
        public override int ByteLength
        {
            get { throw new Exception("The method or operation is not implemented."); }
        }

        public override void Decode(byte[] buffer, int offset, int length)
        {
            throw new Exception("The method or operation is not implemented.");
        }

        public override int Encode(byte[] buffer, int offset)
        {
            throw new Exception("The method or operation is not implemented.");
        }
    }
}
