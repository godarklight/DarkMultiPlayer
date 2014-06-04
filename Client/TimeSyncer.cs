using System;
using System.Collections.Generic;
using UnityEngine;
using DarkMultiPlayerCommon;
using MessageStream;

namespace DarkMultiPlayer
{
    public class TimeSyncer
    {
        public bool locked
        {
            get;
            private set;
        }

        public bool synced
        {
            get;
            private set;
        }

        public bool workerEnabled;

        public int currentSubspace
        {
            get;
            private set;
        }

        public long clockOffsetAverage
        {
            get;
            private set;
        }

        public long networkLatencyAverage
        {
            get;
            private set;
        }

        public long serverLag
        {
            get;
            private set;
        }

        public float averageSkewRate
        {
            get;
            private set;
        }

        public float requestedRate
        {
            get
            {
                if (locked)
                {
                    float tempRate = subspaces[currentSubspace].subspaceSpeed * (1 / averageSkewRate);
                    //Request 0.5-1x speed.
                    if (tempRate < 0.5)
                    {
                        tempRate = 0.5f;
                    }
                    if (tempRate > 1f)
                    {
                        tempRate = 1f;
                    }
                    return tempRate;
                }
                return 1f;
            }
        }

        private const float MAX_CLOCK_SKEW = 5f;
        private const float MIN_CLOCK_RATE = 0.5f;
        private const float MAX_CLOCK_RATE = 1.5f;
        private const float SYNC_TIME_INTERVAL = 30f;
        private const float CLOCK_SET_INTERVAL = .1f;
        private const int SYNC_TIME_VALID = 4;
        private const int SYNC_TIME_MAX = 10;
        private float lastSyncTime = 0f;
        private float lastClockSkew = 0f;
        private List<long> clockOffset = new List<long>();
        private List<long> networkLatency = new List<long>();
        private List<float> skewList = new List<float>();
        private Dictionary<int, Subspace> subspaces = new Dictionary<int, Subspace>();
        public static TimeSyncer singleton;

        public TimeSyncer()
        {
            currentSubspace = -1;
            averageSkewRate = 1f;
        }

        public static TimeSyncer fetch
        {
            get
            {
                return singleton;
            }
        }

        public void FixedUpdate()
        {
            if (workerEnabled)
            {
                if (synced)
                {
                    if ((UnityEngine.Time.realtimeSinceStartup - lastSyncTime) > SYNC_TIME_INTERVAL)
                    {
                        lastSyncTime = UnityEngine.Time.realtimeSinceStartup;
                        NetworkWorker.fetch.SendTimeSync();
                    }

                    if (!locked && currentSubspace != -1)
                    {
                        LockSubspace(currentSubspace);
                    }
            
                    if (locked)
                    {
                        //Set the universe time here
                        SyncTime();
                    }
                }
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
                    double targetTime = GetUniverseTime();
                    double currentError = GetCurrentError();
                    if (Math.Abs(currentError) > MAX_CLOCK_SKEW)
                    {
                        StepClock(targetTime);
                    }
                    else
                    {
                        SkewClock(currentError);
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
                        if (SafeToStepClock(v, targetTick))
                        {
                            v.GoOnRails();
                        }
                    }
                }
            }
            Planetarium.SetUniversalTime(targetTick);
        }

        private bool SafeToStepClock(Vessel checkVessel, double targetTick)
        {
            switch (checkVessel.situation)
            {
                case Vessel.Situations.LANDED:
                case Vessel.Situations.PRELAUNCH:
                case Vessel.Situations.SPLASHED:
                    return (checkVessel.srf_velocity.magnitude < 2);
                case Vessel.Situations.ORBITING:
                case Vessel.Situations.ESCAPING:
                    return true;
                case Vessel.Situations.SUB_ORBITAL:
                    double altitudeAtUT = checkVessel.orbit.getRelativePositionAtUT(targetTick).magnitude;
                    return (altitudeAtUT > checkVessel.mainBody.Radius + 10000 && checkVessel.altitude > 10000);
                default :
                    return false;
            }
        }

        private void SkewClock(double currentError)
        {
            //Same code from KMP.
            float timeWarpRate = (float)Math.Pow(2, -currentError);
            if (timeWarpRate > 1.5f)
                timeWarpRate = 1.5f;
            if (timeWarpRate < 0.5f)
                timeWarpRate = 0.5f;
            Time.timeScale = timeWarpRate;
            skewList.Add(timeWarpRate);
            while (skewList.Count > 300)
            {
                skewList.RemoveAt(0);
            }
            //Set the average
            float totalSkew = 0f;
            int totalCount = 0;
            foreach (float currentSkew in skewList)
            {
                totalSkew += currentSkew;
                totalCount++;
            }
            averageSkewRate = totalSkew / (float)totalCount;
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

        public int LockNewSubspace(long serverTime, double planetariumTime, float subspaceSpeed)
        {
            int highestSubpaceID = 0;
            foreach (int subspaceID in subspaces.Keys)
            {
                if (subspaceID > highestSubpaceID)
                {
                    highestSubpaceID = subspaceID;
                }
            }
            LockNewSubspace((highestSubpaceID + 1), serverTime, planetariumTime, subspaceSpeed);
            return (highestSubpaceID + 1);
        }

        public void LockNewSubspace(int subspaceID, long serverTime, double planetariumTime, float subspaceSpeed)
        {
            if (!subspaces.ContainsKey(subspaceID))
            {
                Subspace newSubspace = new Subspace();
                newSubspace.serverClock = serverTime;
                newSubspace.planetTime = planetariumTime;
                newSubspace.subspaceSpeed = subspaceSpeed;
                subspaces.Add(subspaceID, newSubspace);
            }
            DarkLog.Debug("Subspace " + subspaceID + " locked to server, time: " + planetariumTime);
        }

        public void LockSubspace(int subspaceID)
        {
            if (subspaces.ContainsKey(subspaceID))
            {
                TimeWarp.SetRate(0, true);
                locked = true;
                DarkLog.Debug("Locked to subspace " + subspaceID + ", time: " + GetUniverseTime());
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)WarpMessageType.CHANGE_SUBSPACE);
                    mw.Write<string>(Settings.fetch.playerName);
                    mw.Write<int>(subspaceID);
                    NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                }
            }
            currentSubspace = subspaceID;
        }

        public void UnlockSubspace()
        {
            currentSubspace = -1;
            locked = false;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)WarpMessageType.CHANGE_SUBSPACE);
                mw.Write<string>(Settings.fetch.playerName);
                mw.Write<int>(currentSubspace);
                NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
            }
        }

        public void RelockSubspace(int subspaceID, long serverClock, double planetTime, float subspaceSpeed)
        {
            if (subspaces.ContainsKey(subspaceID))
            {
                subspaces[subspaceID].serverClock = serverClock;
                subspaces[subspaceID].planetTime = planetTime;
                subspaces[subspaceID].subspaceSpeed = subspaceSpeed;
            }
            else
            {
                DarkLog.Debug("Failed to relock non-existant subspace!");
            }
        }

        public long GetServerClock()
        {
            if (synced)
            {
                return DateTime.UtcNow.Ticks + clockOffsetAverage;
            }
            return 0;
        }

        public double GetUniverseTime()
        {
            if (synced && locked && (currentSubspace != -1))
            {
                return GetUniverseTime(currentSubspace);
            }
            return 0;
        }

        public double GetUniverseTime(int subspace)
        {
            if (subspaces.ContainsKey(subspace))
            {
                long realTimeSinceLock = GetServerClock() - subspaces[subspace].serverClock;
                double realTimeSinceLockSeconds = realTimeSinceLock / 10000000d;
                double adjustedTimeSinceLockSeconds = realTimeSinceLockSeconds * subspaces[subspace].subspaceSpeed;
                return subspaces[subspace].planetTime + adjustedTimeSinceLockSeconds;
            }
            else
            {
                return 0;
            }
        }

        public double GetCurrentError()
        {
            if (synced && locked)
            {   
                double currentTime = Planetarium.GetUniversalTime();
                double targetTime = GetUniverseTime();
                return (currentTime - targetTime);
            }
            return 0;
        }

        public Subspace GetSubspace(int subspaceID)
        {
            Subspace ss = new Subspace();
            if (subspaces.ContainsKey(subspaceID))
            {
                ss.serverClock = subspaces[subspaceID].serverClock;
                ss.planetTime = subspaces[subspaceID].planetTime;
                ss.subspaceSpeed = subspaces[subspaceID].subspaceSpeed;
            }
            return ss;
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

        public void HandleSyncTime(long clientSend, long serverReceive, long serverSend)
        {
            long clientReceive = DateTime.UtcNow.Ticks;
            long clientLatency = (clientReceive - clientSend) - (serverSend - serverReceive);
            long clientOffset = ((serverReceive - clientSend) + (serverSend - clientReceive)) / 2;
            clockOffset.Add(clientOffset);
            networkLatency.Add(clientLatency);
            serverLag = serverSend - serverReceive;
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
                NetworkWorker.fetch.SendTimeSync();
            }
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    singleton.workerEnabled = false;
                    Client.fixedUpdateEvent.Remove(singleton.FixedUpdate);
                }
                singleton = new TimeSyncer();
                Client.fixedUpdateEvent.Add(singleton.FixedUpdate);
            }
        }
    }
}

