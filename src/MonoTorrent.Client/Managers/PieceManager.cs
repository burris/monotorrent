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
using System.Threading;

namespace MonoTorrent.Client
{
    /// <summary>
    /// Contains the logic for choosing what piece to download next
    /// </summary>
    public class PieceManager
    {
        #region Internal Constants

        internal const int MaxRequests = 12;
        internal const int MaxEndGameRequests = 2;

        #endregion


        #region Events

        public event EventHandler<BlockEventArgs> BlockReceived;
        public event EventHandler<BlockEventArgs> BlockRequested;
        public event EventHandler<BlockEventArgs> BlockRequestCancelled;

        #endregion


        #region Member Variables
        private PiecePickerBase piecePicker;
        #endregion


        #region Constructors
        internal PieceManager(BitField bitfield, TorrentFile[] files)
        {
            this.piecePicker = new StandardPicker(bitfield, files);
        }
        #endregion


        #region Methods

        /// <summary>
        /// Tries to add a piece request to the peers message queue.
        /// </summary>
        /// <param name="id">The peer to add the request too</param>
        /// <returns>True if the request was added</returns>
        internal bool AddPieceRequest(PeerConnectionID id)
        {
            RequestMessage msg;

            if (id.Peer.Connection.AmRequestingPiecesCount >= PieceManager.MaxRequests)
                return false;

            if (this.InEndGameMode)// In endgame we only want to queue 2 pieces
                if (id.Peer.Connection.AmRequestingPiecesCount > PieceManager.MaxEndGameRequests)
                    return false;

            msg = this.PickPiece(id, id.TorrentManager.Peers.ConnectedPeers);
            if (msg == null)
                return false;

            id.Peer.Connection.EnQueue(msg);
            id.Peer.Connection.AmRequestingPiecesCount++;
            RaiseBlockRequested(new BlockEventArgs(msg, id));
            return true;
        }


        internal bool IsInteresting(PeerConnectionID id)
        {
            // If i have completed the torrent, then no-one is interesting
            if (id.TorrentManager.Bitfield.AllTrue)
                return false;

            // If the peer is a seeder, then he is definately interesting
            if (id.Peer.IsSeeder)
                return true;

            // Otherwise we need to do a full check
            return this.piecePicker.IsInteresting(id);
        }


        public bool InEndGameMode
        {
            get { return this.piecePicker is EndGamePicker; }
        }


        internal BitField MyBitField
        {
            get { return this.piecePicker.MyBitField; }
        }


        internal int CurrentRequestCount()
        {
            return this.piecePicker.CurrentRequestCount();
        }


        internal RequestMessage PickPiece(PeerConnectionID id, List<PeerConnectionID> otherPeers)
        {
            if ((this.MyBitField.Length - this.MyBitField.TrueCount < 15) && this.piecePicker is StandardPicker)
                this.piecePicker = new EndGamePicker(this.MyBitField, id.TorrentManager.Torrent, ((StandardPicker)this.piecePicker).Requests);

            return this.piecePicker.PickPiece(id, otherPeers);
        }


        internal void ReceivedChokeMessage(PeerConnectionID id)
        {
            // If fast peers isnt supported, we remove all pending request messages
            if (!(id.Peer.Connection.SupportsFastPeer && ClientEngine.SupportsFastPeer))
            {
                // Remove any pending request messages from the send queue as there's no point in sending them
                IPeerMessageInternal message;
                int length = id.Peer.Connection.QueueLength;
                for (int i = 0; i < length; i++)
                    if ((message = id.Peer.Connection.DeQueue()) is RequestMessage)
                        RaiseBlockRequestCancelled(new BlockEventArgs( (RequestMessage)message, id));
                    else
                        id.Peer.Connection.EnQueue(message);
            }

            this.piecePicker.ReceivedChokeMessage(id);
        }


        internal void ReceivedRejectRequest(PeerConnectionID id, RejectRequestMessage msg)
        {
            this.piecePicker.ReceivedRejectRequest(id, msg);
        }


        internal void RemoveRequests(PeerConnectionID id)
        {
            this.piecePicker.RemoveRequests(id);
        }


        internal PieceEvent ReceivedPieceMessage(PeerConnectionID id, byte[] buffer, PieceMessage message)
        {
            return this.piecePicker.ReceivedPieceMessage(id, buffer, message);
        }

        #endregion


        #region Event Firing Code

        private void AsyncBlockReceived(object args)
        {
            if (this.BlockReceived == null)
                return;

            BlockEventArgs e = (BlockEventArgs)args;
            this.BlockReceived(e.ID, e);
        }

        private void AsyncBlockRequested(object args)
        {
            if (this.BlockRequested == null)
                return;

            BlockEventArgs e = (BlockEventArgs)args;
            this.BlockRequested(e.ID, e);
        }

        private void AsyncBlockRequestCancelled(object args)
        {
            if (this.BlockRequestCancelled == null)
                return;

            BlockEventArgs e = (BlockEventArgs)args;
            this.BlockRequestCancelled(e.ID, e);
        }

        internal void RaiseBlockReceived(BlockEventArgs args)
        {
            if (this.BlockReceived != null)
                ThreadPool.QueueUserWorkItem(new WaitCallback(AsyncBlockReceived), args);
        }

        internal void RaiseBlockRequested(BlockEventArgs args)
        {
            if (this.BlockRequested != null)
                ThreadPool.QueueUserWorkItem(new WaitCallback(AsyncBlockRequested), args);
        }

        internal void RaiseBlockRequestCancelled(BlockEventArgs args)
        {
            if (this.BlockRequestCancelled != null)
                ThreadPool.QueueUserWorkItem(new WaitCallback(AsyncBlockRequestCancelled), args);
        }

        #endregion Event Firing Code
    }
}
