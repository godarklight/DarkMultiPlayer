using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

namespace DarkMultiPlayer
{
    public class VesselWorker
    {
        //Parent/client
        public bool enabled;
        //Hooks inserted into KSP
        private bool registered;
        //Daddy?
        private Client parent;
        //Update frequency
        private const float VESSEL_PROTOVESSEL_UPDATE_INTERVAL = 30f;
        private const float VESSEL_POSITION_UPDATE_INTERVAL = .2f;
        private const float IGNORE_TIME_AFTER_SCENE_CHANGE = 10f;
        //Spectate stuff
        public const ControlTypes BLOCK_ALL_CONTROLS = ControlTypes.ALL_SHIP_CONTROLS | ControlTypes.ACTIONS_ALL | ControlTypes.EVA_INPUT | ControlTypes.TIMEWARP | ControlTypes.MISC | ControlTypes.GROUPS_ALL | ControlTypes.CUSTOM_ACTION_GROUPS;
        private const string DARK_SPECTATE_LOCK = "DMP_Spectating";
        private const float SPECTATE_MESSAGE_INTERVAL = 1f;
        private ScreenMessage spectateMessage;
        private float lastSpectateMessageUpdate;
        //Incoming queue
        private object createSubspaceLock = new object();
        private Dictionary<int, Queue<VesselRemoveEntry>> vesselRemoveQueue;
        private Dictionary<int, Queue<VesselProtoUpdate>> vesselProtoQueue;
        private Dictionary<int, Queue<VesselUpdate>> vesselUpdateQueue;
        private Dictionary<int, Queue<KerbalEntry>> kerbalProtoQueue;
        private Queue<ActiveVesselEntry> newActiveVessels;
        //Vessel state tracking
        private string lastVessel;
        //Known kerbals
        private Dictionary<int, ProtoCrewMember> serverKerbals;
        public Dictionary<int, string> assignedKerbals;
        //Known vessels and last send/receive time
        private Dictionary<string, float> serverVesselsProtoUpdate;
        private Dictionary<string, float> serverVesselsPositionUpdate;
        //Vessel id (key) owned by player (value) - Also read from PlayerStatusWorker
        public Dictionary<string, string> inUse;
        //Track spectating state
        private bool wasSpectating;
        //Ignore false destroys
        private double sceneChangeTime;

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
            //Hooks
            if (enabled && !registered)
            {
                //GameEvents.debugEvents = true;
                registered = true;
                GameEvents.onVesselChange.Add(OnVesselChange);
                GameEvents.onVesselDestroy.Add(OnVesselDestroy);
                GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
                GameEvents.onVesselRecovered.Add(OnVesselRecovered);
                GameEvents.onVesselTerminated.Add(OnVesselTerminated);
                GameEvents.onVesselWasModified.Add(OnVesselWasModified);
                /*
                GameEvents.onCollision.Add(OnVesselCollision);
                GameEvents.onCrash.Add(OnVesselCrash);
                GameEvents.onCrashSplashdown.Add(OnVesselCrashSplashdown);
                */
            }
            if (!enabled && registered)
            {
                registered = false;
                GameEvents.onVesselChange.Remove(OnVesselChange);
                GameEvents.onVesselDestroy.Remove(OnVesselDestroy);
                GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
                //GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
                GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
                GameEvents.onVesselTerminated.Remove(OnVesselTerminated);
                GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
                /*
                GameEvents.onCollision.Remove(OnVesselCollision);
                GameEvents.onCrash.Remove(OnVesselCrash);
                GameEvents.onCrashSplashdown.Add(OnVesselCrashSplashdown);
                */
            }
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
                lock (createSubspaceLock)
                {
                    foreach (KeyValuePair<int, Queue<VesselRemoveEntry>> vesselRemoveSubspace in vesselRemoveQueue)
                    {
                        while (vesselRemoveSubspace.Value.Count > 0 ? (vesselRemoveSubspace.Value.Peek().planetTime < Planetarium.GetUniversalTime()) : false)
                        {
                            VesselRemoveEntry removeVessel = vesselRemoveSubspace.Value.Dequeue();
                            RemoveVessel(removeVessel.vesselID);
                        }
                    }
                    foreach (KeyValuePair<int, Queue<KerbalEntry>> kerbalProtoSubspace in kerbalProtoQueue)
                    {
                        while (kerbalProtoSubspace.Value.Count > 0 ? (kerbalProtoSubspace.Value.Peek().planetTime < Planetarium.GetUniversalTime()) : false)
                        {
                            KerbalEntry kerbalEntry = kerbalProtoSubspace.Value.Dequeue();
                            LoadKerbal(kerbalEntry.kerbalID, kerbalEntry.kerbalNode);
                        }
                    }
                    foreach (KeyValuePair<int, Queue<VesselProtoUpdate>> vesselProtoSubspace in vesselProtoQueue)
                    {
                        while (vesselProtoSubspace.Value.Count > 0 ? (vesselProtoSubspace.Value.Peek().planetTime < Planetarium.GetUniversalTime()) : false)
                        {
                            VesselProtoUpdate vesselNode = vesselProtoSubspace.Value.Dequeue();
                            LoadVessel(vesselNode.vesselNode);
                        }
                    }
                    foreach (KeyValuePair<int, Queue<VesselUpdate>> vesselUpdateSubspace in vesselUpdateQueue)
                    {
                        while (vesselUpdateSubspace.Value.Count > 0 ? (vesselUpdateSubspace.Value.Peek().planetTime < Planetarium.GetUniversalTime()) : false)
                        {
                            VesselUpdate vesselUpdate = vesselUpdateSubspace.Value.Dequeue();
                            ApplyVesselUpdate(vesselUpdate);
                        }
                    }
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
                        parent.playerStatusWorker.myPlayerStatus.vesselText = FlightGlobals.ActiveVessel.vesselName;
                        SetInUse(FlightGlobals.ActiveVessel.id.ToString(), parent.settings.playerName);
                        parent.networkWorker.SendActiveVessel(FlightGlobals.ActiveVessel.id.ToString());
                        lastVessel = FlightGlobals.ActiveVessel.id.ToString();
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
        //Game hooks
        public void OnVesselChange(Vessel vessel)
        {
            DarkLog.Debug("Vessel " + vessel.id + " reported as changed");
        }
        /*
        public void OnVesselCollision(EventReport report)
        {
            DarkLog.Debug("Vessel " + report.origin.vessel.id + " reported as collided");
        }

        public void OnVesselCrash(EventReport report)
        {
            DarkLog.Debug("Vessel " + report.origin.vessel.id + " reported as crashed");
        }

        public void OnVesselCrashSplashdown(EventReport report)
        {
            DarkLog.Debug("Vessel " + report.origin.vessel.id + " reported as crashed + splashdown");
        }
        */
        public void OnVesselDestroy(Vessel vessel)
        {
            try
            {
                DarkLog.Debug("Vessel " + vessel.id + " reported as destroyed, loaded: " + vessel.loaded + ", packed: " + vessel.packed);
                if (!inUse.ContainsKey(vessel.id.ToString()) && ((UnityEngine.Time.realtimeSinceStartup - sceneChangeTime) > IGNORE_TIME_AFTER_SCENE_CHANGE))
                {
                    parent.networkWorker.SendVesselRemove(vessel.id.ToString());
                    List<int> unassignKerbals = new List<int>();
                    foreach (KeyValuePair<int,string> kerbalAssignment in assignedKerbals)
                    {
                        if (kerbalAssignment.Value == vessel.id.ToString().Replace("-", ""))
                        {
                            DarkLog.Debug("Kerbal " + kerbalAssignment.Key + " unassigned from " + vessel.id + ", name: " + vessel.vesselName);
                            unassignKerbals.Add(kerbalAssignment.Key);
                        }
                    }
                    foreach (int unassignKerbal in unassignKerbals)
                    {
                        assignedKerbals.Remove(unassignKerbal);
                    }
                }
                else
                {
                    DarkLog.Debug("Ignored destroy event - In use or scene recently changed!");
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("OnVesselDestroy threw exception: " + e);
            }
        }

        public void OnGameSceneLoadRequested(GameScenes scene)
        {
            sceneChangeTime = UnityEngine.Time.realtimeSinceStartup;
        }

        public void OnVesselWasModified(Vessel vessel)
        {
            try
            {
                DarkLog.Debug("Vessel " + vessel.id + " was modified");
            }
            catch (Exception e)
            {
                DarkLog.Debug("OnVesselWasModified threw exception: " + e);
            }
        }

        public void OnVesselRecovered(ProtoVessel vessel)
        {
            try
            {
                DarkLog.Debug("Vessel " + vessel.vesselID + " reported as recovered");
                parent.networkWorker.SendVesselRemove(vessel.vesselID.ToString());
            }
            catch (Exception e)
            {
                DarkLog.Debug("OnVesselRecovered threw exception: " + e);
            }
        }

        public void OnVesselTerminated(ProtoVessel vessel)
        {
            try
            {
                DarkLog.Debug("Vessel " + vessel.vesselID + " reported as terminated");
                parent.networkWorker.SendVesselRemove(vessel.vesselID.ToString());
            }
            catch (Exception e)
            {
                DarkLog.Debug("OnVesselRecovered threw exception: " + e);
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
                returnUpdate.planetTime = Planetarium.GetUniversalTime();
                returnUpdate.bodyName = updateVessel.mainBody.bodyName;
                returnUpdate.rotation = new float[4];
                returnUpdate.rotation[0] = updateVessel.transform.localRotation.x;
                returnUpdate.rotation[1] = updateVessel.transform.localRotation.y;
                returnUpdate.rotation[2] = updateVessel.transform.localRotation.z;
                returnUpdate.rotation[3] = updateVessel.transform.localRotation.w;
                returnUpdate.angularVelocity = new float[3];
                returnUpdate.angularVelocity[0] = updateVessel.angularVelocity.x;
                returnUpdate.angularVelocity[1] = updateVessel.angularVelocity.y;
                returnUpdate.angularVelocity[2] = updateVessel.angularVelocity.z;
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
            foreach (KeyValuePair<int, Queue<KerbalEntry>> kerbalQueue in kerbalProtoQueue)
            {
                DarkLog.Debug("Loading " + kerbalQueue.Value.Count + " received kerbals from subspace " + kerbalQueue.Key);
                while (kerbalQueue.Value.Count > 0)
                {
                    KerbalEntry kerbalEntry = kerbalQueue.Value.Dequeue();
                    LoadKerbal(kerbalEntry.kerbalID, kerbalEntry.kerbalNode);
                }
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
                        try
                        {
                            ProtoCrewMember testKerbal = HighLogic.CurrentGame.CrewRoster[kerbalID];
                        }
                        catch
                        {
                            existsInRoster = false;
                        }
                        if (!existsInRoster)
                        {
                            HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoCrew);
                            DarkLog.Debug("Loaded kerbal " + kerbalID + ", name: " + protoCrew.name + ", state " + protoCrew.rosterStatus);
                            serverKerbals[kerbalID] = (new ProtoCrewMember(protoCrew));
                        }
                        else
                        {
                            HighLogic.CurrentGame.CrewRoster[kerbalID].name = protoCrew.name;
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
            foreach (KeyValuePair<int, Queue<VesselProtoUpdate>> vesselQueue in vesselProtoQueue)
            {
                DarkLog.Debug("Loading " + vesselQueue.Value.Count + " vessels from subspace " + vesselQueue.Key);
                while (vesselQueue.Value.Count > 0)
                {
                    ConfigNode currentNode = vesselQueue.Value.Dequeue().vesselNode;
                    LoadVessel(currentNode);
                }
            }
        }
        //Thanks KMP :)
        private void checkProtoNodeCrew(ConfigNode protoNode)
        {
            string protoVesselID = protoNode.GetValue("pid");
            foreach (ConfigNode partNode in protoNode.GetNodes("PART"))
            {
                int currentCrewIndex = 0;
                foreach (string crew in partNode.GetValues("crew"))
                {
                    int crewValue = Convert.ToInt32(crew);
                    DarkLog.Debug("Protovessel: " + protoVesselID + " crew value " + crewValue);
                    if (assignedKerbals.ContainsKey(crewValue) ? assignedKerbals[crewValue] != protoVesselID : false)
                    {
                        DarkLog.Debug("Kerbal taken!");
                        if (assignedKerbals[crewValue] != protoVesselID)
                        {
                            //Assign a new kerbal, this one already belongs to another ship.
                            int freeKerbal = 0;
                            while (assignedKerbals.ContainsKey(freeKerbal))
                            {
                                freeKerbal++;
                            }
                            partNode.SetValue("crew", freeKerbal.ToString(), currentCrewIndex);
                            CheckCrewMemberExists(freeKerbal);
                            HighLogic.CurrentGame.CrewRoster[freeKerbal].rosterStatus = ProtoCrewMember.RosterStatus.ASSIGNED;
                            HighLogic.CurrentGame.CrewRoster[freeKerbal].seatIdx = currentCrewIndex;
                            DarkLog.Debug("Fixing duplicate kerbal reference, changing kerbal " + currentCrewIndex + " to " + freeKerbal);
                            crewValue = freeKerbal;
                            assignedKerbals[crewValue] = protoVesselID;
                            currentCrewIndex++;
                        }
                    }
                    else
                    {
                        assignedKerbals[crewValue] = protoVesselID;
                        CheckCrewMemberExists(crewValue);
                        HighLogic.CurrentGame.CrewRoster[crewValue].rosterStatus = ProtoCrewMember.RosterStatus.ASSIGNED;
                        HighLogic.CurrentGame.CrewRoster[crewValue].seatIdx = currentCrewIndex;
                    }
                    crewValue++;
                }
            }   
        }
        //Again - KMP :)
        private void CheckCrewMemberExists(int kerbalID)
        {
            IEnumerator<ProtoCrewMember> crewEnum = HighLogic.CurrentGame.CrewRoster.GetEnumerator();
            int applicants = 0;
            while (crewEnum.MoveNext())
            {
                if (crewEnum.Current.rosterStatus == ProtoCrewMember.RosterStatus.AVAILABLE)
                    applicants++;
            }
        }

        private void LoadVessel(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                //Fix the kerbals (Tracking station bug)
                checkProtoNodeCrew(vesselNode);

                //vesselNode.Save(Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("DMP-RX", Planetarium.GetUniversalTime() + ".txt")));

                ProtoVessel currentProto = new ProtoVessel(vesselNode, HighLogic.CurrentGame);
                if (currentProto != null)
                {
                    DarkLog.Debug("Loading " + currentProto.vesselID + ", name: " + currentProto.vesselName + ", type: " + currentProto.vesselType);

                    //Skip active vessel
                    if (FlightGlobals.fetch.activeVessel != null ? FlightGlobals.fetch.activeVessel.id.ToString() == currentProto.vesselID.ToString() : false)
                    {
                        DarkLog.Debug("Updating the active vessel is currently not implmented, skipping!");
                        return;
                    }


                    foreach (ProtoPartSnapshot part in currentProto.protoPartSnapshots)
                    {
                        //This line doesn't actually do anything useful, but if you get this reference, you're officially the most geeky person darklight knows.
                        part.temperature = ((part.temperature + 273.15f) * 0.8f) - 273.15f;
                    }

                    bool wasActive = false;
                    bool wasTarget = false;
                    Vector3d activeVesselPos = Vector3d.zero;
                    Vector3d activeVesselVel = Vector3d.zero;

                    if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                    {
                        if (FlightGlobals.fetch.VesselTarget != null ? FlightGlobals.fetch.VesselTarget.GetVessel() : false)
                        {
                            wasTarget = FlightGlobals.fetch.VesselTarget.GetVessel().id == currentProto.vesselID;
                        }
                        if (wasTarget)
                        {
                            DarkLog.Debug("ProtoVessel update for target vessel!");
                        }
                        wasActive = (FlightGlobals.ActiveVessel != null) ? (FlightGlobals.ActiveVessel.id == currentProto.vesselID) : false;
                        if (wasActive)
                        {
                            try
                            {
                                OrbitPhysicsManager.HoldVesselUnpack(100);
                            }
                            catch
                            {
                                //Throw out the update, HVU shouldn't throw if the game is running properly.
                                return;
                            }
                            activeVesselPos = FlightGlobals.ActiveVessel.transform.position;
                            activeVesselVel = FlightGlobals.ActiveVessel.srf_velocity;
                            DarkLog.Debug("ProtoVessel update for active vessel!");
                        }
                    }

                    serverVesselsProtoUpdate[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    currentProto.Load(HighLogic.CurrentGame.flightState);

                    if (currentProto.vesselRef != null)
                    {
                        if (wasActive)
                        {
                            DarkLog.Debug("Set active vessel");
                            DarkLog.Debug("Vessel SRF: " + (Vector3)currentProto.vesselRef.srf_velocity);
                            DarkLog.Debug("Vessel OBT: " + (Vector3)currentProto.vesselRef.orbit.vel);
                            currentProto.vesselRef.transform.position = activeVesselPos;
                            currentProto.vesselRef.ChangeWorldVelocity(activeVesselVel - currentProto.vesselRef.srf_velocity);
                            DarkLog.Debug("Vessel SRF: " + (Vector3)currentProto.vesselRef.srf_velocity);
                            DarkLog.Debug("Vessel OBT: " + (Vector3)currentProto.vesselRef.orbit.vel);
                            FlightGlobals.ForceSetActiveVessel(currentProto.vesselRef);
                        }
                        if (wasTarget)
                        {
                            DarkLog.Debug("Set docking target");
                            FlightGlobals.fetch.SetVesselTarget(currentProto.vesselRef);
                        }
                        DarkLog.Debug("Protovessel Loaded");

                        for (int vesselID = FlightGlobals.fetch.vessels.Count - 1; vesselID >= 0; vesselID--)
                        {
                            DarkLog.Debug("Checking vessel " + vesselID);
                            Vessel oldVessel = FlightGlobals.fetch.vessels[vesselID];
                            if (oldVessel.id == currentProto.vesselID && oldVessel != currentProto.vesselRef)
                            {
                                DarkLog.Debug("Killing " + vesselID);
                                if (!oldVessel.packed)
                                {
                                    oldVessel.GoOnRails();
                                }
                                oldVessel.Unload();
                                oldVessel.Die();
                            }
                        }

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

        private void RemoveVessel(string vesselID)
        {
            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                if (checkVessel.id.ToString() == vesselID)
                {
                    if (FlightGlobals.fetch.activeVessel != null ? FlightGlobals.fetch.activeVessel.id.ToString() == checkVessel.id.ToString() : false)
                    {
                        if (!isSpectating)
                        {
                            //Got a remove message for our vessel, reset the send time on our vessel so we send it back.
                            DarkLog.Debug("Resending vessel, our vessel was removed by another player");
                            serverVesselsProtoUpdate[vesselID] = 0f;
                        }
                    }
                    else
                    {
                        DarkLog.Debug("Removing vessel, packed: " + checkVessel.packed);
                        checkVessel.Die();
                    }
                }
            }
        }

        private void ApplyVesselUpdate(VesselUpdate update)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                return;
            }
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
                //DarkLog.Debug("ApplyVesselUpdate - Got vessel update for " + update.vesselID + " but vessel does not exist");
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
                double updateDistance = Double.PositiveInfinity;
                if ((HighLogic.LoadedScene == GameScenes.FLIGHT) && (FlightGlobals.fetch.activeVessel != null))
                {
                    updateDistance = Vector3.Distance(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), updateVessel.GetWorldPos3D());
                }
                bool isUnpacking = (updateDistance < updateVessel.distanceUnpackThreshold) && updateVessel.packed;
                if (!updateVessel.packed && !isUnpacking)
                {
                    Vector3d updatePostion = updateBody.GetWorldSurfacePosition(update.position[0], update.position[1], update.position[2]);
                    Vector3d updateVelocity = new Vector3d(update.velocity[0], update.velocity[1], update.velocity[2]);
                    Vector3d velocityOffset = updateVelocity - updateVessel.srf_velocity;
                    updateVessel.SetPosition(updatePostion);
                    updateVessel.ChangeWorldVelocity(velocityOffset);
                }
            }
            else
            {
                Orbit updateOrbit = new Orbit(update.orbit[0], update.orbit[1], update.orbit[2], update.orbit[3], update.orbit[4], update.orbit[5], update.orbit[6], updateBody);
                CopyOrbit(updateOrbit, updateVessel.orbitDriver.orbit);
            }
            Quaternion updateRotation = new Quaternion(update.rotation[0], update.rotation[1], update.rotation[2], update.rotation[3]);
            updateVessel.SetRotation(updateRotation);
            if (!updateVessel.packed)
            {
                updateVessel.angularVelocity = new Vector3(update.angularVelocity[0], update.angularVelocity[1], update.angularVelocity[2]);
            }
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
        public void QueueKerbal(int subspace, double planetTime, int kerbalID, ConfigNode kerbalNode)
        {
            KerbalEntry newEntry = new KerbalEntry();
            newEntry.kerbalID = kerbalID;
            newEntry.planetTime = planetTime;
            newEntry.kerbalNode = kerbalNode;
            if (!vesselRemoveQueue.ContainsKey(subspace))
            {
                SetupSubspace(subspace);
            }
            kerbalProtoQueue[subspace].Enqueue(newEntry);
        }
        //Called from networkWorker
        public void QueueVesselRemove(int subspace, double planetTime, string vesselID)
        {
            if (!vesselRemoveQueue.ContainsKey(subspace))
            {
                SetupSubspace(subspace);
            }
            VesselRemoveEntry vre = new VesselRemoveEntry();
            vre.planetTime = planetTime;
            vre.vesselID = vesselID;
            vesselRemoveQueue[subspace].Enqueue(vre);
        }

        public void QueueVesselProto(int subspace, double planetTime, ConfigNode vesselNode)
        {
            if (!vesselProtoQueue.ContainsKey(subspace))
            {
                SetupSubspace(subspace);
            }
            VesselProtoUpdate vpu = new VesselProtoUpdate();
            vpu.planetTime = planetTime;
            vpu.vesselNode = vesselNode;
            vesselProtoQueue[subspace].Enqueue(vpu);
        }

        public void QueueVesselUpdate(int subspace, VesselUpdate update)
        {
            if (!vesselUpdateQueue.ContainsKey(subspace))
            {
                SetupSubspace(subspace);
            }
            vesselUpdateQueue[subspace].Enqueue(update);
        }

        public void QueueActiveVessel(string player, string vesselID)
        {
            ActiveVesselEntry ave = new ActiveVesselEntry();
            ave.player = player;
            ave.vesselID = vesselID;
            newActiveVessels.Enqueue(ave);
        }

        private void SetupSubspace(int subspaceID)
        {
            lock (createSubspaceLock)
            {
                kerbalProtoQueue.Add(subspaceID, new Queue<KerbalEntry>());
                vesselRemoveQueue.Add(subspaceID, new Queue<VesselRemoveEntry>());
                vesselProtoQueue.Add(subspaceID, new Queue<VesselProtoUpdate>());
                vesselUpdateQueue.Add(subspaceID, new Queue<VesselUpdate>());
            }
        }
        //Called from main
        public void Reset()
        {
            enabled = false;
            newActiveVessels = new Queue<ActiveVesselEntry>();
            kerbalProtoQueue = new Dictionary<int, Queue<KerbalEntry>>();
            vesselRemoveQueue = new Dictionary<int,Queue<VesselRemoveEntry>>();
            vesselProtoQueue = new Dictionary<int, Queue<VesselProtoUpdate>>();
            vesselUpdateQueue = new Dictionary<int, Queue<VesselUpdate>>();
            serverKerbals = new Dictionary<int, ProtoCrewMember>();
            assignedKerbals = new Dictionary<int, string>();
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

    class VesselRemoveEntry
    {
        public string vesselID;
        public double planetTime;
    }

    class VesselProtoUpdate
    {
        public double planetTime;
        public ConfigNode vesselNode;
    }

    class KerbalEntry
    {
        public int kerbalID;
        public double planetTime;
        public ConfigNode kerbalNode;
    }

    public class VesselUpdate
    {
        public string vesselID;
        public double planetTime;
        public string bodyName;
        public float[] rotation;
        public float[] angularVelocity;
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

