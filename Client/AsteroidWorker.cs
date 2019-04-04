using System;
using System.Collections.Generic;
using UnityEngine;
using SentinelMission;

namespace DarkMultiPlayer
{
    public class AsteroidWorker
    {
        //How many asteroids to spawn into the server
        public int maxNumberOfUntrackedAsteroids;
        public bool workerEnabled;
        //private state variables
        private float lastAsteroidCheck;
        private float lastInitCheck;
        private const float ASTEROID_CHECK_INTERVAL = 60f; // 1 minute
        private ScenarioDiscoverableObjects scenario;
        private HashSet<Guid> serverAsteroids = new HashSet<Guid>();
        private Dictionary<Guid, string> serverAsteroidTrackStatus = new Dictionary<Guid, string>();
        System.Random random = new System.Random();
        //Services
        private DMPGame dmpGame;
        private LockSystem lockSystem;
        private NetworkWorker networkWorker;
        private VesselWorker vesselWorker;

        public AsteroidWorker(DMPGame dmpGame, LockSystem lockSystem, NetworkWorker networkWorker, VesselWorker vesselWorker)
        {
            this.dmpGame = dmpGame;
            this.lockSystem = lockSystem;
            this.networkWorker = networkWorker;
            this.vesselWorker = vesselWorker;
            this.dmpGame.updateEvent.Add(Update);
            GameEvents.onVesselCreate.Add(OnVesselCreate);
        }

        public void InitializeScenario()
        {
            if (scenario != null)
            {
                return;
            }
            if (ScenarioRunner.Instance == null)
            {
                return;
            }
            if (Client.realtimeSinceStartup > (lastInitCheck + 10))
            {
                lastInitCheck = Client.realtimeSinceStartup;
                foreach (ScenarioModule sm in ScenarioRunner.GetLoadedModules())
                {
                    if (scenario == null && sm is ScenarioDiscoverableObjects)
                    {
                        scenario = (ScenarioDiscoverableObjects)sm;
                    }
                }
                if (scenario != null)
                {
                    DarkLog.Debug("Scenario module found, we can spawn asteroids!");
                    scenario.spawnInterval = float.MaxValue;
                    scenario.spawnOddsAgainst = 1;
                    // Disable the new Sentinel mechanic in KSP 1.3.0
                    SentinelUtilities.SpawnChance = 0f;
                }
            }
        }

        public ProtoVessel SpawnAsteroid()
        {
            double baseDays = 21 + random.NextDouble() * 360;
            Orbit orbit = Orbit.CreateRandomOrbitFlyBy(Planetarium.fetch.Home, baseDays);

            ProtoVessel pv = DiscoverableObjectsUtil.SpawnAsteroid(
                DiscoverableObjectsUtil.GenerateAsteroidName(),
                orbit,
                (uint)SentinelUtilities.RandomRange(random),
                SentinelUtilities.WeightedAsteroidClass(random),
                double.PositiveInfinity, double.PositiveInfinity);
            return pv;
        }

        private void Update()
        {
            if (!workerEnabled)
            {
                return;
            }
            InitializeScenario();
            if (scenario == null)
            {
                return;
            }

            if (lastAsteroidCheck + ASTEROID_CHECK_INTERVAL > Client.realtimeSinceStartup)
            {
                return;
            }
            lastAsteroidCheck = Client.realtimeSinceStartup;

            //Try to acquire the asteroid-spawning lock if nobody else has it.
            if (!lockSystem.LockExists("asteroid-spawning"))
            {
                lockSystem.AcquireLock("asteroid-spawning", false);
            }

            //We have the spawn lock, lets do stuff.
            if (lockSystem.LockIsOurs("asteroid-spawning"))
            {
                if (HighLogic.LoadedSceneIsFlight && !FlightGlobals.ready)
                {
                    return;
                }
                if (HighLogic.CurrentGame.flightState.protoVessels == null)
                {
                    return;
                }
                if (FlightGlobals.fetch.vessels == null)
                {
                    return;
                }
                if (HighLogic.CurrentGame.flightState.protoVessels.Count == 0)
                {
                    return;
                }
                if (FlightGlobals.fetch.vessels.Count == 0)
                {
                    return;
                }
                int beforeSpawn = GetAsteroidCount();
                if (beforeSpawn <  maxNumberOfUntrackedAsteroids)
                {
                    ProtoVessel asty = SpawnAsteroid();
                    DarkLog.Debug("Spawned asteroid " + asty.vesselName + ", have " + (beforeSpawn) + ", need " + maxNumberOfUntrackedAsteroids);
                }
            }

            //Check for changes to tracking
            lock (serverAsteroids)
            {
                foreach (Vessel asteroid in GetCurrentAsteroids())
                {
                    if (asteroid.state != Vessel.State.DEAD)
                    {
                        if (!serverAsteroidTrackStatus.ContainsKey(asteroid.id))
                        {
                            serverAsteroidTrackStatus.Add(asteroid.id, asteroid.DiscoveryInfo.trackingStatus.Value);
                        }
                        else
                        {
                            if (asteroid.DiscoveryInfo.trackingStatus.Value != serverAsteroidTrackStatus[asteroid.id])
                            {
                                ProtoVessel pv = asteroid.BackupVessel();
                                DarkLog.Debug("Sending changed asteroid, new state: " + asteroid.DiscoveryInfo.trackingStatus.Value + "!");
                                serverAsteroidTrackStatus[asteroid.id] = asteroid.DiscoveryInfo.trackingStatus.Value;
                                networkWorker.SendVesselProtoMessage(pv, false, false);
                            }
                        }
                    }
                }
            }
        }

        private void OnVesselCreate(Vessel checkVessel)
        {
            if (workerEnabled)
            {
                if (VesselIsAsteroid(checkVessel))
                {
                    lock (serverAsteroids)
                    {
                        if (lockSystem.LockIsOurs("asteroid-spawning"))
                        {
                            if (!serverAsteroids.Contains(checkVessel.id))
                            {
                                if (GetAsteroidCount() <= maxNumberOfUntrackedAsteroids)
                                {
                                    DarkLog.Debug("Spawned in new server asteroid!");
                                    serverAsteroids.Add(checkVessel.id);
                                    vesselWorker.RegisterServerVessel(checkVessel.id);
                                    networkWorker.SendVesselProtoMessage(checkVessel.protoVessel, false, false);
                                }
                                else
                                {
                                    DarkLog.Debug("Killing non-server asteroid " + checkVessel.id);
                                    checkVessel.Die();
                                }
                            }
                        }
                        else
                        {
                            if (!serverAsteroids.Contains(checkVessel.id))
                            {
                                DarkLog.Debug("Killing non-server asteroid " + checkVessel.id + ", we don't own the asteroid-spawning lock");
                                checkVessel.Die();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the vessel is an asteroid.
        /// </summary>
        /// <returns><c>true</c> if the vessel is an asteroid, <c>false</c> otherwise.</returns>
        /// <param name="checkVessel">The vessel to check</param>
        public bool VesselIsAsteroid(Vessel checkVessel)
        {
            if (checkVessel == null)
            {
                return false;
            }
            if (!checkVessel.loaded)
            {
                return VesselIsAsteroid(checkVessel.protoVessel);
            }
            if (checkVessel.parts == null || checkVessel.parts.Count != 1)
            {
                return false;
            }
            if (checkVessel.parts[0].partName == "PotatoRoid")
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the vessel is an asteroid.
        /// </summary>
        /// <returns><c>true</c> if the vessel is an asteroid, <c>false</c> otherwise.</returns>
        /// <param name="checkVessel">The vessel to check</param>
        public bool VesselIsAsteroid(ProtoVessel checkVessel)
        {
            return checkVessel != null && checkVessel.protoPartSnapshots != null && checkVessel.protoPartSnapshots.Count == 1 && checkVessel.protoPartSnapshots[0].partName == "PotatoRoid";
        }

        private IEnumerable<Vessel> GetCurrentAsteroids()
        {
            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                if (VesselIsAsteroid(checkVessel))
                {
                    yield return checkVessel;
                }
            }
            yield break;
        }

        private int GetAsteroidCount()
        {
            int count = 0;
            foreach (Vessel v in GetCurrentAsteroids())
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// Registers the server asteroid - Prevents DMP from deleting it.
        /// </summary>
        /// <param name="asteroidID">Asteroid to register</param>
        public void RegisterServerAsteroid(Guid asteroidID)
        {
            lock (serverAsteroids)
            {
                if (!serverAsteroids.Contains(asteroidID))
                {
                    serverAsteroids.Add(asteroidID);
                }
                //This will ignore status changes so we don't resend the asteroid.
                if (serverAsteroidTrackStatus.ContainsKey(asteroidID))
                {
                    serverAsteroidTrackStatus.Remove(asteroidID);
                }
            }
        }

        public void Stop()
        {
            dmpGame.updateEvent.Remove(Update);
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
        }
    }
}

