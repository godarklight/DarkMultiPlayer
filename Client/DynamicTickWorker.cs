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
        private float lastDynamicTickRateChange;
        private const float DYNAMIC_TICK_RATE_CHECK_INTERVAL = 1f;
        private const float DYNAMIC_TICK_RATE_CHANGE_INTERVAL = 5f;
        //Twiddle these knobs
        private const int MASTER_MIN_TICKS_PER_SECOND = 1;
        private const int MASTER_MAX_TICKS_PER_SECOND = 5;
        private const int MASTER_MIN_SECONDRY_VESSELS = 1;
        private const int MASTER_MAX_SECONDRY_VESSELS = 15;
        private const int MASTER_MESSAGE_TO_TICK_RATE_DECREASE = 10;
        private const int MASTER_MESSAGE_TO_TICK_RATE_INCREASE = 30;
        private const int MASTER_MESSAGE_TO_PANIC = 200;

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
                if ((UnityEngine.Time.realtimeSinceStartup - lastDynamicTickRateCheck) > DYNAMIC_TICK_RATE_CHECK_INTERVAL)
                {
                    lastDynamicTickRateCheck = UnityEngine.Time.realtimeSinceStartup;
                    CalculateRates();
                }
            }
        }

        private void CalculateRates()
        {
            if ((UnityEngine.Time.realtimeSinceStartup - lastDynamicTickRateChange) > DYNAMIC_TICK_RATE_CHANGE_INTERVAL)
            {
                int outgoingHigh = NetworkWorker.fetch.GetStatistics("HighPriorityQueueLength");
                int outgoingSplit = NetworkWorker.fetch.GetStatistics("SplitPriorityQueueLength");
                int outgoingLow = NetworkWorker.fetch.GetStatistics("LowPriorityQueueLength");
                int outgoingTotal = outgoingHigh + outgoingSplit + outgoingLow;
                if (outgoingTotal < MASTER_MESSAGE_TO_PANIC)
                {
                    //Things are normal, detect lag and reduce our send rates.
                    if (maxSecondryVesselsPerTick == MASTER_MIN_SECONDRY_VESSELS)
                    {
                        if ((outgoingTotal < MASTER_MESSAGE_TO_TICK_RATE_INCREASE) && (sendTickRate < MASTER_MAX_TICKS_PER_SECOND))
                        {
                            lastDynamicTickRateChange = UnityEngine.Time.realtimeSinceStartup;
                            sendTickRate++;
                            return;
                        }
                        if ((outgoingTotal > MASTER_MESSAGE_TO_TICK_RATE_DECREASE) && (sendTickRate > MASTER_MIN_TICKS_PER_SECOND))
                        {
                            lastDynamicTickRateChange = UnityEngine.Time.realtimeSinceStartup;
                            sendTickRate--;
                            return;
                        }
                    }
                    if (sendTickRate == MASTER_MAX_TICKS_PER_SECOND)
                    {
                        if ((outgoingTotal < MASTER_MESSAGE_TO_TICK_RATE_INCREASE) && (maxSecondryVesselsPerTick < MASTER_MAX_SECONDRY_VESSELS))
                        {
                            lastDynamicTickRateChange = UnityEngine.Time.realtimeSinceStartup;
                            maxSecondryVesselsPerTick++;
                            return;
                        }
                        if ((outgoingTotal > MASTER_MESSAGE_TO_TICK_RATE_DECREASE) && (maxSecondryVesselsPerTick > MASTER_MIN_SECONDRY_VESSELS))
                        {
                            lastDynamicTickRateChange = UnityEngine.Time.realtimeSinceStartup;
                            maxSecondryVesselsPerTick--;
                            return;
                        }
                    }
                }
                else
                {
                    //Panic
                    DarkLog.Debug("High lag detected - Going into panic mode!");
                    lastDynamicTickRateChange = UnityEngine.Time.realtimeSinceStartup;
                    sendTickRate = MASTER_MIN_TICKS_PER_SECOND;
                    maxSecondryVesselsPerTick = MASTER_MIN_SECONDRY_VESSELS;
                }
            }
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    Client.updateEvent.Remove(singleton.Update);
                }
                singleton = new DynamicTickWorker();
                singleton.maxSecondryVesselsPerTick = MASTER_MAX_SECONDRY_VESSELS;
                singleton.sendTickRate = MASTER_MAX_TICKS_PER_SECOND;
                Client.updateEvent.Add(singleton.Update);
            }
        }
    }
}