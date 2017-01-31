using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    //This class rate limits the update frequency, and asks the server to send us less messages
    public class DynamicTickWorker
    {
        public bool workerEnabled;
        private static DynamicTickWorker singleton;
        private float lastDynamicTickRateCheck;
        private const float DYNAMIC_TICK_RATE_CHECK_INTERVAL = 1f;
        //Twiddle these knobs
        private const int MASTER_MIN_TICKS_PER_SECOND = 1;
        private const int MASTER_MAX_TICKS_PER_SECOND = 5;
        private const int MASTER_MIN_SECONDARY_VESSELS = 1;
        private const int MASTER_MAX_SECONDARY_VESSELS = 15;
        //500KB
        private const int MASTER_TICK_SCALING = 100 * 1024;
        //1000KB
        private const int MASTER_SECONDARY_VESSELS_SCALING = 200 * 1024;

        public static DynamicTickWorker fetch
        {
            get
            {
                return singleton;
            }
        }
        //Restricted access variables
        public int maxSecondryVesselsPerTick
        {
            private set;
            get;
        }

        public int sendTickRate
        {
            private set;
            get;
        }

        private void Update()
        {
            if (workerEnabled)
            {
                if ((Client.realtimeSinceStartup - lastDynamicTickRateCheck) > DYNAMIC_TICK_RATE_CHECK_INTERVAL)
                {
                    lastDynamicTickRateCheck = Client.realtimeSinceStartup;
                    CalculateRates();
                }
            }
        }

        private void CalculateRates()
        {
            long currentQueuedBytes = NetworkWorker.fetch.GetStatistics("QueuedOutBytes");

            //Tick Rate math - Clamp to minimum value.
            long newTickRate = MASTER_MAX_TICKS_PER_SECOND - (currentQueuedBytes / (MASTER_TICK_SCALING / (MASTER_MAX_TICKS_PER_SECOND - MASTER_MIN_TICKS_PER_SECOND)));
            sendTickRate = newTickRate > MASTER_MIN_TICKS_PER_SECOND ? (int)newTickRate : MASTER_MIN_TICKS_PER_SECOND;

            //Secondary vessel math - Clamp to minimum value
            long newSecondryVesselsPerTick = MASTER_MAX_SECONDARY_VESSELS - (currentQueuedBytes / (MASTER_SECONDARY_VESSELS_SCALING / (MASTER_MAX_SECONDARY_VESSELS - MASTER_MIN_SECONDARY_VESSELS)));
            maxSecondryVesselsPerTick = newSecondryVesselsPerTick > MASTER_MIN_SECONDARY_VESSELS ? (int)newSecondryVesselsPerTick : MASTER_MIN_SECONDARY_VESSELS;
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    singleton.workerEnabled = false;
                    Client.updateEvent.Remove(singleton.Update);
                }
                singleton = new DynamicTickWorker();
                singleton.maxSecondryVesselsPerTick = MASTER_MAX_SECONDARY_VESSELS;
                singleton.sendTickRate = MASTER_MAX_TICKS_PER_SECOND;
                Client.updateEvent.Add(singleton.Update);
            }
        }
    }
}