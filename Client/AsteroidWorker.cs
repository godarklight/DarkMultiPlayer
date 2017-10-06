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
        private const float ASTEROID_CHECK_INTERVAL = 60f; // 1 minute
        private ScenarioDiscoverableObjects scenario;
        private bool initialized;
        private List<string> serverAsteroids = new List<string>();
        private Dictionary<string, string> serverAsteroidTrackStatus = new Dictionary<string, string>();
        private object serverAsteroidListLock = new object();
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
            foreach (ProtoScenarioModule psm in HighLogic.CurrentGame.scenarios)
            {
                if (psm != null && scenario == null && psm.moduleName.Contains("Discoverable"))
                {
                    scenario = (ScenarioDiscoverableObjects)psm.moduleRef;  // this is borked as of 1.3.0; maybe they'll fix it in the future?
                }
            }
            if (scenario != null) scenario.spawnInterval = float.MaxValue;

            // Disable the new Sentinel mechanic in KSP 1.3.0
            SentinelUtilities.SpawnChance = 0f;

            initialized = true;
        }

        public IEnumerable<Vessel> SpawnAsteroids(int quantity = 1)
        {
            System.Random random = new System.Random();
            while (quantity-- > 0)
            {
                yield return SpawnAsteroid(random).vesselRef;
            }
            yield break;
        }

        public ProtoVessel SpawnAsteroid(System.Random random)
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
            if (!workerEnabled) return;
            if (Client.realtimeSinceStartup - lastAsteroidCheck < ASTEROID_CHECK_INTERVAL) return;
                else lastAsteroidCheck = Client.realtimeSinceStartup;
            if (!initialized) InitializeScenario();
            
            //Try to acquire the asteroid-spawning lock if nobody else has it.
            if (!lockSystem.LockExists("asteroid-spawning"))
            {
                lockSystem.AcquireLock("asteroid-spawning", false);
            }

            //We have the spawn lock, lets do stuff.
            if (lockSystem.LockIsOurs("asteroid-spawning"))
            {
                if ((HighLogic.CurrentGame.flightState.protoVessels != null) && (FlightGlobals.fetch.vessels != null))
                {
                    if ((HighLogic.CurrentGame.flightState.protoVessels.Count == 0) || (FlightGlobals.fetch.vessels.Count > 0))
                    {
                        int beforeSpawn = GetAsteroidCount();
                        int asteroidsToSpawn = maxNumberOfUntrackedAsteroids - beforeSpawn;
                        if (asteroidsToSpawn > 0)
                        {
                            foreach (Vessel asty in SpawnAsteroids(1)) // spawn 1 every ASTEROID_CHECK_INTERVAL seconds
                                DarkLog.Debug("Spawned asteroid " + asty.name + ", have " + (beforeSpawn) + ", need " + maxNumberOfUntrackedAsteroids);
                        }
                    }
                }
            }

            //Check for changes to tracking
            foreach (Vessel asteroid in GetCurrentAsteroids())
            {
                if (asteroid.state != Vessel.State.DEAD)
                {
                    if (!serverAsteroidTrackStatus.ContainsKey(asteroid.id.ToString()))
                    {
                        serverAsteroidTrackStatus.Add(asteroid.id.ToString(), asteroid.DiscoveryInfo.trackingStatus.Value);
                    }
                    else
                    {
                        if (asteroid.DiscoveryInfo.trackingStatus.Value != serverAsteroidTrackStatus[asteroid.id.ToString()])
                        {
                            ProtoVessel pv = asteroid.BackupVessel();
                            DarkLog.Debug("Sending changed asteroid, new state: " + asteroid.DiscoveryInfo.trackingStatus.Value + "!");
                            serverAsteroidTrackStatus[asteroid.id.ToString()] = asteroid.DiscoveryInfo.trackingStatus.Value;
                            networkWorker.SendVesselProtoMessage(pv, false, false);
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
                    lock (serverAsteroidListLock)
                    {
                        if (lockSystem.LockIsOurs("asteroid-spawning"))
                        {
                            if (!serverAsteroids.Contains(checkVessel.id.ToString()))
                            {
                                if (GetAsteroidCount() <= maxNumberOfUntrackedAsteroids)
                                {
                                    DarkLog.Debug("Spawned in new server asteroid!");
                                    serverAsteroids.Add(checkVessel.id.ToString());
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
                            if (!serverAsteroids.Contains(checkVessel.id.ToString()))
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
            if (checkVessel != null)
            {
                if (!checkVessel.loaded)
                {
                    return VesselIsAsteroid(checkVessel.protoVessel);
                }
                //Check the vessel has exactly one part.
                if (checkVessel.parts != null ? (checkVessel.parts.Count == 1) : false)
                {
                    if (checkVessel.parts[0].partName == "PotatoRoid")
                    {
                        return true;
                    }
                }
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
            // Short circuit evaluation = faster
            if (
                checkVessel != null
                && checkVessel.protoPartSnapshots != null
                && checkVessel.protoPartSnapshots.Count == 1
                && checkVessel.protoPartSnapshots[0].partName == "PotatoRoid")
                return true;
            else
                return false;
        }

        private Vessel[] GetCurrentAsteroids()
        {
            List<Vessel> currentAsteroids = new List<Vessel>();
            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                if (VesselIsAsteroid(checkVessel))
                {
                    currentAsteroids.Add(checkVessel);
                }
            }
            return currentAsteroids.ToArray();
        }

        private int GetAsteroidCount()
        {
            return GetCurrentAsteroids().Length;
        }

        /// <summary>
        /// Registers the server asteroid - Prevents DMP from deleting it.
        /// </summary>
        /// <param name="asteroidID">Asteroid to register</param>
        public void RegisterServerAsteroid(string asteroidID)
        {
            lock (serverAsteroidListLock)
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

