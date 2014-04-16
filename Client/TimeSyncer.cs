using System;
using System.Collections.Generic;

namespace DarkMultiPlayer
{
    public class TimeSyncer
    {
        public bool synced;
        public bool locked;
        public bool enabled;
        private const float MAX_CLOCK_SKEW = 5f;
        private const float MIN_CLOCK_RATE = 0.5f;
        private const float MAX_CLOCK_RATE = 1.5f;
        private const float SYNC_TIME_INTERVAL = 30f;
        private const float CLOCK_SET_INTERVAL = .1f;
        private const int SYNC_TIME_VALID = 4;
        private const int SYNC_TIME_MAX = 10;
        private float lastSyncTime = 0f;
        private float lastClockSkew = 0f;
        private List<long> clockOffset;
        private List<long> networkLatency;
        private long clockOffsetAverage;
        private long networkLatencyAverage;
        private long serverTimeLock;
        private double planetariumTimeLock;
        private float gameSpeedLock;
        public Client parent;

        public TimeSyncer(Client parent)
        {
            this.parent = parent;
            Reset();
            if (this.parent != null)
            {
                //Shutup compiler
            }
        }

        public void FixedUpdate()
        {
            if (synced)
            {

                if ((UnityEngine.Time.realtimeSinceStartup - lastSyncTime) > SYNC_TIME_INTERVAL)
                {
                    lastSyncTime = UnityEngine.Time.realtimeSinceStartup;
                    parent.networkWorker.SendTimeSync();
                }
            }
            if (synced && locked && enabled)
            {
                //Set the universe time here
                SyncTime();
            }
        }
        //Skew or set the clock
        private void SyncTime()
        {
            if ((UnityEngine.Time.realtimeSinceStartup - lastClockSkew) > CLOCK_SET_INTERVAL)
            {
                lastClockSkew = UnityEngine.Time.realtimeSinceStartup;
                if (CanSyncTime())
                {
                    double currentTime = Planetarium.GetUniversalTime();
                    double targetTime = GetCurrentTime();
                    if (Math.Abs(currentTime - targetTime) > MAX_CLOCK_SKEW)
                    {

                    }
                }
            }
        }

        private void StepClock(double targetTick)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                try
                {
                    OrbitPhysicsManager.HoldVesselUnpack(1);
                }
                catch
                {
                    DarkLog.Debug("Failed to hold vessel unpack");
                    return;
                }
                foreach (Vessel v in FlightGlobals.fetch.vessels)
                {
                    if (v != FlightGlobals.fetch.activeVessel && v.packed)
                    {
                        v.GoOnRails();
                    }
                    if (v == FlightGlobals.fetch.activeVessel)
                    {
                        if (!SituationIsGrounded(v.situation))
                        {
                            v.GoOnRails();
                        }
                    }
                }
                Planetarium.SetUniversalTime(targetTick);
            }
            else
            {
                Planetarium.SetUniversalTime(targetTick);
            }
        }

        private bool SituationIsGrounded(Vessel.Situations situation)
        {
            switch (situation)
            {
                case Vessel.Situations.LANDED:
                case Vessel.Situations.PRELAUNCH:
                case Vessel.Situations.SPLASHED:
                    return true;
            }
            return false;
        }

        public void LockTime(long serverTimeLock, double planetariumTimeLock, float gameSpeedLock)
        {
            this.serverTimeLock = serverTimeLock;
            this.planetariumTimeLock = planetariumTimeLock;
            this.gameSpeedLock = gameSpeedLock;
            locked = true;
        }

        public double GetCurrentTime()
        {
            if (synced && locked)
            {
                long serverTime = DateTime.UtcNow.Ticks + clockOffsetAverage;
                long realTimeSinceLock = serverTime - serverTimeLock;
                double realTimeSinceLockSeconds = realTimeSinceLock / 10000000d;
                double adjustedTimeSinceLockSeconds = realTimeSinceLockSeconds * gameSpeedLock;
                return planetariumTimeLock + adjustedTimeSinceLockSeconds;
            }
            return 0;
        }

        private bool CanSyncTime()
        {
            bool canSync;
            switch (HighLogic.LoadedScene)
            {
                case GameScenes.TRACKSTATION:
                case GameScenes.FLIGHT:
                case GameScenes.EDITOR:
                case GameScenes.SPH:
                case GameScenes.SPACECENTER:
                    canSync = true;
                    break;
                default :
                    canSync = false;
                    break;
            }
            return canSync;
        }

        public void Reset()
        {
            clockOffset = new List<long>();
            networkLatency = new List<long>();
            synced = false;
            locked = false;
        }

        public void HandleSyncTime(long clientSend, long serverReceive, long serverSend)
        {
            long clientReceive = DateTime.UtcNow.Ticks;
            long clientLatency = (clientReceive - clientSend) - (serverSend - serverReceive);
            long clientOffset = ((serverReceive - clientSend) + (serverSend - clientReceive)) / 2;
            clockOffset.Add(clientOffset);
            networkLatency.Add(clientLatency);
            if (clockOffset.Count > SYNC_TIME_MAX)
            {
                clockOffset.RemoveAt(0);
            }
            if (networkLatency.Count > SYNC_TIME_MAX)
            {
                networkLatency.RemoveAt(0);
            }
            long clockOffsetTotal = 0;
            //Calculate the average for the offset and latency.
            foreach (long currentOffset in clockOffset)
            {
                clockOffsetTotal += currentOffset;
            }
            clockOffsetAverage = clockOffsetTotal / clockOffset.Count;

            long networkLatencyTotal = 0;
            foreach (long currentLatency in networkLatency)
            {
                networkLatencyTotal += currentLatency;
            }
            networkLatencyAverage = networkLatencyTotal / networkLatency.Count;

            //Check if we are now synced
            if ((clockOffset.Count > SYNC_TIME_VALID) && !synced)
            {
                synced = true;
                float clockOffsetAverageMs = clockOffsetAverage / 10000f;
                float networkLatencyMs = networkLatencyAverage / 10000f;
                DarkLog.Debug("Initial clock syncronized, offset " + clockOffsetAverageMs + "ms, latency " + networkLatencyMs + "ms");
            }

            //Ask for another time sync if we aren't synced yet.
            if (!synced)
            {
                lastSyncTime = UnityEngine.Time.realtimeSinceStartup;
                parent.networkWorker.SendTimeSync();
            }
        }
    }
}

