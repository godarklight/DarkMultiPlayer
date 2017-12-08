using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;
using System.Reflection;
using Contracts;

namespace DarkMultiPlayer
{
    public class ScenarioWorker
    {
        public bool workerEnabled = false;
        private Dictionary<string, string> checkData = new Dictionary<string, string>();
        private Queue<ScenarioEntry> scenarioQueue = new Queue<ScenarioEntry>();
        private bool blockScenarioDataSends = false;
        private float lastScenarioSendTime = 0f;
        private const float SEND_SCENARIO_DATA_INTERVAL = 30f;
        //Trigger rep loss
        private bool repLoss = false;
        //Does rep have a reason to lower
        private bool repLossHasReason = false;
        //Last time a rep event happened
        private float repEventTimer = 0f;
        //Rep trigger/reason cooldown timer
        private const float REP_LOSS_COOLDOWN = 2f;
        //The current rep taking in account for random rep loss
        private float currentRep = 0f;
        //Total rep lost
        private float repLost = 0f;
        //ScenarioType list to check.
        private Dictionary<string, Type> allScenarioTypesInAssemblies;
        //System.Reflection hackiness for loading kerbals into the crew roster:
        private delegate bool AddCrewMemberToRosterDelegate(ProtoCrewMember pcm);
        // Game hooks
        private bool registered;
        //Services
        private DMPGame dmpGame;
        private VesselWorker vesselWorker;
        private ConfigNodeSerializer configNodeSerializer;
        private NetworkWorker networkWorker;
        private LockSystem lockSystem;

        public ScenarioWorker(DMPGame dmpGame, VesselWorker vesselWorker, ConfigNodeSerializer configNodeSerializer, NetworkWorker networkWorker, LockSystem lockSystem)
        {
            this.dmpGame = dmpGame;
            this.vesselWorker = vesselWorker;
            this.configNodeSerializer = configNodeSerializer;
            this.networkWorker = networkWorker;
            this.lockSystem = lockSystem;
            dmpGame.updateEvent.Add(Update);
        }

        private void RegisterGameHooks()
        {
            registered = true;
            GameEvents.Contract.onAccepted.Add(OnContractAccepted);
            GameEvents.onCrewKilled.Add(OnCrewKilled);
            GameEvents.onVesselTerminated.Add(OnVesselTerminated);
            GameEvents.OnReputationChanged.Add(OnReputationChanged);
            currentRep = Reputation.Instance.reputation;
        }

        private void UnregisterGameHooks()
        {
            registered = false;
            GameEvents.Contract.onAccepted.Remove(OnContractAccepted);
            GameEvents.onCrewKilled.Remove(OnCrewKilled);
            GameEvents.onVesselTerminated.Remove(OnVesselTerminated);
            GameEvents.OnReputationChanged.Remove(OnReputationChanged);
        }

        private void OnContractAccepted(Contract contract)
        {
            DarkLog.Debug("Contract accepted, state: " + contract.ContractState);
            ConfigNode contractNode = new ConfigNode();
            contract.Save(contractNode);

            if (contractNode.GetValue("type") == "RecoverAsset")
            {
                string kerbalName = contractNode.GetValue("kerbalName").Trim();
                uint partID = uint.Parse(contractNode.GetValue("partID"));

                if (!string.IsNullOrEmpty(kerbalName))
                {
                    ProtoCrewMember rescueKerbal = null;
                    if (!HighLogic.CurrentGame.CrewRoster.Exists(kerbalName))
                    {
                        DarkLog.Debug("Generating missing kerbal " + kerbalName + " for rescue contract");
                        int kerbalGender = int.Parse(contractNode.GetValue("gender"));
                        rescueKerbal = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Unowned);
                        rescueKerbal.ChangeName(kerbalName);
                        rescueKerbal.gender = (ProtoCrewMember.Gender)kerbalGender;
                        rescueKerbal.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                    }
                    else
                    {
                        rescueKerbal = HighLogic.CurrentGame.CrewRoster[kerbalName];
                        DarkLog.Debug("Kerbal " + kerbalName + " already exists, skipping respawn");
                    }
                    if (rescueKerbal != null) vesselWorker.SendKerbalIfDifferent(rescueKerbal);
                }

                if (partID != 0)
                {
                    Vessel contractVessel = FinePrint.Utilities.VesselUtilities.FindVesselWithPartIDs(new List<uint> { partID });
                    if (contractVessel != null) vesselWorker.SendVesselUpdateIfNeeded(contractVessel);
                }
            }

            else if (contractNode.GetValue("type") == "TourismContract")
            {
                string tourists = contractNode.GetValue("tourists");
                if (tourists != null)
                {
                    string[] touristsNames = tourists.Split(new char[] { '|' });
                    foreach (string touristName in touristsNames)
                    {
                        ProtoCrewMember pcm = null;
                        if (!HighLogic.CurrentGame.CrewRoster.Exists(touristName))
                        {
                            DarkLog.Debug("Spawning missing tourist " + touristName + " for tourism contract");
                            pcm = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Tourist);
                            pcm.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                            pcm.ChangeName(touristName);
                        }
                        else
                        {
                            DarkLog.Debug("Skipped respawn of existing tourist " + touristName);
                            pcm = HighLogic.CurrentGame.CrewRoster[touristName];
                        }
                        if (pcm != null) vesselWorker.SendKerbalIfDifferent(pcm);
                    }
                }
            }
        }

        private void OnCrewKilled(EventReport report)
        {
            Part part = report.origin;

            if (part != null)
            {
                Vessel vessel = part.vessel;

                if (vessel != null)
                {
                    if ((lockSystem.LockExists("control-" + vessel.id) && !lockSystem.LockIsOurs("control-" + vessel.id)) || !lockSystem.LockExists("control-" + vessel.id))
                    {
                        if (!repLossHasReason)
                        {
                            repLossHasReason = true;
                            repEventTimer = Client.realtimeSinceStartup;
                        }
                    }
                }
                else
                {
                    if (!repLossHasReason)
                    {
                        repLossHasReason = true;
                        repEventTimer = Client.realtimeSinceStartup;
                    }
                }
            }
            else
            {
                if (!repLossHasReason)
                {
                    repLossHasReason = true;
                    repEventTimer = Client.realtimeSinceStartup;
                }
            }
        }

        private void OnVesselTerminated(ProtoVessel pv)
        {
            List<ProtoCrewMember> crew = pv.GetVesselCrew();

            if (crew.Count > 0)
            {
                if (lockSystem.LockExists("control-" + pv.vesselID) && !lockSystem.LockIsOurs("control-" + pv.vesselID))
                {
                    if (!repLossHasReason)
                    {
                        repLossHasReason = true;
                        repEventTimer = Client.realtimeSinceStartup;
                    }
                }
            }
        }

        private void OnReputationChanged(float totalValue, TransactionReasons reason)
        {
            if (reason == TransactionReasons.VesselLoss)
            {
                if (!repLoss)
                {
                    repLoss = true;
                    repEventTimer = Client.realtimeSinceStartup;
                }
                repLost += currentRep - totalValue;
            }
            currentRep = totalValue;
        }

        private void Update()
        {
            if (workerEnabled)
            {
                if (!registered) RegisterGameHooks();

                if (repLossHasReason)
                {
                    if (repLoss)
                    {
                        repLossHasReason = false;
                        repLoss = false;
                        
                        Reputation.Instance.AddReputation(repLost, TransactionReasons.None);

                        repLost = 0f;
                    }
                    else
                    {
                        if ((Client.realtimeSinceStartup - repEventTimer) > REP_LOSS_COOLDOWN)
                        {
                            repLossHasReason = false;

                            repLost = 0f;
                        }
                    }
                }
                else
                {
                    if (repLoss)
                    {
                        if ((Client.realtimeSinceStartup - repEventTimer) > REP_LOSS_COOLDOWN)
                        {
                            repLoss = false;

                            repLost = 0f;
                        }
                    }
                }
                
                if (!blockScenarioDataSends)
                {
                    if ((Client.realtimeSinceStartup - lastScenarioSendTime) > SEND_SCENARIO_DATA_INTERVAL)
                    {
                        lastScenarioSendTime = Client.realtimeSinceStartup;
                        SendScenarioModules(false);
                    }
                }
            }
            else
            {
                if (registered) UnregisterGameHooks();
            }
        }

        private void LoadScenarioTypes()
        {
            allScenarioTypesInAssemblies = new Dictionary<string, Type>();
            foreach (AssemblyLoader.LoadedAssembly something in AssemblyLoader.loadedAssemblies)
            {
                foreach (Type scenarioType in something.assembly.GetTypes())
                {
                    if (scenarioType.IsSubclassOf(typeof(ScenarioModule)))
                    {
                        if (!allScenarioTypesInAssemblies.ContainsKey(scenarioType.Name))
                        {
                            allScenarioTypesInAssemblies.Add(scenarioType.Name, scenarioType);
                        }
                    }
                }
            }
        }

        private bool IsScenarioModuleAllowed(string scenarioName)
        {
            if (scenarioName == null)
            {
                return false;
            }
            //Blacklist asteroid module from every game mode
            if (scenarioName == "DiscoverableObjects")
            {
                //We hijack this and enable / disable it if we need to.
                return false;
            }
            if (allScenarioTypesInAssemblies == null)
            {
                //Load type dictionary on first use
                LoadScenarioTypes();
            }
            if (!allScenarioTypesInAssemblies.ContainsKey(scenarioName))
            {
                //Module missing
                return false;
            }
            Type scenarioType = allScenarioTypesInAssemblies[scenarioName];
            KSPScenario[] scenarioAttributes = (KSPScenario[])scenarioType.GetCustomAttributes(typeof(KSPScenario), true);
            if (scenarioAttributes.Length > 0)
            {
                KSPScenario attribute = scenarioAttributes[0];
                bool protoAllowed = false;
                if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                {
                    protoAllowed = protoAllowed || attribute.HasCreateOption(ScenarioCreationOptions.AddToExistingCareerGames);
                    protoAllowed = protoAllowed || attribute.HasCreateOption(ScenarioCreationOptions.AddToNewCareerGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX)
                {
                    protoAllowed = protoAllowed || attribute.HasCreateOption(ScenarioCreationOptions.AddToExistingScienceSandboxGames);
                    protoAllowed = protoAllowed || attribute.HasCreateOption(ScenarioCreationOptions.AddToNewScienceSandboxGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
                {
                    protoAllowed = protoAllowed || attribute.HasCreateOption(ScenarioCreationOptions.AddToExistingSandboxGames);
                    protoAllowed = protoAllowed || attribute.HasCreateOption(ScenarioCreationOptions.AddToNewSandboxGames);
                }
                return protoAllowed;
            }
            //Scenario is not marked with KSPScenario - let's load it anyway.
            return true;
        }

        public void SendScenarioModules(bool highPriority)
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

                byte[] scenarioBytes = configNodeSerializer.Serialize(scenarioNode);
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
                if (highPriority)
                {
                    networkWorker.SendScenarioModuleDataHighPriority(scenarioName.ToArray(), scenarioData.ToArray());
                }
                else
                {
                    networkWorker.SendScenarioModuleData(scenarioName.ToArray(), scenarioData.ToArray());
                }
            }
        }

        public void LoadScenarioDataIntoGame()
        {
            while (scenarioQueue.Count > 0)
            {
                ScenarioEntry scenarioEntry = scenarioQueue.Dequeue();
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
                        DarkLog.Debug("Skipping " + psm.moduleName + " scenario data in " + dmpGame.gameMode + " mode");
                    }
                }
            }
        }

        private ConfigNode CreateProcessedPartNode(string part, uint id, params ProtoCrewMember[] crew)
        {
            ConfigNode configNode = ProtoVessel.CreatePartNode(part, id, crew);
            if (part != "kerbalEVA")
            {
                ConfigNode[] nodes = configNode.GetNodes("RESOURCE");
                for (int i = 0; i < nodes.Length; i++)
                {
                    ConfigNode configNode2 = nodes[i];
                    if (configNode2.HasValue("amount"))
                    {
                        configNode2.SetValue("amount", 0.ToString(System.Globalization.CultureInfo.InvariantCulture), false);
                    }
                }
            }
            configNode.SetValue("flag", "Squad/Flags/default", true);
            return configNode;
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
                            DarkLog.Debug("Generating missing kerbal from ProgressTracking: " + kerbalName);
                            ProtoCrewMember pcm = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Crew);
                            pcm.ChangeName(kerbalName);
                            HighLogic.CurrentGame.CrewRoster.AddCrewMember(pcm);
                            //Also send it off to the server
                            vesselWorker.SendKerbalIfDifferent(pcm);
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

        public void UpgradeTheAstronautComplexSoTheGameDoesntBugOut()
        {
            ProtoScenarioModule sm = HighLogic.CurrentGame.scenarios.Find(psm => psm.moduleName == "ScenarioUpgradeableFacilities");
            if (sm != null)
            {
                if (ScenarioUpgradeableFacilities.protoUpgradeables.ContainsKey("SpaceCenter/AstronautComplex"))
                {
                    foreach (Upgradeables.UpgradeableFacility uf in ScenarioUpgradeableFacilities.protoUpgradeables["SpaceCenter/AstronautComplex"].facilityRefs)
                    {
                        DarkLog.Debug("Setting astronaut complex to max level");
                        uf.SetLevel(uf.MaxLevel);
                    }
                }
            }
        }

        public void LoadMissingScenarioDataIntoGame()
        {
            List<KSPScenarioType> validScenarios = KSPScenarioType.GetAllScenarioTypesInAssemblies();
            foreach (KSPScenarioType validScenario in validScenarios)
            {
                if (HighLogic.CurrentGame.scenarios.Exists(psm => psm.moduleName == validScenario.ModuleType.Name))
                {
                    continue;
                }
                bool loadModule = false;
                if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                {
                    loadModule = validScenario.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewCareerGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX)
                {
                    loadModule = validScenario.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewScienceSandboxGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
                {
                    loadModule = validScenario.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewSandboxGames);
                }
                if (loadModule)
                {
                    DarkLog.Debug("Creating new scenario module " + validScenario.ModuleType.Name);
                    HighLogic.CurrentGame.AddProtoScenarioModule(validScenario.ModuleType, validScenario.ScenarioAttributes.TargetScenes);
                }
            }
        }

        public void LoadScenarioData(ScenarioEntry entry)
        {
            if (!IsScenarioModuleAllowed(entry.scenarioName))
            {
                DarkLog.Debug("Skipped '" + entry.scenarioName + "' scenario data  in " + dmpGame.gameMode + " mode");
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
                newModule.Load(ScenarioRunner.Instance);
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

        public void Stop()
        {
            workerEnabled = false;
            dmpGame.updateEvent.Remove(Update);
        }
    }

    public class ScenarioEntry
    {
        public string scenarioName;
        public ConfigNode scenarioNode;
    }
}

