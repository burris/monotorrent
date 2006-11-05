//
// PieceManager.cs
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
using MonoTorrent.Client.PeerMessages;
using MonoTorrent.Common;
using MonoTorrent.Client;

namespace MonoTorrent.Client
{
    /// <summary>
    /// Contains the logic for choosing what piece to download next
    /// </summary>
    public class PieceManager
    {
        #region Events
        /// <summary>
        /// Event that's fired every time a Piece changes
        /// </summary>
        internal event EventHandler<PieceEventArgs> OnPieceChanged;
        #endregion


        #region Member Variables
        private IPiecePicker piecePicker;
        #endregion


        #region Constructors
        public PieceManager(BitField bitfield, TorrentFile[] files)
        {
            this.piecePicker = new StandardPicker(bitfield, files);
        }
        #endregion


        #region Methods
        internal bool IsInteresting(PeerConnectionID id)
        {
            return this.piecePicker.IsInteresting(id);
        }


        public bool InEndGameMode
        {
            get { return false; }
        }


        internal BitField MyBitField
        {
            get { return this.piecePicker.MyBitField; }
        }


        internal int CurrentRequestCount()
        {
            return this.piecePicker.CurrentRequestCount();
        }


        internal IPeerMessageInternal PickPiece(PeerConnectionID id, Peers otherPeers)
        {
            return this.piecePicker.PickPiece(id, otherPeers);
        }


        internal void ReceivedRejectRequest(PeerConnectionID id, RejectRequestMessage msg)
        {
            this.piecePicker.ReceivedRejectRequest(id, msg);
        }


        internal void RemoveRequests(PeerConnectionID id)
        {
            this.piecePicker.RemoveRequests(id);
        }


        internal void ReceivedPieceMessage(PeerConnectionID id, byte[] buffer, int dataOffset, int writeIndex, int blockLength, PieceMessage message)
        {
            this.piecePicker.ReceivedPieceMessage(id, buffer, dataOffset, writeIndex, blockLength, message);
        }
        #endregion
    }
}
