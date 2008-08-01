using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using MonoTorrent.Dht.Listeners;
using MonoTorrent.Dht.Messages;
using System.Net;

namespace MonoTorrent.Dht.Tests
{
    public class TestListener : IListener
    {
        public event MessageReceived MessageReceived;

        private bool started;

        public bool Started
        {
            get { return started; }
        }

        public void Send(byte[] buffer, System.Net.IPEndPoint endpoint)
        {
            // Do nothing
        }
        public void RaiseMessageReceived(Message message, IPEndPoint endpoint)
        {
            RaiseMessageReceived(message.Encode(), endpoint);
        }

        public void RaiseMessageReceived(byte[] buffer, IPEndPoint endpoint)
        {
            if (MessageReceived != null)
                DhtEngine.MainLoop.Queue(delegate {
                    MessageReceived(buffer, endpoint);
                });
        }

        public void Start()
        {
            started = true;
        }

        public void Stop()
        {
            started = false;
        }
    }
}
