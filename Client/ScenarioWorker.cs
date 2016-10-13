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

                if (scenarioType == "ContractSystem") FixMissingAssetsForRecoverContracts(ref scenarioNode);

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
                    FixMissingAssetsForRecoverContracts(ref scenarioEntry.scenarioNode);
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
        private void FixMissingAssetsForRecoverContracts(ref ConfigNode contractSystemNode)
        {
            /*
             * Notes on game logic
             * ===================
             * 
             * Rescue contract Offered => No Kerbals in roster, no stranded ships/parts or EVAs in Tracking Station
             * Upon accepting the contract, a Vessel is generated based on contract info, and the kerbal is also added to the roster as Assigned to the vessel
             * 
             * Possible use cases that need to be fixed:
             * 
             * A) The happy path - when the contract is Accepted, the Kerbal and Vessel must be sent to the server unscathed
             * 
             * B) (the current state at bug #381) A Scenario is loaded that contains Active rescue contracts
             *   B.1) Check/search for the corresponding Kerbal in the roster and Vessel containing the Kerbal
             *   B.2) If (and only if) the Kerbal and/or the Vessel do not exist on the game and server, fix that
             *   
             * 
             */
            
            ConfigNode contractsNode = contractSystemNode.GetNode("CONTRACTS");
            foreach (ConfigNode contractNode in contractsNode.GetNodes("CONTRACT"))
            {
                if (contractNode.GetValue("type") == "RecoverAsset")
                {
                    // Parse contract parameters

                    int contractSeed = int.Parse(contractNode.GetValue("seed"));
                    System.Random generator = new System.Random(contractSeed);

                    string contractAgent = contractNode.GetValue("agent");

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
                    // TODO Find this Kerbal in roster already

                    string partName = contractNode.GetValue("partName");
                    uint partID = uint.Parse(contractNode.GetValue("partID"));
                    // For Offered contracts, partID must be 0; for others, find the part?

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

                    // Look for existing Kerbal/vessel
                    ProtoCrewMember strandedKerbal = null;
                    strandedKerbal = HighLogic.CurrentGame.CrewRoster.Exists(kerbalName) ? HighLogic.CurrentGame.CrewRoster[kerbalName] : null;

                    
                    List<string> vesselAdjectives = (recoveryType == 1) || (recoveryType == 3) ?
                        new List<string>
                            {
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
                        new List<string>    // recoveryType == 0, 2 or else (parts)
                            {
                                "Prototype",
                                "Device",
                                "Part",
                                "Module",
                                "Unit",
                                "Component"
                            };

                    ProtoVessel strandedVessel = null;
                    /*Part findPart = FlightGlobals.FindPartByID(partID); // attempt at finding the vessel
                    if (findPart != null)
                    {
                        DarkLog.Debug("RecoverAsset: Found part ID " + partID + ", " + findPart.flightID);
                        findVessel = findPart.vessel;
                        strandedVessel = findVessel.protoVessel;
                    }*/
                    foreach (ProtoVessel pv in HighLogic.CurrentGame.flightState.protoVessels.FindAll(v => v.vesselName.Contains(FinePrint.Utilities.StringUtilities.ShortKerbalName(kerbalName))))
                    {
                        DarkLog.Debug("RecoverAsset: Found vessel with the kerbal's name in it:" + pv.vesselName);
                        Vessel findVessel = pv.vesselRef;
                        if (findVessel != null)
                        {
                            DarkLog.Debug("RecoverAsset: Looking for part ID " + partID);
                            Part findPart = findVessel.GetActiveParts().Find(p => p.flightID == partID);
                            if (findPart.flightID == partID && findVessel.name == pv.vesselName)
                            {
                                DarkLog.Debug("RecoverAsset: Found vessel " + pv.vesselName + " containing part " + partID + "=" + findPart.flightID);
                                strandedVessel = pv;
                            }
                            else strandedVessel = null;
                        }
                    }
                    //strandedVessel = HighLogic.CurrentGame.flightState.protoVessels.Find(v => v.vesselName.Equals(vesselName));

                    if (strandedVessel == null)
                    foreach (string vesselDescription in vesselAdjectives) // "brute force" finding the vessel by name
                    {
                        string vesselName = FinePrint.Utilities.StringUtilities.PossessiveString(FinePrint.Utilities.StringUtilities.ShortKerbalName(kerbalName)) + " " + vesselDescription;
                        if (HighLogic.CurrentGame.flightState.protoVessels.Exists(v => v.vesselName.Equals(vesselName)))
                        {
                            DarkLog.Debug("RecoverAsset: Found vessel by adjective:" + vesselName);
                            ProtoVessel pv = HighLogic.CurrentGame.flightState.protoVessels.Find(v => v.vesselName.Equals(vesselName));
                            if (pv.vesselName == vesselName) strandedVessel = pv;
                        }
                    }
                    
                    bool vesselExists = !(strandedVessel == null);
                    bool kerbalExists = !(strandedKerbal == null);

                    if (!kerbalExists)
                    {
                       DarkLog.Debug("RecoverAsset: Generating UNOWNED Kerbal " + kerbalName + " for contract " + contractTitle);

                        strandedKerbal = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Unowned);
                        strandedKerbal.ChangeName(kerbalName);
                        strandedKerbal.gender = (ProtoCrewMember.Gender)kerbalGender;
                        strandedKerbal.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                        strandedKerbal.isBadass = false;
                        strandedKerbal.hasToured = false;
                        strandedKerbal.inactive = false;
                        strandedKerbal.inactiveTimeEnd = 0;
                        strandedKerbal.gExperienced = 0;
                        strandedKerbal.outDueToG = false;
                        strandedKerbal.seat = null;
                        strandedKerbal.seatIdx = -1;

                        if (!HighLogic.CurrentGame.CrewRoster.Exists(strandedKerbal.name))
                        {
                            //Add kerbal to crew roster.
                            if (AddCrewMemberToRoster == null)
                            {
                                MethodInfo addMemberToCrewRosterMethod = typeof(KerbalRoster).GetMethod("AddCrewMember", BindingFlags.Public | BindingFlags.Instance);
                                AddCrewMemberToRoster = (CrewMemberRosterDelegate)Delegate.CreateDelegate(typeof(CrewMemberRosterDelegate), HighLogic.CurrentGame.CrewRoster, addMemberToCrewRosterMethod);
                            }
                            if (AddCrewMemberToRoster == null)
                            {
                                throw new Exception("Failed to remove Kerbal from roster (#381): " + kerbalName);
                            }
                            AddCrewMemberToRoster.Invoke(strandedKerbal);
                        }
                        kerbalExists = true;
                    }

                    if (contractNode.GetValue("state") == "Active")
                    {
                        DarkLog.Debug("RecoverAsset: Found ACTIVE contract: " + contractTitle);

                        if (kerbalExists)
                        {
                            DarkLog.Debug("RecoverAsset: Found existing KERBAL for ACTIVE contract: " + strandedKerbal.name + ", " + strandedKerbal.rosterStatus.ToString() + ", " + strandedKerbal.type.ToString());
                            if (strandedKerbal.rosterStatus != ProtoCrewMember.RosterStatus.Assigned) // something wrong?
                            {
                                DarkLog.Debug("RecoverAsset: KERBAL is NOT ASSIGNED: " + strandedKerbal.name + " is " + strandedKerbal.rosterStatus.ToString());
                            }
                            
                        }
                        if (vesselExists)
                        {
                            // Check 
                            DarkLog.Debug("RecoverAsset: Found existing VESSEL for ACTIVE contract: " + strandedVessel.vesselName);
                        }

                        if (!vesselExists)
                        {
                            // Spawn kerbal in capsule/pod unless EVA (doesn't happen?)
                            uint newPartID = partID == 0 ? ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState) : partID;
                            contractNode.SetValue("partID", newPartID);

                            string vesselName = FinePrint.Utilities.StringUtilities.PossessiveString(FinePrint.Utilities.StringUtilities.ShortKerbalName(kerbalName)) + " " + vesselAdjectives[(int)(generator.NextDouble() * (vesselAdjectives.Count - 1))];
                            Orbit strandedOrbit;
                            // Low orbit
                            if (recoveryLocation != 1)
                            {
                                // High orbit
                                if (recoveryLocation != 2)
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

                            DarkLog.Debug("RecoverAsset: Spawning missing VESSEL for Active contract: " + vesselName + " / " + contractTitle);
                            ConfigNode protoVesselNode = null;
                            ConfigNode[] partNodes = new ConfigNode[] { CreateProcessedPartNode(partName, newPartID, new ProtoCrewMember[] { strandedKerbal }) };
                                partNodes[0].SetValue("flag", "Squad/Flags/" + contractAgent);
                            protoVesselNode = ProtoVessel.CreateVesselNode(partName == "kerbalEVA" ? kerbalName : vesselName, partName == "kerbalEVA" ? VesselType.EVA : VesselType.Ship, strandedOrbit, 0,
                                partNodes,
                                new ConfigNode[] { ProtoVessel.CreateDiscoveryNode(DiscoveryLevels.Unowned, UntrackedObjectClass.A, contractDeadline * 2, contractDeadline * 2) } );
                            protoVesselNode.AddValue("prst", "True");
                            strandedVessel = HighLogic.CurrentGame.AddVessel(protoVesselNode);

                            //VesselWorker.fetch.LoadVessel(protoVesselNode, strandedVessel.vesselID, false);
                            NetworkWorker.fetch.SendVesselProtoMessage(strandedVessel, false, false);
                        }
                    }
                    else if (contractNode.GetValue("state") == "Offered")
                    {
                        DarkLog.Debug("RecoverAsset: Found Offered contract: " + contractTitle);
                        // There should be no kerbal and vessel yet
                        // Check if there are... deassign and destroy?
                        if (!(strandedKerbal == null))
                        {
                            DarkLog.Debug("RecoverAsset: Found already existing Kerbal for Offered contract: " + strandedKerbal.name + ", " + strandedKerbal.rosterStatus.ToString() + ", " + strandedKerbal.type.ToString());
                            if (strandedKerbal.rosterStatus != ProtoCrewMember.RosterStatus.Assigned) // something wrong?
                            {
                                DarkLog.Debug("RecoverAsset: Already existing Kerbal for Offered contract is not assigned: " + strandedKerbal.name + " is " + strandedKerbal.rosterStatus.ToString());
                            }
                            // Check if assigned to the vessel
                        }
                        if (!(strandedVessel == null))
                        {
                            // Check 
                            DarkLog.Debug("RecoverAsset: Found already existing vessel for Offered contract: " + strandedVessel.vesselName);
                        }
                    }
                    else
                    { //Failed, destroy derelict and release Kerbal?
                        DarkLog.Debug("RecoverAsset: Found contract in Failed or invalid state: " + contractTitle);
                        if (vesselExists)
                        {
                            DarkLog.Debug("RecoverAsset: Killing existing vessel: " + strandedVessel.vesselName);
                            //VesselWorker.fetch.QueueVesselRemove(strandedVessel.vesselID, HighLogic.CurrentGame.UniversalTime, false, "");
                        }
                        if (kerbalExists)
                        {
                            DarkLog.Debug("RecoverAsset: Releasing Kerbal: " + strandedKerbal.name);
                            if (!(strandedKerbal.rosterStatus == ProtoCrewMember.RosterStatus.Dead || strandedKerbal.rosterStatus == ProtoCrewMember.RosterStatus.Missing))
                            {
                                //Remove kerbal from crew roster.
                                if (RemoveCrewMemberFromRoster == null)
                                {
                                    MethodInfo removeMemberFromCrewRosterMethod = typeof(KerbalRoster).GetMethod("Remove", BindingFlags.Public | BindingFlags.Instance);
                                    RemoveCrewMemberFromRoster = (CrewMemberRosterDelegate)Delegate.CreateDelegate(typeof(CrewMemberRosterDelegate), HighLogic.CurrentGame.CrewRoster, removeMemberFromCrewRosterMethod);
                                    //RemoveCrewMemberFromRoster(strandedKerbal);
                                    //strandedKerbal.type = ProtoCrewMember.KerbalType.Unowned;
                                }
                                if (RemoveCrewMemberFromRoster == null)
                                {
                                    throw new Exception("Failed to remove Kerbal from roster (#381): " + kerbalName);
                                }
                            }
                        }
                    }
                    VesselWorker.fetch.SendKerbalIfDifferent(strandedKerbal);
                }
            }
        }

        private bool PartHasSeats(string partName)
        {
            AvailablePart partInfoByName = PartLoader.getPartInfoByName(partName);
            InternalModel internalModel = null;
            if (partInfoByName != null)
            {
                string name = string.Empty;
                if (partInfoByName.internalConfig.HasValue("name")) name = partInfoByName.internalConfig.GetValue("name");
                foreach (InternalModel current in PartLoader.Instance.internalParts)
                {
                    if (current.internalName == name)
                    {
                        internalModel = current;
                        break;
                    }
                }
            }
            return internalModel != null && internalModel.seats != null && internalModel.seats.Count > 0;
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

