using System;
using System.Collections.Generic;
using System.Text;

namespace MonoTorrent.Client
{
    public class TrackerTier
    {
        #region Private Fields

        private bool sendingStartedEvent;
        private bool sentStartedEvent;
        private TrackerCollection trackers;

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

        public TrackerCollection Trackers
        {
            get { return this.trackers; }
        }

        #endregion Properties


        #region Constructors

        public TrackerTier(stringCollection trackerUrls, AsyncCallback announceCallback,
                        AsyncCallback scrapeCallback, EngineSettings engineSettings)
        {
            this.trackers = new TrackerCollection(trackerUrls.Count);
            for (int i = 0; i < trackerUrls.Count; i++)
                this.trackers[i] = new Tracker(trackerUrls[i], announceCallback, scrapeCallback, engineSettings);
        }

        #endregion Constructors


        #region Methods

        internal int IndexOf(Tracker tracker)
        {
            for (int i = 0; i < this.trackers.Count; i++)
                if (this.trackers[i].Equals(tracker))
                    return i;

            return -1;
        }

        #endregion Methods
    }
}
