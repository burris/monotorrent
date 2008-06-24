using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using MonoTorrent.Client.Messages;
using MonoTorrent.Client.Messages.UdpTracker;

namespace MonoTorrent.Client.Tracker
{
    public class UdpTracker : Tracker
    {
        private AnnounceParameters storedParams;
        private long connectionId;
        private UdpClient tracker;
        private Uri announceUrl;
        private IPEndPoint endpoint;
        bool hasConnected;
        bool amConnecting;

        public UdpTracker(Uri announceUrl)
        {
            this.announceUrl = announceUrl;
            CanScrape = false;
            tracker = new UdpClient(announceUrl.Host, announceUrl.Port);
            endpoint = (IPEndPoint)tracker.Client.RemoteEndPoint;
        }

        public override WaitHandle Announce(AnnounceParameters parameters)
        {
            LastUpdated = DateTime.Now;
            if (!hasConnected && amConnecting)
                return null;

            if (!hasConnected)
            {
                storedParams = parameters;
                amConnecting = true;
                Connect();
                return null;
            }

            AnnounceMessage m = new AnnounceMessage(connectionId, parameters);
            tracker.Send(m.Encode(), m.ByteLength);
            byte[] data = tracker.Receive(ref endpoint);
            UdpTrackerMessage message = UdpTrackerMessage.DecodeMessage(data, 0, data.Length);

            CompleteAnnounce(message);

            return null;
        }

        private void CompleteAnnounce(UdpTrackerMessage message)
        {
            TrackerConnectionID id = new TrackerConnectionID(this, false, MonoTorrent.Common.TorrentEvent.None, null);
            AnnounceResponseEventArgs e = new AnnounceResponseEventArgs(id);
            ErrorMessage error = message as ErrorMessage;
            if (error != null)
            {
                e.Successful = false;
                FailureMessage = error.Error;
            }
            else
            {
                AnnounceResponseMessage response = (AnnounceResponseMessage)message;
                e.Successful = true;
                e.Peers.AddRange(response.Peers);
            }

            RaiseAnnounceComplete(e);
        }

        private void Connect()
        {
            ConnectMessage message = new ConnectMessage();
            tracker.Connect(announceUrl.Host, announceUrl.Port);
            tracker.Send(message.Encode(), message.ByteLength);
            byte[] response = tracker.Receive(ref endpoint);
            ConnectResponseMessage m = (ConnectResponseMessage)UdpTrackerMessage.DecodeMessage(response, 0, response.Length);// new ConnectResponseMessage();

            connectionId = m.ConnectionId;
            hasConnected = true;
            amConnecting = false;
            Announce(storedParams);
            storedParams = null;
        }

        public override WaitHandle Scrape(ScrapeParameters parameters)
        {
            throw new Exception("The method or operation is not implemented.");
        }
    }
}
