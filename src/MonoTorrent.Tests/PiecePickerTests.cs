using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using MonoTorrent.Client;
using MonoTorrentTests;
using MonoTorrent.Client.Messages.Standard;
using MonoTorrent.Client.Messages.FastPeer;
using MonoTorrent.Client.StandardMessageTests;

namespace MonoTorrent.Tests
{
    [TestFixture]
    public class PiecePickerTests
    {
        static void Main(string[] args)
        {
            PiecePickerTests t = new PiecePickerTests();
            t.Setup();
            t.RequestFastSeeder();
            t.Setup();
            t.RequestFastNotSeeder();
            t.Setup();
            t.RequestFastHaveEverything();
            t.Setup();
            t.RequestWhenSeeder();
            t.Setup();
            t.NoInterestingPieces();
            t.Setup();
            t.CancelRequests();
            t.Setup();
            t.RejectRequests();
            t.Setup();
            t.PeerChoked();
            t.Setup();
            t.FastPeerChoked();
            t.Setup();
            t.ChokeThenClose();
        }
        protected PeerId peer;
        protected List<PeerId> peers;
        protected StandardPicker picker;
        protected TestRig rig;


        [SetUp]
        public virtual void Setup()
        {
            // Yes, this is horrible. Deal with it.
            rig = new TestRig("");
            peers = new List<PeerId>();
            picker = new StandardPicker();
            picker.Initialise(rig.Manager.Bitfield, rig.Manager.Torrent.Files, new List<Piece>(), new MonoTorrent.Common.BitField(rig.Manager.Bitfield.Length));
            peer = new PeerId(new Peer(new string('a', 20), new Uri("tcp://BLAH")), rig.Manager);
            for (int i = 0; i < 20; i++)
            {
                PeerId p = new PeerId(new Peer(new string(i.ToString()[0], 20), new Uri("tcp://" + i)), rig.Manager);
                p.SupportsFastPeer = true;
                peers.Add(p);
            }
        }

        [Test]
        public void RequestFastSeeder()
        {
            peers[0].SupportsFastPeer = true;
            peers[0].IsAllowedFastPieces.AddRange(new int[] { 1, 2, 3, 5, 8, 13, 21 });

            peers[0].BitField.SetAll(true); // Lets pretend he has everything
            for (int i = 0; i < 7; i++)
                for (int j = 0; j < 16; j++)
                    Assert.IsNotNull(picker.PickPiece(peers[0], peers));

            Assert.IsNull(picker.PickPiece(peers[0], peers));
        }
        [Test]
        public void RequestFastNotSeeder()
        {
            peers[0].SupportsFastPeer = true;
            peers[0].IsAllowedFastPieces.AddRange(new int[] { 1, 2, 3, 5, 8, 13, 21 });

            peers[0].BitField.SetAll(true);
            peers[0].BitField[1] = false;
            peers[0].BitField[3] = false;
            peers[0].BitField[5] = false;

            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 16; j++)
                {
                    RequestMessage m = picker.PickPiece(peers[0], peers);
                    Assert.IsTrue(m.PieceIndex == 2 || m.PieceIndex == 8 || m.PieceIndex == 13 || m.PieceIndex == 21);
                } 

            Assert.IsNull(picker.PickPiece(peers[0], peers));
        }
        [Test]
        public void RequestFastHaveEverything()
        {
            peers[0].SupportsFastPeer = true;
            peers[0].IsAllowedFastPieces.AddRange(new int[] { 1, 2, 3, 5, 8, 13, 21 });

            peers[0].BitField.SetAll(true);
            picker.MyBitField.SetAll(true);

            Assert.IsNull(picker.PickPiece(peers[0], peers));
        }

        [Test]
        public void RequestChoked()
        {
            Assert.IsNull(picker.PickPiece(peers[0], peers));
        }

        [Test]
        public void RequestWhenSeeder()
        {
            picker.MyBitField.SetAll(true);
            peers[0].BitField.SetAll(true);
            peers[0].IsChoking = false;

            Assert.IsNull(picker.PickPiece(peers[0], peers));
        }

        [Test]
        public void NoInterestingPieces()
        {
            peer.IsChoking = false;
            for (int i = 0; i < picker.MyBitField.Length; i++)
                if (i % 2 == 0)
                {
                    peer.BitField[i] = true;
                    picker.MyBitField[i] = true;
                }
            Assert.IsNull(picker.PickPiece(peer, peers));
        }

        [Test]
        public void CancelRequests()
        {
            List<RequestMessage> messages = new List<RequestMessage>();
            peer.IsChoking = false;
            peer.BitField.SetAll(true);

            RequestMessage m;
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages.Add(m);

            picker.RemoveRequests(peer);

            List<RequestMessage> messages2 = new List<RequestMessage>();
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages2.Add(m);

            Assert.AreEqual(messages.Count, messages2.Count, "#1");
            for (int i = 0; i < messages.Count; i++)
                Assert.IsTrue(messages2.Contains(messages[i]));
        }

        [Test]
        public void RejectRequests()
        {
            List<RequestMessage> messages = new List<RequestMessage>();
            peer.IsChoking = false;
            peer.BitField.SetAll(true);

            RequestMessage m;
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages.Add(m);

            foreach (RequestMessage message in messages)
                picker.ReceivedRejectRequest(peer, new RejectRequestMessage(message.PieceIndex, message.StartOffset, message.RequestLength));

            List<RequestMessage> messages2 = new List<RequestMessage>();
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages2.Add(m);

            Assert.AreEqual(messages.Count, messages2.Count, "#1");
            for (int i = 0; i < messages.Count; i++)
                Assert.IsTrue(messages2.Contains(messages[i]), "#2." + i);
        }

        [Test]
        public void PeerChoked()
        {
            List<RequestMessage> messages = new List<RequestMessage>();
            peer.IsChoking = false;
            peer.BitField.SetAll(true);

            RequestMessage m;
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages.Add(m);

            picker.ReceivedChokeMessage(peer);

            List<RequestMessage> messages2 = new List<RequestMessage>();
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages2.Add(m);

            Assert.AreEqual(messages.Count, messages2.Count, "#1");
            for (int i = 0; i < messages.Count; i++)
                Assert.IsTrue(messages2.Contains(messages[i]), "#2." + i);
        }

        [Test]
        public void FastPeerChoked()
        {
            List<RequestMessage> messages = new List<RequestMessage>();
            peer.IsChoking = false;
            peer.BitField.SetAll(true);
            peer.SupportsFastPeer = true;

            RequestMessage m;
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages.Add(m);

            picker.ReceivedChokeMessage(peer);

            List<RequestMessage> messages2 = new List<RequestMessage>();
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages2.Add(m);

            Assert.AreEqual(0, messages2.Count, "#1");
        }

        [Test]
        public void ChokeThenClose()
        {
            List<RequestMessage> messages = new List<RequestMessage>();
            peer.IsChoking = false;
            peer.BitField.SetAll(true);
            peer.SupportsFastPeer = true;

            RequestMessage m;
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages.Add(m);

            picker.ReceivedChokeMessage(peer);
            picker.RemoveRequests(peer);

            List<RequestMessage> messages2 = new List<RequestMessage>();
            while ((m = picker.PickPiece(peer, peers)) != null)
                messages2.Add(m);

            Assert.AreEqual(messages.Count, messages2.Count, "#1");
            for (int i = 0; i < messages.Count; i++)
                Assert.IsTrue(messages2.Contains(messages[i]), "#2." + i);
        }
    }
}
