﻿//
// PeerInfo.cs
//
// Authors:
//   Karthik Kailash    karthik.l.kailash@gmail.com
//   David Sanghera     dsanghera@gmail.com
//
// Copyright (C) 2006 Karthik Kailash, David Sanghera
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

#if STATS

using System;
using System.Collections;
using System.Collections.Generic;

using MonoTorrent.Client;
using MonoTorrent.Client.Encryption;

namespace SampleClient.Stats
{
    /// <summary>
    /// All the peer information for a DataGridView row
    /// </summary>
    class PeerInfo : IEquatable<PeerInfo>
    {
        #region Fields

        private Uri connectionUri;
        private bool connected;
        private int downloadRate;
        private int uploadRate;
        private int dp;
        private int recipUpload;
        private double tyrantRatio;
        private int estimatedDownloadRate;
        private bool amChoking;
        private bool amInterested;
        private bool isChoking;
        private bool isInterested;
        private bool isSeeder;
        private int outstandingRequests;
        private int piecesSentFrom;
        private int piecesSentTo;
        private bool encrypted;

        public bool seen;          // for external use

        #endregion

        #region Properties

        public Uri Uri
        {
            get { return connectionUri; }
        }

        public bool Connected
        {
            get { return connected; }
            set { connected = value; }
        }

        public int Download
        {
            get { return downloadRate; }
        }

        public int Upload
        {
            get { return uploadRate; }
        }

        public int Dp
        {
            get { return dp; }
        }

        public int Up
        {
            get { return recipUpload; }
        }

        public double Ratio
        {
            get { return tyrantRatio; }
        }

        public int Estimated
        {
            get { return estimatedDownloadRate; }
        }

        public bool ImChoking
        {
            get { return amChoking; }
        }

        public bool ImInterested
        {
            get { return amInterested; }
        }

        public bool HesChoking
        {
            get { return isChoking; }
        }

        public bool HesInterested
        {
            get { return isInterested; }
        }

        public bool IsSeeder
        {
            get { return isSeeder; }
        }

        public int Requests
        {
            get { return outstandingRequests; }
        }

        /// <summary>
        /// How many piece requests he has satisfied for us
        /// </summary>
        public int PiecesReceived
        {
            get { return piecesSentFrom; }
        }

        /// <summary>
        /// How many piece requests we've satisfied for him
        /// </summary>
        public int PiecesSent
        {
            get { return piecesSentTo; }
        }

        public bool Encrypted
        {
            get { return encrypted; }
        }

        #endregion

        public PeerInfo(PeerIdInternal id)
        {
            UpdateData(id);
        }

        public bool Equals(PeerInfo info)
        {
            return this.connectionUri.Equals(info.connectionUri);
        }

        public override int GetHashCode()
        {
            return this.connectionUri.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is PeerInfo)
                return Equals(obj as PeerInfo);
            return false;
        }

        public void UpdateData(PeerIdInternal pIdInternal)
        {
            this.connectionUri = pIdInternal.PublicId.Location;
            this.connected = pIdInternal.PublicId.Connection != null;
            this.downloadRate = pIdInternal.PublicId.Monitor.DownloadSpeed;
            this.uploadRate = pIdInternal.PublicId.Monitor.UploadSpeed; // Upload Measured
            this.dp = pIdInternal.PublicId.GetDownloadRate();
            this.recipUpload = pIdInternal.PublicId.UploadRateForRecip;
            this.tyrantRatio = pIdInternal.PublicId.Ratio;
            this.estimatedDownloadRate = pIdInternal.PublicId.EstimatedDownloadRate;
            this.amChoking = pIdInternal.PublicId.AmChoking;
            this.amInterested = pIdInternal.PublicId.AmInterested;
            this.isChoking = pIdInternal.PublicId.IsChoking;
            this.isInterested = pIdInternal.PublicId.IsInterested;
            this.isSeeder = pIdInternal.PublicId.IsSeeder;
            this.outstandingRequests = ((pIdInternal.Connection != null) ? pIdInternal.Connection.AmRequestingPiecesCount : 0);
            this.piecesSentFrom = pIdInternal.PublicId.PiecesReceived; // Pieces Received From
            this.piecesSentTo = pIdInternal.PublicId.PiecesSent; // Pieces Sent To
            this.encrypted = !(pIdInternal.PublicId.Encryptor is PlainTextEncryption);
        }
    }
}

#endif