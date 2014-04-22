using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselWorker
    {
        //Parent/client
        public bool enabled;
        private Client parent;
        //Update frequency
        private const float VESSEL_PROTOVESSEL_UPDATE_INTERVAL = 30f;
        private const float VESSEL_POSITION_UPDATE_INTERVAL = .2f;
        //Spectate stuff
        public const ControlTypes BLOCK_ALL_CONTROLS = ControlTypes.ALL_SHIP_CONTROLS | ControlTypes.ACTIONS_ALL | ControlTypes.EVA_INPUT | ControlTypes.TIMEWARP | ControlTypes.MISC | ControlTypes.GROUPS_ALL | ControlTypes.CUSTOM_ACTION_GROUPS;
        private const string DARK_SPECTATE_LOCK = "DMP_Spectating";
        private const float SPECTATE_MESSAGE_INTERVAL = 1f;
        private ScreenMessage spectateMessage;
        private float lastSpectateMessageUpdate;
        //Incoming queue
        private Queue<ConfigNode> vesselProtoQueue;
        private Queue<VesselUpdate> vesselUpdateQueue;
        private Queue<KerbalEntry> kerbalProtoQueue;
        private Queue<ActiveVesselEntry> newActiveVessels;
        //Vessel state tracking
        private string lastVessel;
        //Known kerbals
        private Dictionary<int, ProtoCrewMember> serverKerbals;
        //Known vessels and last send/receive time
        private Dictionary<string, float> serverVesselsProtoUpdate;
        private Dictionary<string, float> serverVesselsPositionUpdate;
        //Vessel id (key) owned by player (value) - Also read from PlayerStatusWorker
        public Dictionary<string, string> inUse;
        //Track spectating state
        private bool wasSpectating;

        public VesselWorker(Client parent)
        {
            this.parent = parent;
            Reset();
            if (this.parent != null)
            {
                //Shutup compiler
            }
        }
        //Called from main
        public void FixedUpdate()
        {
            if (enabled)
            {
                while (newActiveVessels.Count > 0)
                {
                    ActiveVesselEntry ave = newActiveVessels.Dequeue();
                    if (ave.vesselID != "")
                    {
                        DarkLog.Debug("Player " + ave.player + " is now flying " + ave.vesselID);
                        SetInUse(ave.vesselID, ave.player);
                    }
                    else
                    {
                        DarkLog.Debug("Player " + ave.player + " has released their vessel");
                        SetNotInUse(ave.player);
                    }
                }
                while (kerbalProtoQueue.Count > 0)
                {
                    KerbalEntry kerbalEntry = kerbalProtoQueue.Dequeue();
                    LoadKerbal(kerbalEntry.kerbalID, kerbalEntry.kerbalNode);
                }
                while (vesselProtoQueue.Count > 0)
                {
                    ConfigNode vesselNode = vesselProtoQueue.Dequeue();
                    LoadVessel(vesselNode);
                }
                while (vesselUpdateQueue.Count > 0)
                {
                    VesselUpdate vesselUpdate = vesselUpdateQueue.Dequeue();
                    ApplyVesselUpdate(vesselUpdate);
                }
                bool isActiveVesselOk = FlightGlobals.ActiveVessel != null ? (FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed) : false;
                if ((UnityEngine.Time.realtimeSinceStartup - lastSpectateMessageUpdate) > SPECTATE_MESSAGE_INTERVAL)
                {
                    lastSpectateMessageUpdate = UnityEngine.Time.realtimeSinceStartup;
                    if (isSpectating)
                    {
                        if (spectateMessage != null)
                        {
                            spectateMessage.duration = 0f;
                        }
                        spectateMessage = ScreenMessages.PostScreenMessage("This vessel is controlled by another player...", SPECTATE_MESSAGE_INTERVAL * 2, ScreenMessageStyle.UPPER_CENTER);
                    }
                    else
                    {
                        if (spectateMessage != null)
                        {
                            spectateMessage.duration = 0f;
                            spectateMessage = null;
                        }
                    }
                }
                //Lock and unlock spectate state
                if (isSpectating != wasSpectating)
                {
                    wasSpectating = isSpectating;
                    if (isSpectating)
                    {
                        DarkLog.Debug("Setting spectate lock");
                        InputLockManager.SetControlLock(BLOCK_ALL_CONTROLS, DARK_SPECTATE_LOCK);
                    }
                    else
                    {
                        DarkLog.Debug("Releasing spectate lock");
                        InputLockManager.RemoveControlLock(DARK_SPECTATE_LOCK);
                    }

                }
                //Tell other players we have taken a vessel
                if (!isSpectating && isActiveVesselOk && (HighLogic.LoadedScene == GameScenes.FLIGHT))
                {
                    if (!inUse.ContainsKey(FlightGlobals.ActiveVessel.id.ToString()))
                    {
                        parent.playerStatusWorker.myPlayerStatus.vesselText = "Controlling " + FlightGlobals.ActiveVessel.vesselName;
                        SetInUse(FlightGlobals.ActiveVessel.id.ToString(), parent.settings.playerName);
                        parent.networkWorker.SendActiveVessel(FlightGlobals.ActiveVessel.id.ToString());
                    }
                }
                else
                {
                    if (lastVessel != "")
                    {
                        lastVessel = "";
                        parent.networkWorker.SendActiveVessel("");
                        parent.playerStatusWorker.myPlayerStatus.vesselText = "";
                        SetNotInUse(parent.settings.playerName);
                    }
                }
                //Send updates of needed vessels
                if (isActiveVesselOk && !isSpectating)
                {
                    SendVesselUpdates();
                }
            }
        }

        private void SendVesselUpdates()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                //We aren't in flight so we have nothing to send
                return;
            }
            if (FlightGlobals.ActiveVessel == null)
            {
                //We don't have an active vessel
                return;
            }
            if (!FlightGlobals.ActiveVessel.loaded || FlightGlobals.ActiveVessel.packed)
            {
                //We haven't loaded into the game yet
                return;
            }
            if (isSpectating)
            {
                //Don't send updates in spectate mode
                return;
            }
            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                //Send updates for unpacked vessels that aren't being flown by other players
                bool oursOrNotInUse = inUse.ContainsKey(checkVessel.id.ToString()) ? (inUse[checkVessel.id.ToString()] == parent.settings.playerName) : true;
                bool notRecentlySentProtoUpdate = serverVesselsProtoUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsProtoUpdate[checkVessel.id.ToString()]) > VESSEL_PROTOVESSEL_UPDATE_INTERVAL) : true;
                bool notRecentlySentPositionUpdate = serverVesselsPositionUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsPositionUpdate[checkVessel.id.ToString()]) > VESSEL_POSITION_UPDATE_INTERVAL) : true;
                if (checkVessel.loaded && !checkVessel.packed && oursOrNotInUse)
                {
                    //Check that is hasn't been recently sent
                    if (notRecentlySentProtoUpdate)
                    {
                        //Send a protovessel update
                        serverVesselsProtoUpdate[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        //Also delay the position send
                        serverVesselsPositionUpdate[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        ProtoVessel checkProto = new ProtoVessel(checkVessel);
                        if (checkProto != null)
                        {
                            if (checkProto.vesselID != Guid.Empty)
                            {
                                //Also check for kerbal state changes
                                foreach (ProtoPartSnapshot part in checkProto.protoPartSnapshots)
                                {
                                    foreach (ProtoCrewMember pcm in part.protoModuleCrew)
                                    {
                                        int kerbalID = HighLogic.CurrentGame.CrewRoster.IndexOf(pcm);
                                        if (!serverKerbals.ContainsKey(kerbalID))
                                        {
                                            //New kerbal
                                            DarkLog.Debug("Found new kerbal, sending...");
                                            serverKerbals[kerbalID] = new ProtoCrewMember(pcm);
                                            parent.networkWorker.SendKerbalProtoMessage(HighLogic.CurrentGame.CrewRoster.IndexOf(pcm), pcm);
                                        }
                                        else
                                        {
                                            bool kerbalDifferent = false;
                                            kerbalDifferent = (pcm.name != serverKerbals[kerbalID].name) || kerbalDifferent;
                                            kerbalDifferent = (pcm.courage != serverKerbals[kerbalID].courage) || kerbalDifferent;
                                            kerbalDifferent = (pcm.isBadass != serverKerbals[kerbalID].isBadass) || kerbalDifferent;
                                            kerbalDifferent = (pcm.rosterStatus != serverKerbals[kerbalID].rosterStatus) || kerbalDifferent;
                                            kerbalDifferent = (pcm.seatIdx != serverKerbals[kerbalID].seatIdx) || kerbalDifferent;
                                            kerbalDifferent = (pcm.stupidity != serverKerbals[kerbalID].stupidity) || kerbalDifferent;
                                            kerbalDifferent = (pcm.UTaR != serverKerbals[kerbalID].UTaR) || kerbalDifferent;
                                            if (kerbalDifferent)
                                            {
                                                DarkLog.Debug("Found changed kerbal, sending...");
                                                parent.networkWorker.SendKerbalProtoMessage(HighLogic.CurrentGame.CrewRoster.IndexOf(pcm), pcm);
                                                serverKerbals[kerbalID].name = pcm.name;
                                                serverKerbals[kerbalID].courage = pcm.courage;
                                                serverKerbals[kerbalID].isBadass = pcm.isBadass;
                                                serverKerbals[kerbalID].rosterStatus = pcm.rosterStatus;
                                                serverKerbals[kerbalID].seatIdx = pcm.seatIdx;
                                                serverKerbals[kerbalID].stupidity = pcm.stupidity;
                                                serverKerbals[kerbalID].UTaR = pcm.UTaR;
                                            }
                                        }
                                    }
                                }
                                parent.networkWorker.SendVesselProtoMessage(checkProto);
                            }
                            else
                            {
                                DarkLog.Debug(checkVessel.vesselName + " does not have a guid!");
                            }
                        }
                        else
                        {
                            DarkLog.Debug("Failed to send protovessel for " + checkVessel.id);
                        }
                    }
                    else if (notRecentlySentPositionUpdate)
                    {
                        //Send a position update
                        serverVesselsPositionUpdate[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        bool anotherPlayerCloser = false;
                        //Skip checking player vessels, they are filtered out above in "oursOrNotInUse"
                        if (!inUse.ContainsKey(checkVessel.id.ToString()))
                        {
                            foreach (KeyValuePair<string,string> entry in inUse)
                            {
                                //The active vessel isn't another player that can be closer than the active vessel.
                                if (entry.Value != parent.settings.playerName)
                                {
                                    Vessel playerVessel = FlightGlobals.fetch.vessels.Find(v => v.id.ToString() == entry.Key);
                                    if (playerVessel != null)
                                    {
                                        double ourDistance = Vector3d.Distance(FlightGlobals.ActiveVessel.GetWorldPos3D(), checkVessel.GetWorldPos3D());
                                        double theirDistance = Vector3d.Distance(FlightGlobals.ActiveVessel.GetWorldPos3D(), checkVessel.GetWorldPos3D());
                                        if (ourDistance > theirDistance)
                                        {
                                            DarkLog.Debug("Player " + entry.Value + " is closer to " + entry.Key + ", theirs: " + (float)theirDistance + ", ours: " + (float)ourDistance);
                                            anotherPlayerCloser = true;
                                        }
                                    }
                                }
                            }
                        }
                        if (!anotherPlayerCloser)
                        {
                            VesselUpdate update = GetVesselUpdate(checkVessel);
                            if (update != null)
                            {
                                parent.networkWorker.SendVesselUpdate(update);
                            }
                        }
                    }
                }
            }
        }
        //Also called from PlayerStatusWorker
        public bool isSpectating
        {
            get
            {
                if (FlightGlobals.ActiveVessel != null)
                {
                    if (inUse.ContainsKey(FlightGlobals.ActiveVessel.id.ToString()))
                    {
                        if (inUse[FlightGlobals.ActiveVessel.id.ToString()] != parent.settings.playerName)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        private VesselUpdate GetVesselUpdate(Vessel updateVessel)
        {
            VesselUpdate returnUpdate = new VesselUpdate();
            try
            {
                returnUpdate.vesselID = updateVessel.id.ToString();
                returnUpdate.bodyName = updateVessel.mainBody.bodyName;
                returnUpdate.rotation = new float[4];
                returnUpdate.rotation[0] = updateVessel.transform.localRotation.x;
                returnUpdate.rotation[1] = updateVessel.transform.localRotation.y;
                returnUpdate.rotation[2] = updateVessel.transform.localRotation.z;
                returnUpdate.rotation[3] = updateVessel.transform.localRotation.w;
                //Flight state
                returnUpdate.flightState = new FlightCtrlState();
                returnUpdate.flightState.CopyFrom(updateVessel.ctrlState);
                returnUpdate.actiongroupControls = new bool[5];
                returnUpdate.actiongroupControls[0] = updateVessel.ActionGroups[KSPActionGroup.Gear];
                returnUpdate.actiongroupControls[1] = updateVessel.ActionGroups[KSPActionGroup.Light];
                returnUpdate.actiongroupControls[2] = updateVessel.ActionGroups[KSPActionGroup.Brakes];
                returnUpdate.actiongroupControls[3] = updateVessel.ActionGroups[KSPActionGroup.SAS];
                returnUpdate.actiongroupControls[4] = updateVessel.ActionGroups[KSPActionGroup.RCS];

                if (updateVessel.altitude < 10000)
                {
                    //Use surface position under 10k
                    returnUpdate.isSurfaceUpdate = true;
                    returnUpdate.position = new double[3];
                    returnUpdate.position[0] = updateVessel.latitude;
                    returnUpdate.position[1] = updateVessel.longitude;
                    returnUpdate.position[2] = updateVessel.altitude;
                    returnUpdate.velocity = new double[3];
                    returnUpdate.velocity[0] = updateVessel.srf_velocity.x;
                    returnUpdate.velocity[1] = updateVessel.srf_velocity.y;
                    returnUpdate.velocity[2] = updateVessel.srf_velocity.z;
                }
                else
                {
                    //Use orbital positioning over 10k
                    returnUpdate.isSurfaceUpdate = false;
                    returnUpdate.orbit = new double[7];
                    returnUpdate.orbit[0] = updateVessel.orbit.inclination;
                    returnUpdate.orbit[1] = updateVessel.orbit.eccentricity;
                    returnUpdate.orbit[2] = updateVessel.orbit.semiMajorAxis;
                    returnUpdate.orbit[3] = updateVessel.orbit.LAN;
                    returnUpdate.orbit[4] = updateVessel.orbit.argumentOfPeriapsis;
                    returnUpdate.orbit[5] = updateVessel.orbit.meanAnomalyAtEpoch;
                    returnUpdate.orbit[6] = updateVessel.orbit.epoch;
                }

            }
            catch (Exception e)
            {
                DarkLog.Debug("Failed to get vessel update, exception: " + e);
                returnUpdate = null;
            }
            return returnUpdate;
        }

        private void SetNotInUse(string player)
        {
            string deleteKey = "";
            foreach (KeyValuePair<string, string> inUseEntry in inUse)
            {
                if (inUseEntry.Value == player)
                {
                    deleteKey = inUseEntry.Key;
                }
            }
            if (deleteKey != "")
            {
                inUse.Remove(deleteKey);
            }
        }

        private void SetInUse(string vesselID, string player)
        {
            SetNotInUse(player);
            inUse[vesselID] = player;
        }
        //Called from main
        public void LoadKerbalsIntoGame()
        {

            DarkLog.Debug("Loading " + kerbalProtoQueue.Count + " received kerbals");
            while (kerbalProtoQueue.Count > 0)
            {
                KerbalEntry kerbalEntry = kerbalProtoQueue.Dequeue();
                LoadKerbal(kerbalEntry.kerbalID, kerbalEntry.kerbalNode);
            }

            int generateKerbals = 0;
            if (serverKerbals.Count < 50)
            {
                generateKerbals = 50 - serverKerbals.Count;
                DarkLog.Debug("Generating " + generateKerbals + " new kerbals");
            }

            while (generateKerbals > 0)
            {
                ProtoCrewMember protoKerbal = CrewGenerator.RandomCrewMemberPrototype();
                if (!HighLogic.CurrentGame.CrewRoster.ExistsInRoster(protoKerbal.name))
                {
                    HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoKerbal);
                    int kerbalID = HighLogic.CurrentGame.CrewRoster.IndexOf(protoKerbal); 
                    serverKerbals[kerbalID] = new ProtoCrewMember(protoKerbal);
                    parent.networkWorker.SendKerbalProtoMessage(kerbalID, protoKerbal);
                    generateKerbals--;
                }
            }
        }

        private void LoadKerbal(int kerbalID, ConfigNode crewNode)
        {
            if (crewNode != null)
            {
                ProtoCrewMember protoCrew = new ProtoCrewMember(crewNode);
                if (protoCrew != null)
                {
                    if (!String.IsNullOrEmpty(protoCrew.name))
                    {
                        //Welcome to the world of kludges.
                        bool existsInRoster = true;
                        try {
                            ProtoCrewMember testKerbal = HighLogic.CurrentGame.CrewRoster[kerbalID];
                        }
                        catch {
                            existsInRoster = false;
                        }
                        if (!existsInRoster)
                        {
                            HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoCrew);
                            serverKerbals[kerbalID] = (new ProtoCrewMember(protoCrew));
                        }
                        else
                        {
                            HighLogic.CurrentGame.CrewRoster[kerbalID].courage = protoCrew.courage;
                            HighLogic.CurrentGame.CrewRoster[kerbalID].isBadass = protoCrew.isBadass;
                            HighLogic.CurrentGame.CrewRoster[kerbalID].rosterStatus = protoCrew.rosterStatus;
                            HighLogic.CurrentGame.CrewRoster[kerbalID].seatIdx = protoCrew.seatIdx;
                            HighLogic.CurrentGame.CrewRoster[kerbalID].stupidity = protoCrew.stupidity;
                            HighLogic.CurrentGame.CrewRoster[kerbalID].UTaR = protoCrew.UTaR;
                        }
                    }
                    else
                    {
                        DarkLog.Debug("protoName is blank!");
                    }
                }
                else
                {
                    DarkLog.Debug("protoCrew is null!");
                }
            }
            else
            {
                DarkLog.Debug("crewNode is null!");
            }
        }
        //Called from main
        public void LoadVesselsIntoGame()
        {
            DarkLog.Debug("Loading " + vesselProtoQueue.Count + " vessels");
            while (vesselUpdateQueue.Count > 0)
            {
                ConfigNode currentNode = vesselProtoQueue.Dequeue();
                LoadVessel(currentNode);
            }
        }

        private void LoadVessel(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                ProtoVessel currentProto = new ProtoVessel(vesselNode, HighLogic.CurrentGame);
                if (currentProto != null)
                {
                    DarkLog.Debug("Loading " + currentProto.vesselID + ", name: " + currentProto.vesselName + ", type: " + currentProto.vesselType);
                    bool wasActive = false;
                    bool wasTarget = false;
                    if (HighLogic.LoadedSceneIsFlight)
                    {
                        if (FlightGlobals.activeTarget != null)
                        {
                            wasTarget = (FlightGlobals.activeTarget.vessel != null) ? (FlightGlobals.activeTarget.vessel.id == currentProto.vesselID) : false;
                        }
                        wasActive = (FlightGlobals.ActiveVessel != null) ? (FlightGlobals.ActiveVessel.id == currentProto.vesselID) : false;
                        if (wasActive)
                        {
                            DarkLog.Debug("Temporarily disabling active vessel proto updates");
                            return;
                        }
                    }
                    Vessel oldVessel = null;
                    foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
                    {
                        if (checkVessel.id == currentProto.vesselID)
                        {
                            oldVessel = checkVessel;
                        }
                    }
                    if (oldVessel != null)
                    {
                        DarkLog.Debug("Replacing old vessel, packed: " + oldVessel.packed);
                        if (wasActive)
                        {
                            oldVessel.MakeInactive();
                        }
                        oldVessel.Die();
                    }
                    serverVesselsProtoUpdate[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    currentProto.Load(HighLogic.CurrentGame.flightState);
                    if (currentProto.vesselRef != null)
                    {
                        if (wasActive)
                        {
                            DarkLog.Debug("Set active vessel");
                            currentProto.vesselRef.Load();
                            if (currentProto.vesselRef.packed)
                            {
                                currentProto.vesselRef.GoOffRails();
                            }
                            FlightGlobals.ForceSetActiveVessel(currentProto.vesselRef);
                        }
                        if (wasTarget)
                        {
                            DarkLog.Debug("Set docking target");
                            FlightGlobals.fetch.SetVesselTarget(currentProto.vesselRef);
                        }
                        DarkLog.Debug("Protovessel Loaded");
                    }
                    else
                    {
                        DarkLog.Debug("Protovessel " + currentProto.vesselID + " failed to create a vessel!");
                    }
                }
                else
                {
                    DarkLog.Debug("protoVessel is null!");
                }
            }
            else
            {
                DarkLog.Debug("vesselNode is null!");
            }
        }

        private void ApplyVesselUpdate(VesselUpdate update)
        {
            //Get updating player
            string updatePlayer = inUse.ContainsKey(update.vesselID) ? inUse[update.vesselID] : "Unknown";
            //Ignore updates to our own vessel
            if (!isSpectating && (FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.id.ToString() == update.vesselID : false))
            {
                DarkLog.Debug("ApplyVesselUpdate - Ignoring update for active vessel from " + updatePlayer);
                return;
            }
            Vessel updateVessel = FlightGlobals.fetch.vessels.Find(v => v.id.ToString() == update.vesselID);
            if (updateVessel == null)
            {
                DarkLog.Debug("ApplyVesselUpdate - Got vessel update for " + update.vesselID + " but vessel does not exist");
                return;
            }
            CelestialBody updateBody = FlightGlobals.Bodies.Find(b => b.bodyName == update.bodyName);
            if (updateBody == null)
            {
                DarkLog.Debug("ApplyVesselUpdate - updateBody not found");
                return;
            }
            if (update.isSurfaceUpdate)
            {
                Vector3d updatePostion = updateBody.GetWorldSurfacePosition(update.position[0], update.position[1], update.position[2]);
                Vector3d updateVelocity = new Vector3d(update.velocity[0], update.velocity[1], update.velocity[2]);
                Vector3d velocityOffset = updateVelocity - updateVessel.srf_velocity;
                updateVessel.SetPosition(updatePostion);
                updateVessel.ChangeWorldVelocity(velocityOffset);
            }
            else
            {
                Orbit updateOrbit = new Orbit(update.orbit[0], update.orbit[1], update.orbit[2], update.orbit[3], update.orbit[4], update.orbit[5], update.orbit[6], updateBody);
                CopyOrbit(updateOrbit, updateVessel.orbitDriver.orbit);
            }
            Quaternion updateRotation = new Quaternion(update.rotation[0], update.rotation[1], update.rotation[2], update.rotation[3]);
            updateVessel.SetRotation(updateRotation);
            updateVessel.angularVelocity = Vector3.zero;
            if (!isSpectating)
            {
                updateVessel.ctrlState.CopyFrom(update.flightState);
            }
            else
            {
                FlightInputHandler.state.CopyFrom(update.flightState);
            }
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.Gear, update.actiongroupControls[0]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.Light, update.actiongroupControls[1]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, update.actiongroupControls[2]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, update.actiongroupControls[3]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.RCS, update.actiongroupControls[4]);
        }
        //Credit where credit is due, Thanks hyperedit.
        private void CopyOrbit(Orbit sourceOrbit, Orbit destinationOrbit)
        {
            destinationOrbit.inclination = sourceOrbit.inclination;
            destinationOrbit.eccentricity = sourceOrbit.eccentricity;
            destinationOrbit.semiMajorAxis = sourceOrbit.semiMajorAxis;
            destinationOrbit.LAN = sourceOrbit.LAN;
            destinationOrbit.argumentOfPeriapsis = sourceOrbit.argumentOfPeriapsis;
            destinationOrbit.meanAnomalyAtEpoch = sourceOrbit.meanAnomalyAtEpoch;
            destinationOrbit.epoch = sourceOrbit.epoch;
            destinationOrbit.referenceBody = sourceOrbit.referenceBody;
        }
        //Called from networkWorker
        public void QueueKerbal(int kerbalID, ConfigNode kerbalNode)
        {
            KerbalEntry newEntry = new KerbalEntry();
            newEntry.kerbalID = kerbalID;
            newEntry.kerbalNode = kerbalNode;
            kerbalProtoQueue.Enqueue(newEntry);
        }
        //Called from networkWorker
        public void QueueVesselProto(ConfigNode vesselNode)
        {
            vesselProtoQueue.Enqueue(vesselNode);
        }

        public void QueueVesselUpdate(VesselUpdate update)
        {
            vesselUpdateQueue.Enqueue(update);
        }

        public void QueueActiveVessel(string player, string vesselID)
        {
            ActiveVesselEntry ave = new ActiveVesselEntry();
            ave.player = player;
            ave.vesselID = vesselID;
            newActiveVessels.Enqueue(ave);
        }
        //Called from main
        public void Reset()
        {
            enabled = false;
            newActiveVessels = new Queue<ActiveVesselEntry>();
            kerbalProtoQueue = new Queue<KerbalEntry>();
            vesselProtoQueue = new Queue<ConfigNode>();
            vesselUpdateQueue = new Queue<VesselUpdate>();
            serverKerbals = new Dictionary<int, ProtoCrewMember>();
            serverVesselsProtoUpdate = new Dictionary<string, float>();
            serverVesselsPositionUpdate = new Dictionary<string, float>();
            inUse = new Dictionary<string, string>();
            lastVessel = "";
        }
    }

    class ActiveVesselEntry
    {
        public string player;
        public string vesselID;
    }

    class KerbalEntry
    {
        public int kerbalID;
        public ConfigNode kerbalNode;
    }

    public class VesselUpdate
    {
        public string vesselID;
        public string bodyName;
        public float[] rotation;
        public FlightCtrlState flightState;
        public bool[] actiongroupControls;
        public bool isSurfaceUpdate;
        //Orbital parameters
        public double[] orbit;
        //Surface parameters
        //Position = lat,long,alt.
        public double[] position;
        public double[] velocity;
    }
}

