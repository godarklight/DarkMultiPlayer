using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    //This class rate limits the update frequency, and asks the server to send us less messages
    public class DynamicTickWorker
    {
        public bool enabled;
        private Client parent;
        private float lastDynamicTickRateCheck;
        private float lastDynamicTickRateChange;
        private const float DYNAMIC_TICK_RATE_CHECK_INTERVAL = 1f;
        private const float DYNAMIC_TICK_RATE_CHANGE_INTERVAL = 5f;
        //Twiddle these knobs
        private const int MASTER_MIN_TICKS_PER_SECOND = 1;
        private const int MASTER_MAX_TICKS_PER_SECOND = 5;
        private const int MASTER_MIN_SECONDRY_VESSELS_PER_SECOND = 1;
        private const int MASTER_MAX_SECONDRY_VESSELS_PER_SECOND = 15;
        private const int MASTER_MESSAGE_TO_TICK_RATE_DECREASE = 10;
        private const int MASTER_MESSAGE_TO_TICK_RATE_INCREASE = 30;
        private const int MASTER_MESSAGE_TO_PANIC = 200;
        //Constructor
        public DynamicTickWorker(Client parent)
        {
            this.parent = parent;
            Reset();
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
        //Main worker hook
        public void Update()
        {
            if (enabled)
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
                int outgoingHigh = parent.vesselWorker.GetStatistics("HighPriorityQueueLength");
                int outgoingSplit = parent.vesselWorker.GetStatistics("SplitPriorityQueueLength");
                int outgoingLow = parent.vesselWorker.GetStatistics("LowPriorityQueueLength");
                int outgoingTotal = outgoingHigh + outgoingSplit + outgoingLow;
                if (outgoingTotal < MASTER_MESSAGE_TO_PANIC)
                {
                    //Things are normal, detect lag and reduce our send rates.
                    if (maxSecondryVesselsPerTick == MASTER_MIN_SECONDRY_VESSELS_PER_SECOND)
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
                    if (sendTickRate == MASTER_MAX_TICKS_PER_SECOND) {
                        if ((outgoingTotal < MASTER_MESSAGE_TO_TICK_RATE_INCREASE) && (maxSecondryVesselsPerTick < MASTER_MAX_SECONDRY_VESSELS_PER_SECOND))
                        {
                            lastDynamicTickRateChange = UnityEngine.Time.realtimeSinceStartup;
                            maxSecondryVesselsPerTick++;
                            return;
                        }
                        if ((outgoingTotal > MASTER_MESSAGE_TO_TICK_RATE_DECREASE) && (maxSecondryVesselsPerTick > MASTER_MIN_SECONDRY_VESSELS_PER_SECOND))
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
                    maxSecondryVesselsPerTick = MASTER_MIN_SECONDRY_VESSELS_PER_SECOND;
                }
            }
        }
        //Main worker reset
        public void Reset()
        {
            lastDynamicTickRateCheck = 0f;
            maxSecondryVesselsPerTick = MASTER_MAX_SECONDRY_VESSELS_PER_SECOND;
            sendTickRate = MASTER_MAX_TICKS_PER_SECOND;
            enabled = false;
        }
    }
}