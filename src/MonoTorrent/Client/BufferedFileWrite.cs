using System;
using System.Collections.Generic;
using System.Text;
using MonoTorrent.Client.PeerMessages;

namespace MonoTorrent.Client
{
    internal class BufferedFileWrite
    {
        public byte[] Buffer;
        public PeerId Id;
        public IPeerMessageInternal Message;
        public Piece Piece;
        public BitField BitField;
        public List<int> UnhashedPieces;


        public BufferedFileWrite(PeerId id, byte[] buffer, IPeerMessageInternal message, Piece piece, BitField bitfield,
            List<int> unhashedPieces)
        {
            this.Id = id;
            this.Buffer = buffer;
            this.Message = message;
            this.Piece = piece;
            this.BitField = bitfield;
            this.UnhashedPieces = unhashedPieces;
        }
    }
}
