using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class AsteroidWorker
    {
        //singleton
        private static AsteroidWorker singleton;
        //How many asteroids to spawn into the server
        public int maxNumberOfUntrackedAsteroids;
        public bool workerEnabled;
        //private state variables
        private float lastAsteroidCheck;
        private const float ASTEROID_CHECK_INTERVAL = 5f;
        ScenarioDiscoverableObjects scenarioController;
        private List<string> serverAsteroids = new List<string>();
        private Dictionary<string,string> serverAsteroidTrackStatus = new Dictionary<string,string>();
        private object serverAsteroidListLock = new object();

        public static AsteroidWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        private void Update()
        {
            if (workerEnabled)
            {
                if (scenarioController == null)
                {
                    foreach (ProtoScenarioModule psm in HighLogic.CurrentGame.scenarios)
                    {
                        if (psm != null)
                        {
                            if (psm.moduleName == "ScenarioDiscoverableObjects")
                            {
                                if (psm.moduleRef != null)
                                {
                                    scenarioController = (ScenarioDiscoverableObjects)psm.moduleRef;
                                    scenarioController.spawnInterval = float.MaxValue;
                                }
                            }
                        }
                    }
                }
            
                if (scenarioController != null)
                {
                    if ((UnityEngine.Time.realtimeSinceStartup - lastAsteroidCheck) > ASTEROID_CHECK_INTERVAL)
                    {
                        lastAsteroidCheck = UnityEngine.Time.realtimeSinceStartup;
                        //Try to acquire the asteroid-spawning lock if nobody else has it.
                        if (!LockSystem.fetch.LockExists("asteroid-spawning"))
                        {
                            LockSystem.fetch.AcquireLock("asteroid-spawning", false);
                        }

                        //We have the spawn lock, lets do stuff.
                        if (LockSystem.fetch.LockIsOurs("asteroid-spawning"))
                        {
                            if ((HighLogic.CurrentGame.flightState.protoVessels != null) && (FlightGlobals.fetch.vessels != null))
                            {
                                if ((HighLogic.CurrentGame.flightState.protoVessels.Count == 0) || (FlightGlobals.fetch.vessels.Count > 0))
                                {
                                    int beforeSpawn = GetAsteroidCount();
                                    int asteroidsToSpawn = maxNumberOfUntrackedAsteroids - beforeSpawn;
                                    for (int asteroidsSpawned = 0; asteroidsSpawned < asteroidsToSpawn; asteroidsSpawned++)
                                    {
                                        DarkLog.Debug("Spawning asteroid, have " + (beforeSpawn + asteroidsSpawned) + ", need " + maxNumberOfUntrackedAsteroids);
                                        scenarioController.SpawnAsteroid();
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
                                        NetworkWorker.fetch.SendVesselProtoMessage(pv, false, false);
                                    }
                                }
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
                    lock (serverAsteroidListLock)
                    {
                        if (LockSystem.fetch.LockIsOurs("asteroid-spawning"))
                        {
                            if (!serverAsteroids.Contains(checkVessel.id.ToString()))
                            {
                                if (GetAsteroidCount() <= maxNumberOfUntrackedAsteroids)
                                {
                                    DarkLog.Debug("Spawned in new server asteroid!");
                                    serverAsteroids.Add(checkVessel.id.ToString());
                                    VesselWorker.fetch.RegisterServerVessel(checkVessel.id);
                                    NetworkWorker.fetch.SendVesselProtoMessage(checkVessel.protoVessel, false, false);
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
            if (checkVessel != null)
            {
                if (checkVessel.protoPartSnapshots != null ? (checkVessel.protoPartSnapshots.Count == 1) : false)
                {
                    if (checkVessel.protoPartSnapshots[0].partName == "PotatoRoid")
                    {
                        return true;
                    }
                }
            }
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
            List<string> seenAsteroids = new List<string>();
            foreach (Vessel checkAsteroid in GetCurrentAsteroids())
            {
                if (!seenAsteroids.Contains(checkAsteroid.id.ToString()))
                {
                    seenAsteroids.Add(checkAsteroid.id.ToString());
                }
            }
            foreach (ProtoVessel checkAsteroid in HighLogic.CurrentGame.flightState.protoVessels)
            {
                if (VesselIsAsteroid(checkAsteroid))
                {
                    if (!seenAsteroids.Contains(checkAsteroid.vesselID.ToString()))
                    {
                        seenAsteroids.Add(checkAsteroid.vesselID.ToString());
                    }
                }
            }
            return seenAsteroids.Count;
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

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            //Force the worker to find the scenario module again.
            scenarioController = null;
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    singleton.workerEnabled = false;
                    Client.updateEvent.Remove(singleton.Update);
                    GameEvents.onGameSceneLoadRequested.Remove(singleton.OnGameSceneLoadRequested);
                    GameEvents.onVesselCreate.Remove(singleton.OnVesselCreate);
                }
                singleton = new AsteroidWorker();
                Client.updateEvent.Add(singleton.Update);
                GameEvents.onGameSceneLoadRequested.Add(singleton.OnGameSceneLoadRequested);
                GameEvents.onVesselCreate.Add(singleton.OnVesselCreate);
            }
        }
    }
}

