using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using MonoTorrent.Client.PeerMessages;
using MonoTorrent.BEncoding;

namespace MonoTorrent.Client
{
	internal class MessageHandler
	{
		private struct AsyncMessageDetails
		{
			public byte[] Buffer;
			public int StartOffset;
			public int Count;
		}

		private Queue<PeerConnectionID> cleanUpQueue = new Queue<PeerConnectionID>();
		private readonly object queuelock = new object();
		private Queue<Nullable<KeyValuePair<PeerConnectionID, AsyncMessageDetails>>> queue;
		private Queue<PeerConnectionID> sendQueue;
		private bool messageLoopActive;
		private Thread messageLoopThread;
		private ManualResetEvent waitHandle;

		public MessageHandler()
		{
			this.queue = new Queue<KeyValuePair<PeerConnectionID, AsyncMessageDetails>?>();
			this.sendQueue = new Queue<PeerConnectionID>();
			this.waitHandle = new ManualResetEvent(false);
		}

		private void MessageLoop()
		{
			PeerConnectionID sendMessageToId;
			PeerConnectionID cleanupId;
			Nullable<KeyValuePair<PeerConnectionID, AsyncMessageDetails>> receivedMessage;

			while (this.messageLoopActive)
			{
				receivedMessage = null;
				cleanupId = null;
				sendMessageToId = null;
			
				lock (this.queuelock)
				{
					if (this.queue.Count > 0)
						receivedMessage = this.queue.Dequeue();

					if (this.sendQueue.Count > 0)
						sendMessageToId = this.sendQueue.Dequeue();

					if (this.cleanUpQueue.Count > 0)
						cleanupId = this.cleanUpQueue.Dequeue();

					if (this.queue.Count == 0 && this.sendQueue.Count == 0 && this.cleanUpQueue.Count == 0)
						this.waitHandle.Reset();
				}

				if (receivedMessage.HasValue)
					HandleMessage(receivedMessage.Value.Key, receivedMessage.Value.Value);

				if (sendMessageToId != null)
					SendMessage(sendMessageToId);

				if (cleanupId != null)
					ClientEngine.ConnectionManager.AsyncCleanupSocket(cleanupId, true, "Async Cleanup");

				this.waitHandle.WaitOne();
			}
		}

		private void SendMessage(PeerConnectionID id)
		{
			lock (id.TorrentManager.resumeLock)
				lock (id)
					if (id.Peer.Connection != null)
						ClientEngine.ConnectionManager.ProcessQueue(id);
		}

		public void Start()
		{
			if (this.messageLoopThread != null)
				throw new InvalidOperationException("Message loop already started");

			messageLoopActive = true;
			this.messageLoopThread = new Thread(new ThreadStart(MessageLoop));
			this.messageLoopThread.Start();
		}

		public void Stop()
		{
			if (this.messageLoopThread == null)
				throw new InvalidOperationException("Message loop is not running");

			messageLoopActive = false;
			this.waitHandle.Set();
			this.messageLoopThread.Join(500);
		}

		private void HandleMessage(PeerConnectionID id, AsyncMessageDetails messageDetails)
		{
			try
			{
				lock (id.TorrentManager.listLock)
				{
					lock (id)
					{
						if (id.Peer.Connection == null)
							return;

						try
						{
							IPeerMessageInternal message = PeerwireEncoder.Decode(messageDetails.Buffer, 0, messageDetails.Count, id.TorrentManager);

							// Fire the event to say we recieved a new message
							//if (this.PeerMessageTransferred != null)
							//	this.PeerMessageTransferred(id, new PeerMessageEventArgs((IPeerMessage)message, Direction.Incoming));

							message.Handle(id);
						}
						catch (Exception)
						{
#warning Do this in a better way so as to not hide an important exception!
							return;
						}
					}
				}
			}
			finally
			{
				ClientEngine.BufferManager.FreeBuffer(ref messageDetails.Buffer);
			}
		}


		public bool IsActive
		{
			get { return this.messageLoopThread != null; }
		}


		internal void EnqueueReceived(PeerConnectionID id, byte[] buffer, int startOffset, int count)
		{
			byte[] messageBuffer = BufferManager.EmptyBuffer;
			ClientEngine.BufferManager.GetBuffer(ref messageBuffer, buffer.Length);
			Buffer.BlockCopy(buffer, startOffset, messageBuffer, 0, count);

			AsyncMessageDetails details = new AsyncMessageDetails();
			details.Buffer = messageBuffer;
			details.Count = count;
			details.StartOffset = 0;

			lock (this.queuelock)
			{
				this.queue.Enqueue(new KeyValuePair<PeerConnectionID, AsyncMessageDetails>(id, details));
				this.waitHandle.Set();
			}
		}


		internal void EnqueueSend(PeerConnectionID id)
		{
			lock (this.queuelock)
			{
				this.sendQueue.Enqueue(id);
				this.waitHandle.Set();
			}
		}

		internal void EnqueueCleanup(PeerConnectionID id)
		{
			lock (this.queuelock)
			{
				this.cleanUpQueue.Enqueue(id);
				this.waitHandle.Set();
			}
		}
	}
}
