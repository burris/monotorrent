using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using MonoTorrent.Client.Messages;
namespace MonoTorrent.Client.Messages.UdpTracker
{
    class ConnectResponseMessage : UdpTrackerMessage
    {
        long connectionId;

        public long ConnectionId
        {
            get { return connectionId; }
        }

        public ConnectResponseMessage()
            : this(0, 0)
        {

        }

        public ConnectResponseMessage(int transactionId, long connectionId)
            : base(0, transactionId)
        {
            this.connectionId = connectionId;
        }

        public override int ByteLength
        {
            get { return 8 + 4 + 4; }
        }

        public override void Decode(byte[] buffer, int offset, int length)
        {
            if (Action != ReadInt(buffer, ref offset))
                ThrowInvalidActionException();
            TransactionId = ReadInt(buffer, ref offset);
            connectionId = ReadLong(buffer, ref offset);
        }

        public override int Encode(byte[] buffer, int offset)
        {
            int written = offset;

            written += Write(buffer, written, Action);
            written += Write(buffer, written, TransactionId);
            written += Write(buffer, written, ConnectionId);
            
            return ByteLength;
        }
    }
}
