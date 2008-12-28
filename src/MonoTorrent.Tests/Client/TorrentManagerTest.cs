using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using MonoTorrent.Client.Connections;
using MonoTorrent.Client.Messages.Standard;
using MonoTorrent.Common;
using MonoTorrent.Client.Messages;
using System.Threading;

namespace MonoTorrent.Client
{

    [TestFixture]
    public class TorrentManagerTest
    {
        //static void Main()
        //{
        //    TorrentManagerTest t = new TorrentManagerTest();
        //    t.Setup();
        //    t.UnregisteredAnnounce();
        //}
        TestRig rig;
        ConnectionPair conn;

        [SetUp]
        public void Setup()
        {
            rig = new TestRig("", new TestWriter());
            conn = new ConnectionPair(51515);
        }
        [TearDown]
        public void Teardown()
        {
            rig.Dispose();
            conn.Dispose();
        }

        [Test]
        public void AddConnectionToStoppedManager()
        {
            MessageBundle bundle = new MessageBundle();

            // Create the handshake and bitfield message
            bundle.Messages.Add(new HandshakeMessage(rig.Manager.Torrent.InfoHash, "11112222333344445555", VersionInfo.ProtocolStringV100));
            bundle.Messages.Add(new BitfieldMessage(rig.Torrent.Pieces.Count));
            byte[] data = bundle.Encode();

            // Add the 'incoming' connection to the engine and send our payload
            rig.Listener.Add(rig.Manager, conn.Incoming);
            conn.Outgoing.EndSend(conn.Outgoing.BeginSend(data, 0, data.Length, null, null));

            try { conn.Outgoing.EndReceive(conn.Outgoing.BeginReceive(data, 0, data.Length, null, null)); }
            catch {
            	Assert.IsFalse(conn.Incoming.Connected, "#1");
//                Assert.IsFalse(conn.Outgoing.Connected, "#2");
            	return;
            }

            Assert.Fail ("The outgoing connection should've thrown an exception");
        }

        [Test]
        public void UnregisteredAnnounce()
        {
            rig.Engine.Unregister(rig.Manager);
            rig.Tracker.AddPeer(new Peer("", new Uri("tcp://myCustomTcpSocket")));
            Assert.AreEqual(0, rig.Manager.Peers.Available, "#1");
            rig.Tracker.AddFailedPeer(new Peer("", new Uri("tcp://myCustomTcpSocket")));
            Assert.AreEqual(0, rig.Manager.Peers.Available, "#2");
        }

        [Test]
        public void ReregisterManager()
        {
            ManualResetEvent handle = new ManualResetEvent(false);
            rig.Manager.TorrentStateChanged += delegate(object sender, TorrentStateChangedEventArgs e)
            {
                if (e.OldState == TorrentState.Hashing)
                    handle.Set();
            };
            rig.Manager.HashCheck(false);

            handle.WaitOne();
            handle.Reset();

            rig.Engine.Unregister(rig.Manager);
            TestRig rig2 = new TestRig("", new TestWriter());
            rig2.Engine.Unregister(rig2.Manager);
            rig.Engine.Register(rig2.Manager);
            rig2.Manager.TorrentStateChanged += delegate(object sender, TorrentStateChangedEventArgs e)
            {
                if (e.OldState == TorrentState.Hashing)
                    handle.Set();
            };
            rig2.Manager.HashCheck(true);
            handle.WaitOne();
            rig2.Dispose();
        }

        [Test]
        public void StopTest()
        {
            ManualResetEvent h = new ManualResetEvent(false);

            rig.Manager.TorrentStateChanged += delegate(object o, TorrentStateChangedEventArgs e)
            {
                if (e.OldState == TorrentState.Hashing)
                    h.Set();
            };

            rig.Manager.Start();
            h.WaitOne();
            Assert.IsTrue(rig.Manager.Stop().WaitOne(15000, false));
        }

        [Test]
        public void NoAnnouncesTest()
        {
            rig.TorrentDict.Remove("announce-list");
            rig.TorrentDict.Remove("announce");
            Torrent t = Torrent.Load(rig.TorrentDict);
            rig.Engine.Unregister(rig.Manager);
            TorrentManager manager = new TorrentManager(t, "", new TorrentSettings());
            rig.Engine.Register(manager);

            ManualResetEvent handle = new ManualResetEvent(false);
            manager.TorrentStateChanged += delegate(object o, TorrentStateChangedEventArgs e) {
                if (e.NewState == TorrentState.Downloading)
                    handle.Set();
            };
            manager.Start();
            handle.WaitOne();
            System.Threading.Thread.Sleep(1000);

            Assert.IsTrue(manager.Stop().WaitOne(10000, true), "#1");
            Assert.IsTrue(manager.TrackerManager.Announce().WaitOne(10000, true), "#2"); ;
        }

		[Test]
		public void UnsupportedTrackers ()
		{
			MonoTorrentCollection<string> tier = new MonoTorrentCollection<string> ();
			tier.Add ("fake://123.123.123.2:5665");
			rig.Torrent.AnnounceUrls.Add (tier);
			TorrentManager manager = new TorrentManager (rig.Torrent, "", new TorrentSettings());
			foreach (MonoTorrent.Client.Tracker.TrackerTier t in manager.TrackerManager)
			{
				Assert.IsTrue (t.Trackers.Count > 0, "#1");
			}
		}

        [Test]
        public void AnnounceWhenComplete()
        {
            AutoResetEvent handle = new AutoResetEvent(false);
            rig.Manager.TorrentStateChanged += delegate (object o, TorrentStateChangedEventArgs e) {
                if(e.NewState != TorrentState.Hashing)
                    handle.Set();
            };
            rig.Manager.Start();
            handle.WaitOne();
            Assert.AreEqual(TorrentState.Downloading, rig.Manager.State, "#1");
            Assert.AreEqual(1, rig.Tracker.AnnouncedAt.Count, "#2");
            rig.Manager.Bitfield.SetAll(true);
            handle.WaitOne();
            Assert.AreEqual(TorrentState.Seeding, rig.Manager.State, "#3");
            Assert.AreEqual(2, rig.Tracker.AnnouncedAt.Count, "#4");
            Assert.IsTrue(rig.Manager.Stop().WaitOne(100), "Didn't stop");
            Assert.AreEqual(TorrentState.Stopped, rig.Manager.State, "#5");
            Assert.AreEqual(3, rig.Tracker.AnnouncedAt.Count, "#6");
        }
    }
}
