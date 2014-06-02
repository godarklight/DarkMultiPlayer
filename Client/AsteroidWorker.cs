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

        public void LoadAsteroidScenario()
        {
            ConfigNode asteroidNode = GetAsteroidModuleNode();
            ProtoScenarioModule asteroidModule = new ProtoScenarioModule(asteroidNode);
            HighLogic.CurrentGame.scenarios.Add(asteroidModule);
            asteroidModule.Load(ScenarioRunner.fetch);
        }

        private void FixedUpdate()
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
                                    DarkLog.Debug("Found reference to asteroid spawner");
                                    scenarioController = (ScenarioDiscoverableObjects)psm.moduleRef;
                                    scenarioController.spawnInterval = float.MaxValue;
                                }
                            }
                        }
                    }
                }
            }
            if (workerEnabled && scenarioController != null)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastAsteroidCheck) > ASTEROID_CHECK_INTERVAL)
                {
                    List<Vessel> asteroidList = GetAsteroidList();
                    lastAsteroidCheck = UnityEngine.Time.realtimeSinceStartup;
                    //Try to acquire the asteroid-spawning lock if nobody else has it.
                    if (!LockSystem.fetch.LockExists("asteroid-spawning"))
                    {
                        LockSystem.fetch.AcquireLock("asteroid-spawning", false);
                    }
                    //We have the spawn lock, lets do stuff.
                    if (LockSystem.fetch.LockIsOurs("asteroid-spawning"))
                    {
                        if (FlightGlobals.fetch.vessels != null ? FlightGlobals.fetch.vessels.Count > 0 : false)
                        {
                            lock (serverAsteroidListLock)
                            {

                                if (asteroidList.Count < maxNumberOfUntrackedAsteroids)
                                {
                                    DarkLog.Debug("Spawning asteroid, have " + asteroidList.Count + ", need " + maxNumberOfUntrackedAsteroids);
                                    scenarioController.SpawnAsteroid();
                                }
                                foreach (Vessel asteroid in asteroidList)
                                {
                                    if (!serverAsteroids.Contains(asteroid.id.ToString()))
                                    {
                                        DarkLog.Debug("Spawned in new server asteroid!");
                                        serverAsteroids.Add(asteroid.id.ToString());
                                        NetworkWorker.fetch.SendVesselProtoMessage(asteroid.protoVessel, false);
                                    }
                                }
                            }
                        }
                    }
                    //Check for changes to tracking
                    foreach (Vessel asteroid in asteroidList)
                    {
                        if (!serverAsteroidTrackStatus.ContainsKey(asteroid.id.ToString()))
                        {
                            serverAsteroidTrackStatus.Add(asteroid.id.ToString(), asteroid.DiscoveryInfo.trackingStatus.Value);
                        }
                        else
                        {
                            if (asteroid.DiscoveryInfo.trackingStatus.Value != serverAsteroidTrackStatus[asteroid.id.ToString()])
                            {
                                DarkLog.Debug("Sending changed asteroid, new state: " + asteroid.DiscoveryInfo.trackingStatus.Value + "!");
                                serverAsteroidTrackStatus[asteroid.id.ToString()] = asteroid.DiscoveryInfo.trackingStatus.Value;
                                NetworkWorker.fetch.SendVesselProtoMessage(asteroid.protoVessel, false);
                            }
                        }
                    }
                }
            }
        }

        private List<Vessel> GetAsteroidList()
        {
            List<Vessel> returnList = new List<Vessel>();
            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                if (checkVessel != null)
                {
                    if (checkVessel.vesselType == VesselType.SpaceObject)
                    {
                        if (checkVessel.protoVessel != null)
                        {
                            if (checkVessel.protoVessel.protoPartSnapshots != null)
                            {
                                if (checkVessel.protoVessel.protoPartSnapshots.Count == 1)
                                {
                                    if (checkVessel.protoVessel.protoPartSnapshots[0].partName == "PotatoRoid")
                                    {
                                        returnList.Add(checkVessel);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return returnList;
        }

        private ConfigNode GetAsteroidModuleNode()
        {
            ConfigNode newNode = new ConfigNode();
            newNode.AddValue("name", "ScenarioDiscoverableObjects");
            newNode.AddValue("scene", "7, 8, 5");
            //This appears to be an int value of the seed, let's just make it random.
            int randomSeed = new System.Random().Next();
            newNode.AddValue("", randomSeed);
            ConfigNode sizeCurveNode = newNode.AddNode("sizeCurve");
            sizeCurveNode.AddValue("key", "0 0 1.5 1.5");
            sizeCurveNode.AddValue("key", "0.3 0.45 0.875 0.875");
            sizeCurveNode.AddValue("key", "0.7 0.55 0.875 0.875");
            sizeCurveNode.AddValue("key", "1 1 1.5 1.5");
            return newNode;
        }

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

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    Client.fixedUpdateEvent.Remove(singleton.FixedUpdate);
                }
                singleton = new AsteroidWorker();
                Client.fixedUpdateEvent.Add(singleton.FixedUpdate);
            }
        }
    }
}

