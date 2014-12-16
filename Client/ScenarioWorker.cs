using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;
using System.Reflection;

namespace DarkMultiPlayer
{
    public class ScenarioWorker
    {
        public bool workerEnabled = false;
        private static ScenarioWorker singleton;
        private Dictionary<string,string> checkData = new Dictionary<string, string>();
        private Queue<ScenarioEntry> scenarioQueue = new Queue<ScenarioEntry>();
        private bool blockScenarioDataSends = false;
        private float lastScenarioSendTime = 0f;
        private const float SEND_SCENARIO_DATA_INTERVAL = 30f;
        //ScenarioType list to check.
        private List<KSPScenarioType> allScenarioTypesInAssemblies;
        //System.Reflection hackiness for loading kerbals into the crew roster:
        private delegate bool AddCrewMemberToRosterDelegate(ProtoCrewMember pcm);

        private AddCrewMemberToRosterDelegate AddCrewMemberToRoster;

        public static ScenarioWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        private void Update()
        {
            if (workerEnabled && !blockScenarioDataSends)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastScenarioSendTime) > SEND_SCENARIO_DATA_INTERVAL)
                {
                    lastScenarioSendTime = UnityEngine.Time.realtimeSinceStartup;
                    SendScenarioModules();
                }
            }
        }

        private bool IsScenarioModuleAllowed(string scenarioName)
        {
            //Blacklist asteroid module from every game mode
            if (scenarioName == "ScenarioDiscoverableObjects")
            {
                return false;
            }
            if (allScenarioTypesInAssemblies == null)
            {
                allScenarioTypesInAssemblies = KSPScenarioType.GetAllScenarioTypesInAssemblies();
            }
            foreach (KSPScenarioType scenarioType in allScenarioTypesInAssemblies)
            {
                if (scenarioName != scenarioType.ModuleType.Name)
                {
                    continue;
                }
                bool protoAllowed = false;
                if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                {
                    protoAllowed = protoAllowed || scenarioType.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToExistingCareerGames);
                    protoAllowed = protoAllowed || scenarioType.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewCareerGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX)
                {
                    protoAllowed = protoAllowed || scenarioType.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToExistingScienceSandboxGames);
                    protoAllowed = protoAllowed || scenarioType.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewScienceSandboxGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
                {
                    protoAllowed = protoAllowed || scenarioType.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToExistingSandboxGames);
                    protoAllowed = protoAllowed || scenarioType.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewSandboxGames);
                }
                return protoAllowed;
            }
            return false;
        }

        private void SendScenarioModules()
        {
            List<string> scenarioName = new List<string>();
            List<byte[]> scenarioData = new List<byte[]>();

            foreach (ScenarioModule sm in ScenarioRunner.GetLoadedModules())
            {
                string scenarioType = sm.GetType().Name;
                if (!IsScenarioModuleAllowed(scenarioType))
                {
                    continue;
                }
                ConfigNode scenarioNode = new ConfigNode();
                sm.Save(scenarioNode);
                byte[] scenarioBytes = ConfigNodeSerializer.fetch.Serialize(scenarioNode);
                string scenarioHash = Common.CalculateSHA256Hash(scenarioBytes);
                if (scenarioBytes.Length == 0)
                {
                    DarkLog.Debug("Error writing scenario data for " + scenarioType);
                    continue;
                }
                if (checkData.ContainsKey(scenarioType) ? (checkData[scenarioType] == scenarioHash) : false)
                {
                    //Data is the same since last time - Skip it.
                    continue;
                }
                else
                {
                    checkData[scenarioType] = scenarioHash;
                }
                if (scenarioBytes != null)
                {
                    scenarioName.Add(scenarioType);
                    scenarioData.Add(scenarioBytes);
                }
            }

            if (scenarioName.Count > 0)
            {
                NetworkWorker.fetch.SendScenarioModuleData(scenarioName.ToArray(), scenarioData.ToArray());
            }
        }

        public void LoadScenarioDataIntoGame()
        {
            while (scenarioQueue.Count > 0)
            {
                ScenarioEntry scenarioEntry = scenarioQueue.Dequeue();
                if (scenarioEntry.scenarioName == "ContractSystem")
                {
                    SpawnStrandedKerbalsForRescueMissions(scenarioEntry.scenarioNode);
                }
                if (scenarioEntry.scenarioName == "ProgressTracking")
                {
                    CreateMissingKerbalsInProgressTrackingSoTheGameDoesntBugOut(scenarioEntry.scenarioNode);
                }
                CheckForBlankSceneSoTheGameDoesntBugOut(scenarioEntry);
                ProtoScenarioModule psm = new ProtoScenarioModule(scenarioEntry.scenarioNode);
                if (psm != null)
                {
                    if (IsScenarioModuleAllowed(psm.moduleName))
                    {
                        DarkLog.Debug("Loading " + psm.moduleName + " scenario data");
                        HighLogic.CurrentGame.scenarios.Add(psm);
                    }
                    else
                    {
                        DarkLog.Debug("Skipping " + psm.moduleName + " scenario data in " + Client.fetch.gameMode + " mode");
                    }
                }
            }
        }
        //Defends against bug #172
        private void SpawnStrandedKerbalsForRescueMissions(ConfigNode contractSystemNode)
        {
            ConfigNode contractsNode = contractSystemNode.GetNode("CONTRACTS");
            foreach (ConfigNode contractNode in contractsNode.GetNodes("CONTRACT"))
            {
                if ((contractNode.GetValue("type") == "RescueKerbal") && (contractNode.GetValue("state") == "Offered"))
                {
                    string kerbalName = contractNode.GetValue("kerbalName");
                    if (!HighLogic.CurrentGame.CrewRoster.Exists(kerbalName))
                    {
                        DarkLog.Debug("Spawning missing kerbal (" + kerbalName + ") for offered KerbalRescue contract");
                        ProtoCrewMember pcm = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Unowned);
                        pcm.name = kerbalName;
                    }
                }
                if ((contractNode.GetValue("type") == "RescueKerbal") && (contractNode.GetValue("state") == "Active"))
                {

                    string kerbalName = contractNode.GetValue("kerbalName");
                    DarkLog.Debug("Spawning stranded kerbal (" + kerbalName + ") for active KerbalRescue contract");
                    int bodyID = Int32.Parse(contractNode.GetValue("body"));
                    if (!HighLogic.CurrentGame.CrewRoster.Exists(kerbalName))
                    {
                        GenerateStrandedKerbal(bodyID, kerbalName);
                    }
                }
            }
        }

        private void GenerateStrandedKerbal(int bodyID, string kerbalName)
        {
            //Add kerbal to crew roster.
            DarkLog.Debug("Spawning missing kerbal, name: " + kerbalName);
            ProtoCrewMember pcm = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Unowned);
            pcm.name = kerbalName;
            pcm.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
            //Create protovessel
            uint newPartID = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);
            CelestialBody contractBody = FlightGlobals.Bodies[bodyID];
            //Atmo: 10km above atmo, to half the planets radius out.
            //Non-atmo: 30km above ground, to half the planets radius out.
            double minAltitude = contractBody.atmosphere ? contractBody.Radius + contractBody.maxAtmosphereAltitude + 10000 : contractBody.Radius + 30000;
            double maxAltitude = minAltitude + contractBody.Radius * 0.5;
            Orbit strandedOrbit = Orbit.CreateRandomOrbitAround(FlightGlobals.Bodies[bodyID], minAltitude, maxAltitude);
            ConfigNode[] kerbalPartNode = new ConfigNode[1];
            ProtoCrewMember[] partCrew = new ProtoCrewMember[1];
            partCrew[0] = pcm;
            kerbalPartNode[0] = ProtoVessel.CreatePartNode("kerbalEVA", newPartID, partCrew);
            ConfigNode protoVesselNode = ProtoVessel.CreateVesselNode(kerbalName, VesselType.EVA, strandedOrbit, 0, kerbalPartNode);
            ConfigNode discoveryNode = ProtoVessel.CreateDiscoveryNode(DiscoveryLevels.Unowned, UntrackedObjectClass.A, double.PositiveInfinity, double.PositiveInfinity);
            ProtoVessel protoVessel = new ProtoVessel(protoVesselNode, HighLogic.CurrentGame);
            protoVessel.discoveryInfo = discoveryNode;
            //It's not supposed to be infinite, but you're crazy if you think I'm going to decipher the values field of the rescue node.
            HighLogic.CurrentGame.flightState.protoVessels.Add(protoVessel);
        }
        //Defends against bug #172
        private void CreateMissingKerbalsInProgressTrackingSoTheGameDoesntBugOut(ConfigNode progressTrackingNode)
        {
            foreach (ConfigNode possibleNode in progressTrackingNode.nodes)
            {
                //Recursion (noun): See Recursion.
                CreateMissingKerbalsInProgressTrackingSoTheGameDoesntBugOut(possibleNode);
            }
            //The kerbals are kept in a ConfigNode named 'crew', with 'crews' as a comma space delimited array of names.
            if (progressTrackingNode.name == "crew")
            {
                string kerbalNames = progressTrackingNode.GetValue("crews");
                if (!String.IsNullOrEmpty(kerbalNames))
                {
                    string[] kerbalNamesSplit = kerbalNames.Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string kerbalName in kerbalNamesSplit)
                    {
                        if (!HighLogic.CurrentGame.CrewRoster.Exists(kerbalName))
                        {
                            if (AddCrewMemberToRoster == null)
                            {
                                MethodInfo addMemberToCrewRosterMethod = typeof(KerbalRoster).GetMethod("AddCrewMember", BindingFlags.NonPublic | BindingFlags.Instance);
                                AddCrewMemberToRoster = (AddCrewMemberToRosterDelegate)Delegate.CreateDelegate(typeof(AddCrewMemberToRosterDelegate), HighLogic.CurrentGame.CrewRoster, addMemberToCrewRosterMethod);
                            }
                            if (AddCrewMemberToRoster == null)
                            {
                                throw new Exception("Failed to initialize AddCrewMemberToRoster for #172 ProgressTracking fix.");
                            }
                            DarkLog.Debug("Generating missing kerbal from ProgressTracking: " + kerbalName);
                            ProtoCrewMember pcm = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Crew);
                            pcm.name = kerbalName;
                            AddCrewMemberToRoster(pcm);
                            //Also send it off to the server
                            NetworkWorker.fetch.SendKerbalProtoMessage(pcm);
                        }
                    }
                }
            }
        }

        //If the scene field is blank, KSP will throw an error while starting the game, meaning players will be unable to join the server.
        private void CheckForBlankSceneSoTheGameDoesntBugOut(ScenarioEntry scenarioEntry)
        {
            if (scenarioEntry.scenarioNode.GetValue("scene") == string.Empty)
            {
                string nodeName = scenarioEntry.scenarioName;
                ScreenMessages.PostScreenMessage(nodeName + " is badly behaved!");
                DarkLog.Debug(nodeName + " is badly behaved!");
                scenarioEntry.scenarioNode.SetValue("scene", "7, 8, 5, 6, 9");
            }
        }

        public void LoadMissingScenarioDataIntoGame()
        {
            if (allScenarioTypesInAssemblies == null)
            {
                allScenarioTypesInAssemblies = KSPScenarioType.GetAllScenarioTypesInAssemblies();
            }
            foreach (KSPScenarioType scenarioType in allScenarioTypesInAssemblies)
            {
                if (HighLogic.CurrentGame.scenarios.Exists(psm => psm.moduleName == scenarioType.ModuleType.Name))
                {
                    continue;
                }
                bool loadModule = false;
                if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                {
                    loadModule = scenarioType.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewCareerGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX)
                {
                    loadModule = scenarioType.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewScienceSandboxGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
                {
                    loadModule = scenarioType.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewSandboxGames);
                }
                if (loadModule)
                {
                    DarkLog.Debug("Creating new scenario module " + scenarioType.ModuleType.Name);
                    HighLogic.CurrentGame.AddProtoScenarioModule(scenarioType.ModuleType, scenarioType.ScenarioAttributes.TargetScenes);
                }
            }
        }

        public void LoadScenarioData(ScenarioEntry entry)
        {
            if (!IsScenarioModuleAllowed(entry.scenarioName))
            {
                DarkLog.Debug("Skipped '" + entry.scenarioName + "' scenario data  in " + Client.fetch.gameMode + " mode");
                return;
            }

            //Load data from DMP
            if (entry.scenarioNode == null)
            {
                DarkLog.Debug(entry.scenarioName + " scenario data failed to create a ConfigNode!");
                blockScenarioDataSends = true;
                return;
            }

            //Load data into game
            bool loaded = false;
            foreach (ProtoScenarioModule psm in HighLogic.CurrentGame.scenarios)
            {
                if (psm.moduleName == entry.scenarioName)
                {
                    DarkLog.Debug("Loading existing " + entry.scenarioName + " scenario module");
                    try
                    {
                        if (psm.moduleRef == null)
                        {
                            DarkLog.Debug("Fixing null scenario module!");
                            psm.moduleRef = new ScenarioModule();
                        }
                        psm.moduleRef.Load(entry.scenarioNode);
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("Error loading " + entry.scenarioName + " scenario module, Exception: " + e);
                        blockScenarioDataSends = true;
                    }
                    loaded = true;
                }
            }
            if (!loaded)
            {
                DarkLog.Debug("Loading new " + entry.scenarioName + " scenario module");
                LoadNewScenarioData(entry.scenarioNode);
            }
        }

        public void LoadNewScenarioData(ConfigNode newScenarioData)
        {
            ProtoScenarioModule newModule = new ProtoScenarioModule(newScenarioData);
            try
            {
                HighLogic.CurrentGame.scenarios.Add(newModule);
                newModule.Load(ScenarioRunner.fetch);
            }
            catch
            {
                DarkLog.Debug("Error loading scenario data!");
                blockScenarioDataSends = true;
            }
        }

        public void QueueScenarioData(string scenarioName, ConfigNode scenarioData)
        {
            ScenarioEntry entry = new ScenarioEntry();
            entry.scenarioName = scenarioName;
            entry.scenarioNode = scenarioData;
            scenarioQueue.Enqueue(entry);
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    singleton.workerEnabled = false;
                    Client.updateEvent.Remove(singleton.Update);
                }
                singleton = new ScenarioWorker();
                Client.updateEvent.Add(singleton.Update);
            }
        }
    }

    public class ScenarioEntry
    {
        public string scenarioName;
        public ConfigNode scenarioNode;
    }
}

