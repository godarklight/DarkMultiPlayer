using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselWorker
    {
        //Parent/client
        public bool workerEnabled;
        //Hooks inserted into KSP
        private bool hooksRegistered;
        //Daddy?
        private Client parent;
        //Update frequency
        private const float VESSEL_PROTOVESSEL_UPDATE_INTERVAL = 30f;
        private const float DESTROY_IGNORE_TIME = 5f;
        //Pack distances
        private const float PLAYER_UNPACK_THRESHOLD = 9000;
        private const float PLAYER_PACK_THRESHOLD = 10000;
        private const float NORMAL_UNPACK_THRESHOLD = 300;
        private const float NORMAL_PACK_THRESHOLD = 600;
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
        private Queue<VesselRemoveEntry> checkDestroyedVesselQueue;
        private Queue<ActiveVesselEntry> newActiveVessels;
        private bool killDuplicateActiveVessels;
        //Vessel state tracking
        private string lastVessel;
        private int lastPartCount;
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
        private Dictionary<string, float> lastVesselLoadTime;

        public VesselWorker(Client parent)
        {
            this.parent = parent;
            Reset();
        }
        //Called from main
        public void FixedUpdate()
        {
            //Event hook registration
            if (workerEnabled && !hooksRegistered)
            {
                RegisterWorkerHooks();
                hooksRegistered = true;
            }
            if (!workerEnabled && hooksRegistered)
            {
                UnregsiterWorkerHooks();
                hooksRegistered = false;
            }

            //If we aren't in a DMP game don't do anything.
            if (workerEnabled)
            {
                //Delayed kill of the active vessel replacement.
                if (killDuplicateActiveVessels)
                {
                    KillDuplicateActiveVessels();
                }

                CheckVesselDestructionEvents();

                //State tracking the active players vessels.
                UpdateOtherPlayersActiveVesselStatus();

                //Process new messages
                lock (createSubspaceLock)
                {
                    ProcessNewVesselMessages();
                }

                //Update the screen spectate message.
                UpdateOnScreenSpectateMessage();

                //Lock and unlock spectate state
                UpdateSpectateLock();

                //Tell other players we have taken a vessel
                UpdateActiveVesselStatus();

                //Check current vessel state
                CheckVesselHasChanged();


                //Send updates of needed vessels
                SendVesselUpdates();
            }
        }

        private void RegisterWorkerHooks()
        {
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselDestroy.Add(OnVesselDestroy);
            GameEvents.onVesselRecovered.Add(OnVesselRecovered);
            GameEvents.onVesselTerminated.Add(OnVesselTerminated);
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
        }

        private void UnregsiterWorkerHooks()
        {

            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselDestroy.Remove(OnVesselDestroy);
            GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(OnVesselTerminated);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
        }

        private void UpdateOtherPlayersActiveVesselStatus()
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
        }



        private void ProcessNewVesselMessages()
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

        private void UpdateOnScreenSpectateMessage()
        {
            
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
        }

        private void UpdateSpectateLock()
        {

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
        }

        private void UpdateActiveVesselStatus()
        {
            bool isActiveVesselOk = FlightGlobals.ActiveVessel != null ? (FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed) : false;
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && isActiveVesselOk)
            {
                if (!isSpectating)
                {
                    if (!inUse.ContainsKey(FlightGlobals.ActiveVessel.id.ToString()))
                    {
                        //Nobody else is flying the vessel - let's take it
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
                        //We are still in flight, 
                        lastVessel = "";
                        parent.networkWorker.SendActiveVessel("");
                        parent.playerStatusWorker.myPlayerStatus.vesselText = "";
                        SetNotInUse(parent.settings.playerName);
                    }
                }
            }
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                //Release the vessel if we aren't in flight anymore.
                if (lastVessel != "")
                {
                    lastVessel = "";
                    parent.networkWorker.SendActiveVessel("");
                    parent.playerStatusWorker.myPlayerStatus.vesselText = "";
                    SetNotInUse(parent.settings.playerName);
                }
            }
        }

        private void UpdatePackDistance(string vesselID)
        {
            foreach (Vessel v in FlightGlobals.fetch.vessels)
            {
                if (v.id.ToString() == vesselID)
                {
                    //Bump other players active vessels
                    if (inUse.ContainsKey(vesselID) ? (inUse[vesselID] != parent.settings.playerName) : false)
                    {
                        v.distanceLandedUnpackThreshold = PLAYER_UNPACK_THRESHOLD;
                        v.distanceLandedPackThreshold = PLAYER_PACK_THRESHOLD;
                        v.distanceUnpackThreshold = PLAYER_UNPACK_THRESHOLD;
                        v.distancePackThreshold = PLAYER_PACK_THRESHOLD;
                    }
                    else
                    {
                        v.distanceLandedUnpackThreshold = NORMAL_UNPACK_THRESHOLD;
                        v.distanceLandedPackThreshold = NORMAL_PACK_THRESHOLD;
                        v.distanceUnpackThreshold = NORMAL_UNPACK_THRESHOLD;
                        v.distancePackThreshold = NORMAL_PACK_THRESHOLD;
                    }
                }
            }
        }

        //Game hooks
        public void OnVesselChange(Vessel vessel)
        {
            DarkLog.Debug("Vessel " + vessel.id + " reported as changed");
        }

        public void OnVesselDestroy(Vessel vessel)
        {
            try
            {
                bool vesselRecentlyLoaded = lastVesselLoadTime.ContainsKey(vessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - lastVesselLoadTime[vessel.id.ToString()]) < DESTROY_IGNORE_TIME) : false;
                bool vesselInUseAndNotMine = inUse.ContainsValue(vessel.id.ToString()) ? (inUse[vessel.id.ToString()] != parent.settings.playerName) : false;

                if (!vesselRecentlyLoaded && !vesselInUseAndNotMine)
                {
                    VesselRemoveEntry vre = new VesselRemoveEntry();
                    vre.planetTime = UnityEngine.Time.realtimeSinceStartup;
                    vre.vesselID = vessel.id.ToString();
                    checkDestroyedVesselQueue.Enqueue(vre);
                }
            }
            catch (Exception e) {
                DarkLog.Debug("OnVesselDestroyed threw exception: " + e);
            }
        }

        private void CheckVesselDestructionEvents()
        {
            while (true)
            {
                if (checkDestroyedVesselQueue.Count == 0)
                {
                    break;
                }
                if ((UnityEngine.Time.realtimeSinceStartup - checkDestroyedVesselQueue.Peek().planetTime) > DESTROY_IGNORE_TIME)
                {
                    CheckVesselDestroy(checkDestroyedVesselQueue.Dequeue().vesselID);
                }
                else
                {
                    break;
                }
            }
        }

        private void CheckVesselDestroy(string vesselID)
        {
            if (!FlightGlobals.fetch.vessels.Exists(v => v.id.ToString() == vesselID))
            {
                DarkLog.Debug("Vessel " + vesselID + " was destroyed!");
                parent.networkWorker.SendVesselRemove(vesselID);
                List<int> unassignKerbals = new List<int>();
                foreach (KeyValuePair<int,string> kerbalAssignment in assignedKerbals)
                {
                    if (kerbalAssignment.Value == vesselID.Replace("-", ""))
                    {
                        DarkLog.Debug("Kerbal " + kerbalAssignment.Key + " unassigned from " + vesselID);
                        unassignKerbals.Add(kerbalAssignment.Key);
                    }
                }
                foreach (int unassignKerbal in unassignKerbals)
                {
                    assignedKerbals.Remove(unassignKerbal);
                }
            }
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

        private void CheckVesselHasChanged()
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.fetch.activeVessel != null)
            {
                if (!isSpectating && FlightGlobals.fetch.activeVessel.loaded && !FlightGlobals.fetch.activeVessel.packed)
                {
                    if (FlightGlobals.fetch.activeVessel.parts.Count != lastPartCount)
                    {
                        lastPartCount = FlightGlobals.fetch.activeVessel.parts.Count;
                        serverVesselsProtoUpdate[FlightGlobals.fetch.activeVessel.id.ToString()] = 0;
                    }
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
                bool notRecentlySentPositionUpdate = serverVesselsPositionUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsPositionUpdate[checkVessel.id.ToString()]) > (1f / (float)parent.dynamicTickWorker.sendTickRate)) : true;
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
                        //TODO: Fix sending of flying vessels.
                        if (checkProto != null && (checkProto.situation != Vessel.Situations.FLYING))
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
                if (FlightGlobals.fetch.activeVessel != null)
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
                /*
                returnUpdate.rotation = new float[4];
                Quaternion transformRotation = updateVessel.transform.rotation;
                returnUpdate.rotation[0] = transformRotation.x;
                returnUpdate.rotation[1] = transformRotation.y;
                returnUpdate.rotation[2] = transformRotation.z;
                returnUpdate.rotation[3] = transformRotation.w;
                */
                returnUpdate.vesselForward = new float[3];
                Vector3 transformVesselForward = updateVessel.mainBody.transform.InverseTransformDirection(updateVessel.transform.forward);
                returnUpdate.vesselForward[0] = transformVesselForward.x;
                returnUpdate.vesselForward[1] = transformVesselForward.y;
                returnUpdate.vesselForward[2] = transformVesselForward.z;

                returnUpdate.vesselUp = new float[3];
                Vector3 transformVesselUp = updateVessel.mainBody.transform.InverseTransformDirection(updateVessel.transform.up);
                returnUpdate.vesselUp[0] = transformVesselUp.x;
                returnUpdate.vesselUp[1] = transformVesselUp.y;
                returnUpdate.vesselUp[2] = transformVesselUp.z;

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
        //Also called from QuickSaveLoader
        public void LoadVessel(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                //Fix the kerbals (Tracking station bug)
                checkProtoNodeCrew(vesselNode);

                //Can be used for debugging incoming vessel config nodes.
                //vesselNode.Save(Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("DMP-RX", Planetarium.GetUniversalTime() + ".txt")));

                ProtoVessel currentProto = new ProtoVessel(vesselNode, HighLogic.CurrentGame);
                if (currentProto != null)
                {
                    DarkLog.Debug("Loading " + currentProto.vesselID + ", name: " + currentProto.vesselName + ", type: " + currentProto.vesselType);

                    //Skip active vessel
                    /*
                    if (FlightGlobals.fetch.activeVessel != null ? FlightGlobals.fetch.activeVessel.id.ToString() == currentProto.vesselID.ToString() : false)
                    {
                        DarkLog.Debug("Updating the active vessel is currently not implmented, skipping!");
                        return;
                    }
                    */

                    foreach (ProtoPartSnapshot part in currentProto.protoPartSnapshots)
                    {
                        //This line doesn't actually do anything useful, but if you get this reference, you're officially the most geeky person darklight knows.
                        part.temperature = ((part.temperature + 273.15f) * 0.8f) - 273.15f;
                    }

                    bool wasActive = false;
                    bool wasTarget = false;

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
                            DarkLog.Debug("ProtoVessel update for active vessel!");
                        }
                    }

                    serverVesselsProtoUpdate[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    lastVesselLoadTime[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    currentProto.Load(HighLogic.CurrentGame.flightState);

                    if (currentProto.vesselRef != null)
                    {
                        UpdatePackDistance(currentProto.vesselRef.id.ToString());

                        if (wasActive)
                        {
                            DarkLog.Debug("Set active vessel");
                            FlightGlobals.ForceSetActiveVessel(currentProto.vesselRef);
                            /*
                            if (!currentProto.vesselRef.loaded)
                            {
                                currentProto.vesselRef.Load();
                            }
                            */
                        }
                        if (wasTarget)
                        {
                            DarkLog.Debug("Set docking target");
                            FlightGlobals.fetch.SetVesselTarget(currentProto.vesselRef);
                        }
                        DarkLog.Debug("Protovessel Loaded");

                        for (int vesselID = FlightGlobals.fetch.vessels.Count - 1; vesselID >= 0; vesselID--)
                        {
                            Vessel oldVessel = FlightGlobals.fetch.vessels[vesselID];
                            if (oldVessel.id == currentProto.vesselID && oldVessel != currentProto.vesselRef)
                            {
                                if (!wasActive)
                                {
                                    KillVessel(oldVessel);
                                }
                                else
                                {
                                    killDuplicateActiveVessels = true;
                                }
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

        private void KillDuplicateActiveVessels()
        {
            killDuplicateActiveVessels = false;
            if (FlightGlobals.fetch.activeVessel != null)
            {
                string killID = FlightGlobals.fetch.activeVessel.id.ToString();
                if (FlightGlobals.fetch.activeVessel != null)
                {
                    for (int vesselID = FlightGlobals.fetch.vessels.Count - 1; vesselID >= 0; vesselID--)
                    {
                        Vessel oldVessel = FlightGlobals.fetch.vessels[vesselID];
                        if (oldVessel.id.ToString() == killID && oldVessel != FlightGlobals.fetch.activeVessel)
                        {
                            KillVessel(oldVessel);
                        }
                    }
                }
            }
        }

        private void KillVessel(Vessel killVessel)
        {
            if (killVessel != null)
            {
                if (!killVessel.packed)
                {
                    killVessel.GoOnRails();
                }
                killVessel.Unload();
                killVessel.Die();
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

                if (updateVessel.packed)
                {
                    CopyOrbit(updateOrbit, updateVessel.orbitDriver.orbit);
                }
                else
                {
                    updateVessel.SetPosition(updateOrbit.getPositionAtUT(Planetarium.GetUniversalTime()));
                    Vector3d velocityOffset = updateOrbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime()).xzy - updateVessel.orbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime()).xzy;
                    updateVessel.ChangeWorldVelocity(velocityOffset);
                }

            }
            //Quaternion updateRotation = new Quaternion(update.rotation[0], update.rotation[1], update.rotation[2], update.rotation[3]);
            //updateVessel.SetRotation(updateRotation);

            Vector3 vesselForward = new Vector3(update.vesselForward[0], update.vesselForward[1], update.vesselForward[2]);
            Vector3 vesselUp = new Vector3(update.vesselUp[0], update.vesselUp[1], update.vesselUp[2]);

            updateVessel.transform.LookAt(updateVessel.transform.position + updateVessel.mainBody.transform.TransformDirection(vesselForward).normalized, updateVessel.mainBody.transform.TransformDirection(vesselUp));
            updateVessel.SetRotation(updateVessel.transform.rotation);

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
            destinationOrbit.Init();
            destinationOrbit.UpdateFromUT(Planetarium.GetUniversalTime());
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
            workerEnabled = false;
            newActiveVessels = new Queue<ActiveVesselEntry>();
            kerbalProtoQueue = new Dictionary<int, Queue<KerbalEntry>>();
            vesselRemoveQueue = new Dictionary<int,Queue<VesselRemoveEntry>>();
            vesselProtoQueue = new Dictionary<int, Queue<VesselProtoUpdate>>();
            vesselUpdateQueue = new Dictionary<int, Queue<VesselUpdate>>();
            serverKerbals = new Dictionary<int, ProtoCrewMember>();
            assignedKerbals = new Dictionary<int, string>();
            serverVesselsProtoUpdate = new Dictionary<string, float>();
            serverVesselsPositionUpdate = new Dictionary<string, float>();
            lastVesselLoadTime = new Dictionary<string, float>();
            checkDestroyedVesselQueue = new Queue<VesselRemoveEntry>();
            inUse = new Dictionary<string, string>();
            lastVessel = "";
        }

        public int GetStatistics(string statType) {
            switch (statType)
            {
                case "StoredFutureUpdates":
                    {
                        int futureUpdates = 0;
                        foreach (KeyValuePair<int, Queue<VesselUpdate>> vUQ in vesselUpdateQueue)
                        {
                            futureUpdates += vUQ.Value.Count;
                        }
                        return futureUpdates;
                    }
                case "StoredFutureProtoUpdates":
                    {
                        int futureProtoUpdates = 0;
                        foreach (KeyValuePair<int, Queue<VesselProtoUpdate>> vPQ in vesselProtoQueue)
                        {
                            futureProtoUpdates += vPQ.Value.Count;
                        }
                        return futureProtoUpdates;
                    }
            }
            return 0;
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
        //public float[] rotation;
        public float[] vesselForward;
        public float[] vesselUp;
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

