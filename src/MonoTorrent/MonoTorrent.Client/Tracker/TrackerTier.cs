using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using MonoTorrent.Common;

namespace MonoTorrent.Client.Tracker
{
    public class TrackerTier : IEnumerable<Tracker>
    {
        #region Private Fields

        private bool sendingStartedEvent;
        private bool sentStartedEvent;
        private Tracker[] trackers;

        #endregion Private Fields


        #region Properties

        internal bool SendingStartedEvent
        {
            get { return this.sendingStartedEvent; }
            set { this.sendingStartedEvent = value; }
        }

        internal bool SentStartedEvent
        {
            get { return this.sentStartedEvent; }
            set { this.sentStartedEvent = value; }
        }

        public Tracker[] Trackers
        {
            get { return this.trackers; }
        }

        #endregion Properties


        #region Constructors

        internal TrackerTier(IEnumerable<string> trackerUrls)
        {
            Uri result;
            List<Tracker> trackerList = new List<Tracker>();

            foreach (string trackerUrl in trackerUrls)
            {
                // FIXME: Debug spew?
                if (!Uri.TryCreate(trackerUrl, UriKind.Absolute, out result))
                {
                    Logger.Log(null, "TrackerTier - Invalid tracker Url specified: {0}", trackerUrl);
                    continue;
                }

                Tracker tracker = TrackerFactory.Create(result.Scheme, result);
                if (tracker != null)
                {
                    tracker.Tier = this;
                    trackerList.Add(tracker);
                }
                else
                {
                    Console.Error.WriteLine("Unsupported protocol {0}", result);                // FIXME: Debug spew?
                }
            }

            this.trackers = trackerList.ToArray();
        }

        #endregion Constructors


        #region Methods

        internal int IndexOf(Tracker tracker)
        {
            return Array.IndexOf<Tracker>(trackers, tracker);
        }


        public IEnumerator<Tracker> GetEnumerator()
        {
            for (int i = 0; i < trackers.Length; i++)
                yield return trackers[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion Methods
    }
}
