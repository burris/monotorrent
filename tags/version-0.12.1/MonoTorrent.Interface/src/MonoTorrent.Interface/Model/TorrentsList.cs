/*
 * $Id: TorrentsList.cs 931 2006-08-22 17:45:59Z piotr $
 * Copyright (c) 2006 by Piotr Wolny <gildur@gmail.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;

using Gtk;

using MonoTorrent.Client;

using MonoTorrent.Interface.Helpers;
using System.Text;

namespace MonoTorrent.Interface.Model
{
    public class TorrentsList : ListStore
    {
        public event EventHandler TorrentStateChanged;

        private Dictionary<TreeIter, TorrentManager> rowsToTorrents;

        public TorrentsList()
            : base(
                typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string))
        {
            this.rowsToTorrents = new Dictionary<TreeIter, TorrentManager>();
            ClientEngine.ConnectionManager.PeerConnected
                    += OnPeerChange;
            ClientEngine.ConnectionManager.PeerDisconnected
                    += OnPeerChange;
            ClientEngine.ConnectionManager.PeerMessageTransferred
                    += OnPeerChange;
        }

        public TreeIter AddTorrent(TorrentManager torrent)
        {
            TreeIter row = Append();
            UpdateRow(row, torrent);
            rowsToTorrents.Add(row, torrent);
            torrent.PieceHashed += OnTorrentChange;
            torrent.PeersFound += OnTorrentChange;
            torrent.TorrentStateChanged += OnTorrentStateChange;
            //torrent.PieceManager.OnPieceChanged += OnTorrentChange;
            return row;
        }

        public bool RemoveTorrent(ref TreeIter row)
        {
            //rowsToTorrents[row].PieceManager.OnPieceChanged -= OnTorrentChange;
            rowsToTorrents[row].PeersFound -= OnTorrentChange;
            rowsToTorrents[row].PieceHashed -= OnTorrentChange;
            rowsToTorrents.Remove(row);
            return Remove(ref row);
        }

        public TorrentManager GetTorrent(TreeIter row)
        {
            // FIXME
            // This is a fix for a bug i'm having (possibly only my own system, but possibly anyone on windows + mono.
            // I need to compare by stamp as i'm getting keynotfound exceptions even though the row is there.
            foreach (KeyValuePair<TreeIter, TorrentManager> keypair in this.rowsToTorrents)
                if (keypair.Key.Stamp == row.Stamp)
                    return keypair.Value;

            throw new ArgumentException("row");
        }

        private void OnTorrentChange(object sender, EventArgs args)
        {
            Application.Invoke(sender, args, OnTorrentChangeSync);
        }

        private void OnTorrentChangeSync(object sender, EventArgs args)
        {
            TorrentManager torrent = (TorrentManager) sender;
            UpdateStats(FindRow(torrent), torrent);
        }

        private void OnTorrentStateChange(object sender, EventArgs args)
        {
            Application.Invoke(sender, args, OnTorrentStateChangeSync);
        }

        private void OnTorrentStateChangeSync(object sender, EventArgs args)
        {
            TorrentManager torrent = (TorrentManager) sender;
            UpdateState(FindRow(torrent), torrent);
        }

        private void OnPeerChange(object sender, EventArgs args)
        {
            Application.Invoke(sender, args, OnPeerChangeSync);
        }

        private void OnPeerChangeSync(object sender, EventArgs args)
        {
            TorrentManager torrent=null;
            if(args is PeerConnectionEventArgs)
            {
                PeerConnectionEventArgs e = args as PeerConnectionEventArgs;
                torrent = e.PeerID.TorrentManager;

            }
            else if (args is PeerMessageEventArgs)
            {
                return;
                //PeerConnectionID id = sender as PeerConnectionID;
                //torrent = id.TorrentManager;
            }
            UpdateStats(FindRow(torrent), torrent);

        }

        private TreeIter FindRow(TorrentManager torrent)
        {
            foreach (TreeIter row in rowsToTorrents.Keys) {
                if (rowsToTorrents[row] == torrent) {
                    return row;
                }
            }
            return TreeIter.Zero;
        }

        private void UpdateRow(TreeIter row, TorrentManager torrent)
        {
            UpdateState(row, torrent);
            SetValue(row, 1, torrent.Torrent.Name);
            SetValue(row, 2, Formatter.FormatSize(torrent.Torrent.Size));
            UpdateStats(row, torrent);
        }

        private void UpdateStats(TreeIter row, TorrentManager torrent)
        {
            SetValue(row, 3, Formatter.FormatPercent(torrent.Progress));
            SetValue(row, 4, Formatter.FormatSize(torrent.Monitor.DataBytesDownloaded));
            SetValue(row, 5, Formatter.FormatSize(torrent.Monitor.DataBytesUploaded));
            SetValue(row, 6, Formatter.FormatSpeed(torrent.Monitor.DownloadSpeed));
            SetValue(row, 7, Formatter.FormatSpeed(torrent.Monitor.UploadSpeed));
            SetValue(row, 9, torrent.Peers.Leechs().ToString());
            SetValue(row, 8, torrent.Peers.Seeds().ToString());
            SetValue(row, 10, torrent.Peers.Available.ToString());
        }

        private void UpdateState(TreeIter row, TorrentManager torrent)
        {
            SetValue(row, 0, torrent.State.ToString());
            FireTorrentStateChanged();
        }

        private void FireTorrentStateChanged()
        {
            if (TorrentStateChanged != null) {
                TorrentStateChanged(this, EventArgs.Empty);
            }
        }
    }
}