using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    //This class rate limits the update frequency, and asks the server to send us less messages
    public class DynamicTickWorker
    {
        public bool workerEnabled;
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
        //Services
        private DMPGame dmpGame;
        private NetworkWorker networkWorker;

        public DynamicTickWorker(DMPGame dmpGame, NetworkWorker networkWorker)
        {
            this.dmpGame = dmpGame;
            this.networkWorker = networkWorker;
            maxSecondryVesselsPerTick = MASTER_MAX_SECONDARY_VESSELS;
            sendTickRate = MASTER_MAX_TICKS_PER_SECOND;
            dmpGame.updateEvent.Add(Update);
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
            long currentQueuedBytes = networkWorker.GetStatistics("QueuedOutBytes");

            //Tick Rate math - Clamp to minimum value.
            long newTickRate = MASTER_MAX_TICKS_PER_SECOND - (currentQueuedBytes / (MASTER_TICK_SCALING / (MASTER_MAX_TICKS_PER_SECOND - MASTER_MIN_TICKS_PER_SECOND)));
            sendTickRate = newTickRate > MASTER_MIN_TICKS_PER_SECOND ? (int)newTickRate : MASTER_MIN_TICKS_PER_SECOND;

            //Secondary vessel math - Clamp to minimum value
            long newSecondryVesselsPerTick = MASTER_MAX_SECONDARY_VESSELS - (currentQueuedBytes / (MASTER_SECONDARY_VESSELS_SCALING / (MASTER_MAX_SECONDARY_VESSELS - MASTER_MIN_SECONDARY_VESSELS)));
            maxSecondryVesselsPerTick = newSecondryVesselsPerTick > MASTER_MIN_SECONDARY_VESSELS ? (int)newSecondryVesselsPerTick : MASTER_MIN_SECONDARY_VESSELS;
        }

        public void Stop()
        {
            workerEnabled = false;
            dmpGame.updateEvent.Remove(Update);
        }
    }
}