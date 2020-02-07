using System;
using System.Collections.Generic;
using Contracts;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class ScenarioWorker
    {
        public bool workerEnabled = false;
        private List<string> warnedModules = new List<string>();
        private Dictionary<string, string> checkData = new Dictionary<string, string>();
        private Queue<ScenarioEntry> scenarioQueue = new Queue<ScenarioEntry>();
        private bool blockScenarioDataSends = false;
        private float lastScenarioSendTime = 0f;
        private const float SEND_SCENARIO_DATA_INTERVAL = 30f;
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
        /// <summary>
        /// Methods to call before DMP loads a network scenario module. Returning true will count the message as handled and will prevent DMP from loading it.
        /// </summary>
        private Dictionary<string, Func<ConfigNode, bool>> beforeCallback = new Dictionary<string, Func<ConfigNode, bool>>();
        /// <summary>
        /// Methods to call after DMP loads a network scenario module
        /// </summary>
        private Dictionary<string, Action<ConfigNode>> afterCallback = new Dictionary<string, Action<ConfigNode>>();
        private NamedAction updateAction;

        public ScenarioWorker(DMPGame dmpGame, VesselWorker vesselWorker, ConfigNodeSerializer configNodeSerializer, NetworkWorker networkWorker)
        {
            this.dmpGame = dmpGame;
            this.vesselWorker = vesselWorker;
            this.configNodeSerializer = configNodeSerializer;
            this.networkWorker = networkWorker;
            updateAction = new NamedAction(Update);
            dmpGame.updateEvent.Add(updateAction);
        }

        public void RegisterBeforeCallback(string moduleName, Func<ConfigNode, bool> callback)
        {
            beforeCallback[moduleName] = callback;
        }

        public void RegisterAfterCallback(string moduleName, Action<ConfigNode> callback)
        {
            afterCallback[moduleName] = callback;
        }

        private void RegisterGameHooks()
        {
            registered = true;
            GameEvents.Contract.onAccepted.Add(OnContractAccepted);
            GameEvents.OnFundsChanged.Add(OnFundsChanged);
            GameEvents.OnTechnologyResearched.Add(OnTechnologyResearched);
            GameEvents.OnScienceRecieved.Add(OnScienceRecieved);
            GameEvents.OnScienceChanged.Add(OnScienceChanged);
            GameEvents.OnReputationChanged.Add(OnReputationChanged);
            RegisterAfterCallback("Funding", FundingCallback);
            RegisterAfterCallback("ResearchAndDevelopment", ResearchAndDevelopmentCallback);
            RegisterAfterCallback("Reputation", ReputationCallback);
        }

        private void UnregisterGameHooks()
        {
            registered = false;
            GameEvents.Contract.onAccepted.Remove(OnContractAccepted);
            GameEvents.OnFundsChanged.Remove(OnFundsChanged);
            GameEvents.OnTechnologyResearched.Remove(OnTechnologyResearched);
            GameEvents.OnScienceRecieved.Remove(OnScienceRecieved);
            GameEvents.OnScienceChanged.Remove(OnScienceChanged);
            GameEvents.OnReputationChanged.Remove(OnReputationChanged);
        }

        //Events so we can quickly send our changed modules
        private void OnReputationChanged(float data0, TransactionReasons data1)
        {
            SendScenarioModules(false);
        }

        private void OnScienceChanged(float data0, TransactionReasons data1)
        {
            SendScenarioModules(false);
        }

        private void OnScienceRecieved(float data0, ScienceSubject data1, ProtoVessel data2, bool data3)
        {
            SendScenarioModules(false);
        }

        private void OnTechnologyResearched(GameEvents.HostTargetAction<RDTech, RDTech.OperationResult> data)
        {
            SendScenarioModules(false);
        }

        private void OnFundsChanged(double newValue, TransactionReasons reason)
        {
            SendScenarioModules(false);
        }

        //Callbacks for UI
        private void FundingCallback(ConfigNode configNode)
        {
            GameEvents.OnFundsChanged.Fire(Funding.Instance.Funds, TransactionReasons.None);
        }

        private void ResearchAndDevelopmentCallback(ConfigNode configNode)
        {
            GameEvents.OnScienceChanged.Fire(ResearchAndDevelopment.Instance.Science, TransactionReasons.None);
        }

        private void ReputationCallback(ConfigNode configNode)
        {
            GameEvents.OnReputationChanged.Fire(Reputation.CurrentRep, TransactionReasons.None);
        }

        //Kerbal fixups
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

        private void Update()
        {
            if (workerEnabled)
            {
                if (!registered) RegisterGameHooks();
                if (!blockScenarioDataSends)
                {
                    if ((Client.realtimeSinceStartup - lastScenarioSendTime) > SEND_SCENARIO_DATA_INTERVAL)
                    {
                        SendScenarioModules(false);
                    }
                    lock (scenarioQueue)
                    {
                        while (scenarioQueue.Count > 0)
                        {
                            ScenarioEntry se = scenarioQueue.Dequeue();
                            LoadScenarioData(se);
                        }
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
            lastScenarioSendTime = Client.realtimeSinceStartup;
            List<string> scenarioName = new List<string>();
            List<ByteArray> scenarioData = new List<ByteArray>();

            foreach (ScenarioModule sm in ScenarioRunner.GetLoadedModules())
            {
                string scenarioType = sm.GetType().Name;
                if (!IsScenarioModuleAllowed(scenarioType))
                {
                    continue;
                }
                try
                {
                    ConfigNode scenarioNode = new ConfigNode();
                    sm.Save(scenarioNode);

                    ByteArray scenarioBytes = configNodeSerializer.Serialize(scenarioNode);
                    string scenarioHash = Common.CalculateSHA256Hash(scenarioBytes);
                    if (scenarioBytes.Length == 0)
                    {
                        DarkLog.Debug("Error writing scenario data for " + scenarioType);
                        ByteRecycler.ReleaseObject(scenarioBytes);
                        continue;
                    }
                    if (checkData.ContainsKey(scenarioType) ? (checkData[scenarioType] == scenarioHash) : false)
                    {
                        //Data is the same since last time - Skip it.
                        ByteRecycler.ReleaseObject(scenarioBytes);
                        continue;
                    }
                    else
                    {
                        checkData[scenarioType] = scenarioHash;
                    }
                    scenarioName.Add(scenarioType);
                    scenarioData.Add(scenarioBytes);
                }
                catch (Exception e)
                {
                    string fullName = sm.GetType().FullName;
                    DarkLog.Debug("Unable to save module data from " + fullName + ", skipping upload of this module. Exception: " + e);
                    if (!warnedModules.Contains(fullName))
                    {
                        warnedModules.Add(fullName);
                        if (!fullName.Contains("Expansions.Serenity.DeployedScience"))
                        {
                            ScreenMessages.PostScreenMessage("DMP was unable to save " + fullName + ", this module data will be lost.", 30f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
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
            lock (scenarioQueue)
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
                            ByteArray scenarioHashBytes = configNodeSerializer.Serialize(scenarioEntry.scenarioNode);
                            checkData[scenarioEntry.scenarioName] = Common.CalculateSHA256Hash(scenarioHashBytes);
                            ByteRecycler.ReleaseObject(scenarioHashBytes);
                        }
                        else
                        {
                            DarkLog.Debug("Skipping " + psm.moduleName + " scenario data in " + dmpGame.gameMode + " mode");
                        }
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
                ScreenMessages.PostScreenMessage("Scenario " + entry.scenarioName + " failed to load, blocking scenario uploads.", 10f, ScreenMessageStyle.UPPER_CENTER);
                blockScenarioDataSends = true;
                return;
            }

            //Load data into game
            if (DidScenarioChange(entry))
            {
                bool loaded = false;
                ByteArray scenarioBytes = configNodeSerializer.Serialize(entry.scenarioNode);
                checkData[entry.scenarioName] = Common.CalculateSHA256Hash(scenarioBytes);
                ByteRecycler.ReleaseObject(scenarioBytes);
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
                            bool skipLoad = false;
                            if (beforeCallback.ContainsKey(psm.moduleName))
                            {
                                skipLoad = beforeCallback[psm.moduleName](entry.scenarioNode);
                            }
                            if (!skipLoad)
                            {
                                psm.moduleRef.Load(entry.scenarioNode);
                            }
                            if (afterCallback.ContainsKey(psm.moduleName))
                            {
                                afterCallback[psm.moduleName](entry.scenarioNode);
                            }
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
            lock (scenarioQueue)
            {
                ScenarioEntry entry = new ScenarioEntry();
                entry.scenarioName = scenarioName;
                entry.scenarioNode = scenarioData;
                scenarioQueue.Enqueue(entry);
            }
        }

        private bool DidScenarioChange(ScenarioEntry scenarioEntry)
        {
            string previousScenarioHash = null;
            ByteArray scenarioBytes = configNodeSerializer.Serialize(scenarioEntry.scenarioNode);
            string currentScenarioHash = Common.CalculateSHA256Hash(scenarioBytes);
            ByteRecycler.ReleaseObject(scenarioBytes);
            if (checkData.TryGetValue(scenarioEntry.scenarioName, out previousScenarioHash))
            {
                return previousScenarioHash != currentScenarioHash;
            }
            return true;
        }

        public void Stop()
        {
            workerEnabled = false;
            dmpGame.updateEvent.Remove(updateAction);
        }
    }

    public class ScenarioEntry
    {
        public string scenarioName;
        public ConfigNode scenarioNode;
    }
}
