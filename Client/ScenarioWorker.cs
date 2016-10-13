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
        private Dictionary<string, Type> allScenarioTypesInAssemblies;
        //System.Reflection hackiness for loading kerbals into the crew roster:
        private delegate bool CrewMemberRosterDelegate(ProtoCrewMember pcm);
        private CrewMemberRosterDelegate AddCrewMemberToRoster;
        private CrewMemberRosterDelegate RemoveCrewMemberFromRoster;
        private static bool verbose = true;

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
                    SendScenarioModules(false);
                }
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
            if (scenarioName == "ScenarioDiscoverableObjects")
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
                if (highPriority)
                {
                    NetworkWorker.fetch.SendScenarioModuleDataHighPriority(scenarioName.ToArray(), scenarioData.ToArray());
                }
                else
                {
                    NetworkWorker.fetch.SendScenarioModuleData(scenarioName.ToArray(), scenarioData.ToArray());
                }
            }
        }

        public void LoadScenarioDataIntoGame()
        {
            while (scenarioQueue.Count > 0)
            {
                ScenarioEntry scenarioEntry = scenarioQueue.Dequeue();
                if (scenarioEntry.scenarioName == "ContractSystem")
                {
                    FixMissingAssetsForRecoverContracts(scenarioEntry.scenarioNode);
                    CreateMissingTourists(scenarioEntry.scenarioNode);
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

        private void CreateMissingTourists(ConfigNode contractSystemNode)
        {
            ConfigNode contractsNode = contractSystemNode.GetNode("CONTRACTS");
            foreach (ConfigNode contractNode in contractsNode.GetNodes("CONTRACT"))
            {
                if (contractNode.GetValue("type") == "TourismContract" && contractNode.GetValue("state") == "Active")
                {
                    foreach (ConfigNode paramNode in contractNode.GetNodes("PARAM"))
                    {
                        foreach (string kerbalName in paramNode.GetValues("kerbalName"))
                        {
                            if (!HighLogic.CurrentGame.CrewRoster.Exists(kerbalName))
                            {
                                DarkLog.Debug("Spawning missing tourist (" + kerbalName + ") for active tourism contract");
                                ProtoCrewMember pcm = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Tourist);
                                pcm.ChangeName(kerbalName);
                            }
                            else DarkLog.Debug("Skipped respawning existing tourist" + kerbalName + ") for active tourism contract");
                        }
                    }
                }
            }
        }

        // Fix for bug #381
        private void FixMissingAssetsForRecoverContracts(ConfigNode contractSystemNode)
        {
          if(verbose) DarkLog.Debug("RecoverAsset: Checking assets for recover contracts...");
          
          ConfigNode contractsNode = contractSystemNode.GetNode("CONTRACTS");
            if (contractsNode == null) {
                if(verbose) DarkLog.Debug("RecoverAsset: Contract node is null");
                return;
            }
            foreach (ConfigNode contractNode in contractsNode.GetNodes("CONTRACT"))
            {
                if (contractNode.GetValue("type") == "RecoverAsset")
                {
                    // Parse contract parameters
                    if(verbose) DarkLog.Debug("RecoverAsset: Parsing contract: " + contractNode.GetValue("kerbalName"));

                    int contractSeed = int.Parse(contractNode.GetValue("seed"));
                    System.Random generator = new System.Random(contractSeed);

                    int recoveryType = int.Parse(contractNode.GetValue("recoveryType"));
                    //  RECOVERY TYPES:
                    //      0: None
                    //      1: Kerbal
                    //      2: Part
                    //      3: Compound
                    int recoveryLocation = int.Parse(contractNode.GetValue("recoveryLocation"));
                    //  RECOVERY LOCATIONS:
                    //      0: None
                    //      1: Low orbit
                    //      2: High orbit
                    //      3: Surface
                    int bodyID = int.Parse(contractNode.GetValue("targetBody"));
                    CelestialBody contractBody = FlightGlobals.Bodies[bodyID];

                    string kerbalName = contractNode.GetValue("kerbalName");

                    string partName = contractNode.GetValue("partName");
                    uint partID = uint.Parse(contractNode.GetValue("partID"));
                    // For Offered contracts, partID is 0; else the cockpit/module part id

                    uint kerbalGender = uint.Parse(contractNode.GetValue("gender"));
                    // 1 = male 2 = female

                    double[] contractValues = Array.ConvertAll(contractNode.GetValue("values").Split(','), double.Parse);
                    double contractDeadline = contractValues[1];
                    // Example: values = 43200,46008000,14040,32076,14040,0,6,19,724961.999657156,681769.239657163,46689769.2396572,0

                    bool recoveringKerbal = recoveryType == 1 || recoveryType == 3;
                    bool recoveringPart = recoveryType == 2 || recoveryType == 3;

                    string contractTitle = (
                        (recoveringKerbal ? "Rescue " + kerbalName : "Recover Part" + partName) +
                        " from " + (
                            recoveryLocation == 3 ? "the Surface of" + contractBody.name :
                           (recoveryLocation == 1 ? "Low " : "") + contractBody.name + " Orbit"));
                    if(verbose) DarkLog.Debug("RecoverAsset: Contract: " + contractTitle);

                    // Look for existing Kerbal/vessel
                    ProtoCrewMember strandedKerbal = HighLogic.CurrentGame.CrewRoster.Exists(kerbalName) ? HighLogic.CurrentGame.CrewRoster[kerbalName] : null;

                    bool kerbalExists = !(strandedKerbal == null);

                    if (!kerbalExists) // Create and add kerbal to roster
                    {
                        if(verbose) DarkLog.Debug("****RecoverAsset: Kerbal " + kerbalName + " from contract " + contractTitle + " not found in roster, generating");

                        strandedKerbal = CreateContractKerbal(kerbalName, (ProtoCrewMember.Gender)kerbalGender);
                        VesselWorker.fetch.SendKerbalIfDifferent(strandedKerbal);
                    }
                    else
                    {
                        if (strandedKerbal.type == ProtoCrewMember.KerbalType.Unowned && strandedKerbal.rosterStatus != ProtoCrewMember.RosterStatus.Assigned) // something wrong?
                        {
                            if (verbose) DarkLog.Debug("RecoverAsset: " + strandedKerbal.name + " was not Assigned - " + strandedKerbal.rosterStatus.ToString());
                            strandedKerbal.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                        }
                    }
                    
                    ProtoVessel strandedVessel = null;
                    if (partID != 0)
                    {   // attempt to find vessel containing the contract's part
                        strandedVessel = FindProtoVesselByPartID(partID);
                    }

                    bool vesselExists = !(strandedVessel == null);

                    if (!vesselExists && verbose) DarkLog.Debug("RecoverAsset: No existing vessel found for contract " + contractTitle);
                    else if(verbose) DarkLog.Debug("RecoverAsset: Contract vessel " + strandedVessel.vesselName + " (" + strandedVessel.vesselID + ")");

                    if (contractNode.GetValue("state") == "Active")
                    {
                        if(verbose) DarkLog.Debug("RecoverAsset: Contract: " + contractTitle + " is Active");

                        if (!vesselExists)
                        {
                            strandedVessel = CreateContractVessel(contractNode, strandedKerbal);
                            if (strandedVessel != null)
                            {
                                vesselExists = true;
                            }
                        }
                    }
                    else if (contractNode.GetValue("state") == "Offered")
                    {
                        if(verbose) DarkLog.Debug("RecoverAsset: Offered contract: " + contractTitle);
                        // There should be no kerbal and vessel yet
                        // Check if there are... deassign and destroy?
                      
                        if (vesselExists)
                        {
                            // Check 
                            if(verbose) DarkLog.Debug("RecoverAsset: Vessel for Offered contract: " + strandedVessel.vesselName);
                        }
                    }
                    else
                    { //Failed? Should probably clean up - destroy the vessel and Kerbal
                        if(verbose) DarkLog.Debug("RecoverAsset: Contract in Failed or invalid state: " + contractTitle);
                        if (vesselExists)
                        {
                            if(verbose) DarkLog.Debug("RecoverAsset: Killing existing vessel: " + strandedVessel.vesselName);
                            VesselWorker.fetch.QueueVesselRemove(strandedVessel.vesselID, HighLogic.CurrentGame.UniversalTime, false, "");
                        }
                        if (kerbalExists)
                        {
                            if(verbose) DarkLog.Debug("RecoverAsset: Releasing Kerbal: " + strandedKerbal.name);
                            if (!(strandedKerbal.rosterStatus == ProtoCrewMember.RosterStatus.Dead || strandedKerbal.rosterStatus == ProtoCrewMember.RosterStatus.Missing))
                            {
                                //Remove kerbal from crew roster
                                if (RemoveCrewMemberFromRoster == null)
                                {
                                    MethodInfo removeMemberFromCrewRosterMethod = typeof(KerbalRoster).GetMethod("Remove", BindingFlags.Public | BindingFlags.Instance);
                                    RemoveCrewMemberFromRoster = (CrewMemberRosterDelegate)Delegate.CreateDelegate(typeof(CrewMemberRosterDelegate), HighLogic.CurrentGame.CrewRoster, removeMemberFromCrewRosterMethod);
                                }
                                if (RemoveCrewMemberFromRoster == null)
                                {
                                    throw new Exception("Failed to remove Kerbal from roster (#381): " + kerbalName);
                                }
                                else
                                {
                                    RemoveCrewMemberFromRoster(strandedKerbal);
                                }
                            }
                        }
                    }
                }
            }
        }

        private ProtoCrewMember CreateContractKerbal(string kerbalName, ProtoCrewMember.Gender gender)
        {
            ProtoCrewMember newKerbal = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Unowned);
            newKerbal.ChangeName(kerbalName);
            newKerbal.gender = (ProtoCrewMember.Gender)gender;
            newKerbal.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
            //strandedKerbal.seat = null;
            //strandedKerbal.seatIdx = -1;

            if (verbose) DarkLog.Debug("RecoverAsset: Adding " + kerbalName + " to roster (" + newKerbal.type + ", " + newKerbal.trait + "," + newKerbal.rosterStatus + ")");
            if (AddCrewMemberToRoster == null)
            {
                MethodInfo addMemberToCrewRosterMethod = typeof(KerbalRoster).GetMethod("AddCrewMember", BindingFlags.Public | BindingFlags.Instance);
                AddCrewMemberToRoster = (CrewMemberRosterDelegate)Delegate.CreateDelegate(typeof(CrewMemberRosterDelegate), HighLogic.CurrentGame.CrewRoster, addMemberToCrewRosterMethod);
            }
            if (AddCrewMemberToRoster == null)
            {
                throw new Exception("Failed to add Kerbal to roster (#381): " + kerbalName);
            }
            AddCrewMemberToRoster.Invoke(newKerbal);
            if (HighLogic.CurrentGame.CrewRoster.Exists(kerbalName))
            {
                if (verbose) DarkLog.Debug("RecoverAsset: Succesfully added " + kerbalName + " to roster");
                return newKerbal;
            }
            else return null;
        }

        ProtoVessel FindProtoVesselByPartID(uint partID)
        {
            if (verbose) DarkLog.Debug("RecoverAsset: Looking for vessel with part ID " + partID);

            Vessel findVessel = null;
            Part findPart = FlightGlobals.FindPartByID(partID);
            if (findPart != null)
            {
                if (verbose) DarkLog.Debug("RecoverAsset: Found part ID " + partID + "=" + findPart.flightID + " in vessel " + findPart.vessel.vesselName);
                findVessel = findPart.vessel;
                return findVessel.protoVessel;
            }
            else // try the protopart / protovessel
            {
                if (verbose) DarkLog.Debug("RecoverAsset: Looking for vessel with protopart ID " + partID);
                ProtoPartSnapshot findProtoPart = FlightGlobals.FindProtoPartByID(partID);
                if (findProtoPart != null)
                {
                    if (verbose) DarkLog.Debug("RecoverAsset: Found protopart ID " + partID + "=" + findProtoPart.flightID + " in vessel " + findProtoPart.pVesselRef.vesselName);
                    findVessel = findProtoPart.pVesselRef.vesselRef;
                    return findVessel.protoVessel;
                }
            }
            
            // Finally, try to find the partID in a vessel by iterating through all ProtoVessels
            foreach (ProtoVessel pv in HighLogic.CurrentGame.flightState.protoVessels)
            {
                foreach(ProtoPartSnapshot pps in pv.protoPartSnapshots)
                {
                    if(pps.flightID == partID || pps.missionID == partID || pps.craftID == partID)
                    {
                        findVessel = FlightGlobals.Vessels.Find(v => v.protoVessel.Equals(pv));
                        if (verbose) DarkLog.Debug("FindVesselByPartID: Found part " + partID + " in ProtoVessel " + pv.vesselID);
                        if (findVessel == null) return pv; else return findVessel.protoVessel;
                    }
                }
            }

            if (findVessel == null)
            {
                if (verbose) DarkLog.Debug("RecoverAsset: No vessel found");
                return null;
            }
            else return findVessel.protoVessel;
        }

        private ProtoVessel CreateContractVessel(ConfigNode contractNode, ProtoCrewMember strandedKerbal)
        {
            if (contractNode.GetValue("type") != "RecoverAsset") return null;

            int contractSeed = int.Parse(contractNode.GetValue("seed"));
            System.Random generator = new System.Random(contractSeed);

            int recoveryType = int.Parse(contractNode.GetValue("recoveryType"));
            //  RECOVERY TYPES:
            //      0: None
            //      1: Kerbal
            //      2: Part
            //      3: Compound
            int recoveryLocation = int.Parse(contractNode.GetValue("recoveryLocation"));
            //  RECOVERY LOCATIONS:
            //      0: None
            //      1: Low orbit
            //      2: High orbit
            //      3: Surface
            int bodyID = int.Parse(contractNode.GetValue("targetBody"));
            CelestialBody contractBody = FlightGlobals.Bodies[bodyID];

            string kerbalName = contractNode.GetValue("kerbalName");

            string partName = contractNode.GetValue("partName");
            uint partID = uint.Parse(contractNode.GetValue("partID"));
            // For Offered contracts, partID is 0; else the cockpit/module part id
            uint newPartID = partID == 0 ? ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState) : partID;
            //contractNode.SetValue("partID", newPartID);

            uint kerbalGender = uint.Parse(contractNode.GetValue("gender"));
            // 1 = male 2 = female

            double[] contractValues = Array.ConvertAll(contractNode.GetValue("values").Split(','), double.Parse);
            double contractDeadline = contractValues[1];
            // Example: values = 43200,46008000,14040,32076,14040,0,6,19,724961.999657156,681769.239657163,46689769.2396572,0

            bool recoveringKerbal = recoveryType == 1 || recoveryType == 3;
            bool recoveringPart = recoveryType == 2 || recoveryType == 3;

            if (strandedKerbal == null && recoveringKerbal)
            {
                DarkLog.Debug("RecoverAsset: null kerbal for rescue");
                return null;
            }
            string contractTitle = (
                (recoveringKerbal ? "Rescue " + kerbalName : "Recover Part" + partName) +
                " from " + (
                    recoveryLocation == 3 ? "the Surface of" + contractBody.name :
                    (recoveryLocation == 1 ? "Low " : "") + contractBody.name + " Orbit"));
            if (verbose) DarkLog.Debug("RecoverAsset: Contract: " + contractTitle);

            List<string> vesselAdjectives = (recoveryType == 1) || (recoveryType == 3) ?
            new List<string> {
                                    "Shipwreck",
                                    "Wreckage",
                                    "Pod",
                                    "Capsule",
                                    "Derelict",
                                    "Heap",
                                    "Hulk",
                                    "Craft",
                                    "Debris",
                                    "Scrap"
            } :
            new List<string> {   // recoveryType == 0, 2 or else (parts)
                                    "Prototype",
                                    "Device",
                                    "Part",
                                    "Module",
                                    "Unit",
                                    "Component"
            };
            string vesselName = FinePrint.Utilities.StringUtilities.PossessiveString(FinePrint.Utilities.StringUtilities.ShortKerbalName(kerbalName)) + " " + vesselAdjectives[(int)Math.Round(generator.NextDouble() * (vesselAdjectives.Count - 1))];
            if (verbose) DarkLog.Debug("****RecoverAsset: Spawning Vessel for Active contract: " + vesselName + ", part " + partID + ", flight " + newPartID);

            Orbit strandedOrbit;
            if (recoveryLocation != 1) // Low orbit
            {
                if (recoveryLocation != 2) // High orbit
                {
                    strandedOrbit = new Orbit(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, contractBody);
                }
                else
                {
                    strandedOrbit = FinePrint.Utilities.OrbitUtilities.GenerateOrbit(int.Parse(contractNode.GetValue("seed")), contractBody, FinePrint.Utilities.OrbitType.RANDOM,
                        FinePrint.ContractDefs.Recovery.HighOrbitDifficulty, FinePrint.ContractDefs.Recovery.HighOrbitDifficulty, 0.0);
                }
            }
            else
            {
                double minAltitude = FinePrint.Utilities.CelestialUtilities.GetMinimumOrbitalDistance(contractBody, 1f) - contractBody.Radius;
                strandedOrbit = Orbit.CreateRandomOrbitAround(contractBody, contractBody.Radius + minAltitude * 1.1000000238418579, contractBody.Radius + minAltitude * 1.25);
                strandedOrbit.meanAnomalyAtEpoch = generator.NextDouble() * 2.0 * Math.PI;
            }
            
            ConfigNode protoVesselNode = ProtoVessel.CreateVesselNode(vesselName, partName == "kerbalEVA" ? VesselType.EVA : VesselType.Ship, strandedOrbit, 0,
                new ConfigNode[] { CreateProcessedPartNode(partName, newPartID, new ProtoCrewMember[] { strandedKerbal }) },
                new ConfigNode[] { ProtoVessel.CreateDiscoveryNode(DiscoveryLevels.Unowned, UntrackedObjectClass.A, contractDeadline * 2, contractDeadline * 2) });
            protoVesselNode.AddValue("prst", "True");
            if (verbose) DarkLog.Debug("RecoverAsset: Creating protovessel");
            ProtoVessel strandedProtoVessel = VesselWorker.fetch.CreateSafeProtoVesselFromConfigNode(protoVesselNode, Guid.NewGuid());
            VesselWorker.fetch.RegisterServerVessel(strandedProtoVessel.vesselID);
            HighLogic.CurrentGame.flightState.protoVessels.Add(strandedProtoVessel);
            NetworkWorker.fetch.SendVesselProtoMessage(strandedProtoVessel,false,false);
            return HighLogic.CurrentGame.flightState.protoVessels.Find(pv => pv.vesselID.Equals(strandedProtoVessel.vesselID));
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
                            if (AddCrewMemberToRoster == null)
                            {
                                MethodInfo addMemberToCrewRosterMethod = typeof(KerbalRoster).GetMethod("AddCrewMember", BindingFlags.Public | BindingFlags.Instance);
                                AddCrewMemberToRoster = (CrewMemberRosterDelegate)Delegate.CreateDelegate(typeof(CrewMemberRosterDelegate), HighLogic.CurrentGame.CrewRoster, addMemberToCrewRosterMethod);
                            }
                            if (AddCrewMemberToRoster == null)
                            {
                                throw new Exception("Failed to initialize AddCrewMemberToRoster for #172 ProgressTracking fix.");
                            }
                            DarkLog.Debug("Generating missing kerbal from ProgressTracking: " + kerbalName);
                            ProtoCrewMember pcm = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Crew);
                            pcm.ChangeName(kerbalName);
                            AddCrewMemberToRoster(pcm);
                            //Also send it off to the server
                            VesselWorker.fetch.SendKerbalIfDifferent(pcm);
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

