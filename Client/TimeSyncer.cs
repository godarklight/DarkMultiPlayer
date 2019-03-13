using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class TimeSyncer
    {
        public bool disabled
        {
            get;
            set;
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

        public bool locked
        {
            get
            {
                return lockedSubspace != null;
            }
        }

        public Subspace lockedSubspace
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

        public float requestedRate
        {
            get;
            private set;
        }

        private const float MAX_CLOCK_SKEW = 5f;
        private const float MAX_SUBSPACE_RATE = 1f;
        private const float MIN_SUBSPACE_RATE = 0.3f;
        private const float MIN_CLOCK_RATE = 0.3f;
        private const float MAX_CLOCK_RATE = 1.5f;
        private const float SYNC_TIME_INTERVAL = 30f;
        private const float CLOCK_SET_INTERVAL = .1f;
        private const int SYNC_TIME_VALID = 4;
        private float lastSyncTime = 0f;
        private float lastClockSkew = 0f;
        private bool clockOffsetFull = false;
        private int clockOffsetPos = 0;
        private long[] clockOffset = new long[10];
        private bool networkLatencyFull = false;
        private int networkLatencyPos = 0;
        private long[] networkLatency = new long[10];
        private bool requestedRatesListFull = false;
        private int requestedRatesListPos = 0;
        private float[] requestedRatesList = new float[50];
        private Dictionary<int, Subspace> subspaces = new Dictionary<int, Subspace>();
        //Services
        private DMPGame dmpGame;
        private NetworkWorker networkWorker;
        private VesselWorker vesselWorker;
        public bool isSubspace;

        public TimeSyncer(DMPGame dmpGame, NetworkWorker networkWorker, VesselWorker vesselWorker)
        {
            this.dmpGame = dmpGame;
            this.networkWorker = networkWorker;
            this.vesselWorker = vesselWorker;
            dmpGame.fixedUpdateEvent.Add(FixedUpdate);
            currentSubspace = -1;
            requestedRate = 1f;
        }

        public void FixedUpdate()
        {
            if (!workerEnabled)
            {
                return;
            }

            if (!synced)
            {
                return;
            }

            if ((Client.realtimeSinceStartup - lastSyncTime) > SYNC_TIME_INTERVAL)
            {
                lastSyncTime = Client.realtimeSinceStartup;
                networkWorker.SendTimeSync();
            }

            //Mod API to disable the time syncer
            if (disabled)
            {
                return;
            }

            if (locked)
            {
                if (isSubspace)
                {
                    vesselWorker.DetectReverting();
                }
                //Set the universe time here
                SyncTime();
            }
        }

        //Skew or set the clock
        private void SyncTime()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                requestedRatesListPos = 0;
                requestedRatesListFull = false;
                requestedRate = 1f;
            }

            if (Time.timeSinceLevelLoad < 1f)
            {
                return;
            }

            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                if (!FlightGlobals.ready)
                {
                    return;
                }
                if (FlightGlobals.fetch.activeVessel == null)
                {
                    return;
                }
            }

            if ((Client.realtimeSinceStartup - lastClockSkew) > CLOCK_SET_INTERVAL)
            {
                lastClockSkew = Client.realtimeSinceStartup;
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
                else
                {
                    Time.timeScale = 1f;
                }
            }
        }

        private void StepClock(double targetTick)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                DarkLog.Debug("Skipping StepClock in loading screen");
                return;
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (FlightGlobals.fetch.activeVessel == null || !FlightGlobals.ready)
                {
                    DarkLog.Debug("Skipping StepClock (active vessel is null or not ready)");
                    return;
                }
                try
                {
                    OrbitPhysicsManager.HoldVesselUnpack(5);
                }
                catch
                {
                    DarkLog.Debug("Failed to hold vessel unpack");
                    return;
                }
                foreach (Vessel v in FlightGlobals.fetch.vessels)
                {
                    if (!v.packed)
                    {
                        if (v != FlightGlobals.fetch.activeVessel)
                        {
                            try
                            {
                                v.GoOnRails();
                            }
                            catch
                            {
                                DarkLog.Debug("Error packing vessel " + v.id.ToString());
                            }
                        }
                        if (v == FlightGlobals.fetch.activeVessel)
                        {
                            if (SafeToStepClock(v, targetTick))
                            {
                                try
                                {
                                    v.GoOnRails();
                                }
                                catch
                                {
                                    DarkLog.Debug("Error packing active vessel " + v.id.ToString());
                                }
                            }
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
                default:
                    return false;
            }
        }

        private void SkewClock(double currentError)
        {
            float timeWarpRate = (float)Math.Pow(10, -currentError);
            if (currentError > 0)
            {
                timeWarpRate = timeWarpRate * lockedSubspace.subspaceSpeed;
            }
            if (timeWarpRate > MAX_CLOCK_RATE)
            {
                timeWarpRate = MAX_CLOCK_RATE;
            }
            if (timeWarpRate < MIN_CLOCK_RATE)
            {
                timeWarpRate = MIN_CLOCK_RATE;
            }
            //Request how fast we *think* we can run (The reciporical of the current warp rate)
            float tempRequestedRate = lockedSubspace.subspaceSpeed * (1 / timeWarpRate);
            if (tempRequestedRate > MAX_SUBSPACE_RATE)
            {
                tempRequestedRate = MAX_SUBSPACE_RATE;
            }
            requestedRatesList[requestedRatesListPos] = tempRequestedRate;
            requestedRatesListPos++;
            if (requestedRatesListPos >= requestedRatesList.Length)
            {
                requestedRatesListFull = true;
                requestedRatesListPos = 0;
            }
            //Set the average requested rate
            float requestedRateTotal = 0f;
            int requestedEndPos = requestedRatesListPos;
            if (requestedRatesListFull)
            {
                requestedEndPos = requestedRatesList.Length;
            }
            for (int i = 0; i < requestedEndPos; i++)
            {
                requestedRateTotal += requestedRatesList[i];
            }
            requestedRate = requestedRateTotal / requestedEndPos;

            //Set the physwarp rate
            Time.timeScale = timeWarpRate;
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

        public void AddNewSubspace(int subspaceID, long serverTime, double planetariumTime, float subspaceSpeed)
        {
            Subspace newSubspace = new Subspace();
            newSubspace.serverClock = serverTime;
            newSubspace.planetTime = planetariumTime;
            newSubspace.subspaceSpeed = subspaceSpeed;
            subspaces[subspaceID] = newSubspace;
            if (currentSubspace == subspaceID)
            {
                LockSubspace(currentSubspace);
            }
            DarkLog.Debug("Subspace " + subspaceID + " locked to server, time: " + planetariumTime);
        }

        public void LockTemporarySubspace(long serverClock, double planetTime, float subspaceSpeed)
        {
            Subspace tempSubspace = new Subspace();
            tempSubspace.serverClock = serverClock;
            tempSubspace.planetTime = planetTime;
            tempSubspace.subspaceSpeed = subspaceSpeed;
            lockedSubspace = tempSubspace;
        }

        public void LockSubspace(int subspaceID)
        {
            if (subspaces.ContainsKey(subspaceID))
            {
                TimeWarp.SetRate(0, true);
                lockedSubspace = subspaces[subspaceID];
                DarkLog.Debug("Locked to subspace " + subspaceID + ", time: " + GetUniverseTime());
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)WarpMessageType.CHANGE_SUBSPACE);
                    mw.Write<int>(subspaceID);
                    networkWorker.SendWarpMessage(mw.GetMessageBytes());
                }
            }
            currentSubspace = subspaceID;
        }

        public void UnlockSubspace()
        {
            currentSubspace = -1;
            lockedSubspace = null;
            Time.timeScale = 1f;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)WarpMessageType.CHANGE_SUBSPACE);
                mw.Write<int>(currentSubspace);
                networkWorker.SendWarpMessage(mw.GetMessageBytes());
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
                DarkLog.Debug("Failed to relock non-existant subspace " + subspaceID);
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
            if (synced && locked)
            {
                return GetUniverseTime(lockedSubspace);
            }
            return 0;
        }

        public double GetUniverseTime(int subspace)
        {
            if (subspaces.ContainsKey(subspace))
            {
                return GetUniverseTime(subspaces[subspace]);
            }
            else
            {
                return 0;
            }
        }

        public double GetUniverseTime(Subspace subspace)
        {
            long realTimeSinceLock = GetServerClock() - subspace.serverClock;
            double realTimeSinceLockSeconds = realTimeSinceLock / 10000000d;
            double adjustedTimeSinceLockSeconds = realTimeSinceLockSeconds * subspace.subspaceSpeed;
            return subspace.planetTime + adjustedTimeSinceLockSeconds;
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

        public bool SubspaceExists(int subspaceID)
        {
            return subspaces.ContainsKey(subspaceID);
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

        public int GetMostAdvancedSubspace()
        {
            double highestTime = double.NegativeInfinity;
            int retVal = -1;
            foreach (int subspaceID in subspaces.Keys)
            {
                double testTime = GetUniverseTime(subspaceID);
                if (testTime > highestTime)
                {
                    highestTime = testTime;
                    retVal = subspaceID;
                }
            }
            return retVal;
        }

        private bool CanSyncTime()
        {
            if (!locked)
            {
                return false;
            }
            bool canSync;
            switch (HighLogic.LoadedScene)
            {
                case GameScenes.TRACKSTATION:
                case GameScenes.FLIGHT:
                case GameScenes.SPACECENTER:
                    canSync = true;
                    break;
                default:
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
            clockOffset[clockOffsetPos] = clientOffset;
            clockOffsetPos++;
            networkLatency[networkLatencyPos] = clientLatency;
            networkLatencyPos++;
            serverLag = serverSend - serverReceive;
            if (clockOffsetPos >= clockOffset.Length)
            {
                clockOffsetPos = 0;
                clockOffsetFull = true;
            }
            if (networkLatencyPos >= networkLatency.Length)
            {
                networkLatencyPos = 0;
                networkLatencyFull = true;
            }
            long clockOffsetTotal = 0;
            int clockOffsetEndPos = clockOffsetPos;
            if (clockOffsetFull)
            {
                clockOffsetEndPos = clockOffset.Length;
            }
            for (int i = 0; i < clockOffsetEndPos; i++)
            {
                clockOffsetTotal += clockOffset[i];
            }
            clockOffsetAverage = clockOffsetTotal / clockOffsetEndPos;


            long networkLatencyTotal = 0;
            int networkLatencyEndPos = clockOffsetPos;
            if (networkLatencyFull)
            {
                networkLatencyEndPos = networkLatency.Length;
            }
            for (int i = 0; i < networkLatencyEndPos; i++)
            {
                networkLatencyTotal += networkLatency[i];
            }
            networkLatencyAverage = networkLatencyTotal / networkLatencyEndPos;

            //Check if we are now synced
            if ((clockOffsetFull || clockOffsetPos > SYNC_TIME_VALID) && !synced)
            {
                synced = true;
                float clockOffsetAverageMs = clockOffsetAverage / 10000f;
                float networkLatencyMs = networkLatencyAverage / 10000f;
                DarkLog.Debug("Initial clock syncronized, offset " + clockOffsetAverageMs + "ms, latency " + networkLatencyMs + "ms");
            }

            //Ask for another time sync if we aren't synced yet.
            if (!synced)
            {
                lastSyncTime = Client.realtimeSinceStartup;
                networkWorker.SendTimeSync();
            }
        }

        public void Stop()
        {
            workerEnabled = false;
            dmpGame.fixedUpdateEvent.Remove(FixedUpdate);
            Time.timeScale = 1f;
        }
    }
}

