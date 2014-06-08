using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselWorker
    {
        public bool workerEnabled;
        //Hooks enabled
        private bool registered;
        private static VesselWorker singleton;
        //Update frequency
        private const float VESSEL_PROTOVESSEL_UPDATE_INTERVAL = 30f;
        //Pack distances
        private const float PLAYER_UNPACK_THRESHOLD = 9000;
        private const float PLAYER_PACK_THRESHOLD = 10000;
        private const float NORMAL_UNPACK_THRESHOLD = 300;
        private const float NORMAL_PACK_THRESHOLD = 600;
        private const float SAFETY_BUBBLE_DISTANCE = 100;
        //Spectate stuff
        public const ControlTypes BLOCK_ALL_CONTROLS = ControlTypes.ALL_SHIP_CONTROLS | ControlTypes.ACTIONS_ALL | ControlTypes.EVA_INPUT | ControlTypes.TIMEWARP | ControlTypes.MISC | ControlTypes.GROUPS_ALL | ControlTypes.CUSTOM_ACTION_GROUPS;
        private const string DARK_SPECTATE_LOCK = "DMP_Spectating";
        private const float UPDATE_SCREEN_MESSAGE_INTERVAL = 1f;
        private ScreenMessage spectateMessage;
        private float lastSpectateMessageUpdate;
        private ScreenMessage bannedPartsMessage;
        private string bannedPartsString = "";
        private float lastBannedPartsMessageUpdate;
        private float lastDockingMessageUpdate;
        private ScreenMessage dockingMessage;
        //Incoming queue
        private object updateQueueLock = new object();
        private Dictionary<string, Queue<VesselRemoveEntry>> vesselRemoveQueue = new Dictionary<string, Queue<VesselRemoveEntry>>();
        private Dictionary<string, Queue<VesselProtoUpdate>> vesselProtoQueue = new Dictionary<string, Queue<VesselProtoUpdate>>();
        private Dictionary<string, Queue<VesselUpdate>> vesselUpdateQueue = new Dictionary<string, Queue<VesselUpdate>>();
        private Dictionary<int, Queue<KerbalEntry>> kerbalProtoQueue = new Dictionary<int, Queue<KerbalEntry>>();
        private Queue<ActiveVesselEntry> newActiveVessels = new Queue<ActiveVesselEntry>();
        private List<string> serverVessels = new List<string>();
        private Dictionary<string, bool> vesselPartsOk = new Dictionary<string, bool>();
        //Vessel state tracking
        private string lastVesselID;
        private Dictionary <string, int> vesselPartCount = new Dictionary<string, int>();
        private Dictionary <string, string> vesselNames = new Dictionary<string, string>();
        private Dictionary <string, VesselType> vesselTypes = new Dictionary<string, VesselType>();
        private Dictionary <string, Vessel.Situations> vesselSituations = new Dictionary<string, Vessel.Situations>();
        //Known kerbals
        private Dictionary<int, ProtoCrewMember> serverKerbals = new Dictionary<int, ProtoCrewMember>();
        public Dictionary<int, string> assignedKerbals = new Dictionary<int, string>();
        //Known vessels and last send/receive time
        private Dictionary<string, float> serverVesselsProtoUpdate = new Dictionary<string, float>();
        private Dictionary<string, float> serverVesselsPositionUpdate = new Dictionary<string, float>();
        //Track when the vessel was last controlled.
        private Dictionary<string, double> latestVesselUpdate = new Dictionary<string, double>();
        //Track when we last tried to acquire locks
        private Dictionary<string, double> latestControlLockRequest = new Dictionary<string, double>();
        private Dictionary<string, double> latestUpdateLockRequest = new Dictionary<string, double>();
        private Dictionary<string, double> latestUpdateSent = new Dictionary<string, double>();
        //Track spectating state
        private bool wasSpectating;
        private int spectateType;
        private bool destroyIsValid;
        private Dictionary<string, double> lastKillVesselDestroy = new Dictionary<string, double>();
        private List<Vessel> killVessels = new List<Vessel>();
        private Vessel switchActiveVesselOnNextUpdate;
        private string fromDockedVesselID;
        private string toDockedVesselID;
        private bool sentDockingDestroyUpdate;
        private bool isSpectatorDocking;
        private string spectatorDockingPlayer;
        private string spectatorDockingID;

        public static VesselWorker fetch
        {
            get
            {
                return singleton;
            }
        }
        //Called from main
        public void FixedUpdate()
        {
            //GameEvents.debugEvents = true;
            if (workerEnabled && !registered)
            {
                RegisterGameHooks();
            }
            if (!workerEnabled && registered)
            {
                UnregisterGameHooks();
            }
            //If we aren't in a DMP game don't do anything.
            if (workerEnabled)
            {
                List<Vessel> deleteList = new List<Vessel>();
                foreach (Vessel dyingVessel in killVessels)
                {
                    if (FlightGlobals.fetch.vessels.Contains(dyingVessel))
                    {
                        DarkLog.Debug("Trying to kill " + dyingVessel.id.ToString() + " because of KSP's stupidity!");
                        KillVessel(dyingVessel);
                    }
                    else
                    {
                        deleteList.Add(dyingVessel);
                    }
                }
                foreach (Vessel deadVessel in deleteList)
                {
                    killVessels.Remove(deadVessel);
                }


                //Switch to a new active vessel if needed.
                if (switchActiveVesselOnNextUpdate != null)
                {
                    FlightGlobals.ForceSetActiveVessel(switchActiveVesselOnNextUpdate);
                    switchActiveVesselOnNextUpdate = null;
                }

                if (fromDockedVesselID != null || toDockedVesselID != null)
                {
                    HandleDocking();
                }

                if (isSpectatorDocking)
                {
                    HandleSpectatorDocking();
                }

                //Process new messages
                lock (updateQueueLock)
                {
                    ProcessNewVesselMessages();
                }

                //Update the screen spectate message.
                UpdateOnScreenSpectateMessage();

                //Lock and unlock spectate state
                UpdateSpectateLock();

                //Release old update locks
                ReleaseOldUpdateLocks();

                //Tell other players we have taken a vessel
                UpdateActiveVesselStatus();

                //Check current vessel state
                CheckVesselHasChanged();

                //Send updates of needed vessels
                SendVesselUpdates();
            }
        }

        private void RegisterGameHooks()
        {
            registered = true;
            GameEvents.onVesselRecovered.Add(this.OnVesselRecovered);
            GameEvents.onVesselTerminated.Add(this.OnVesselTerminated);
            GameEvents.onVesselDestroy.Add(this.OnVesselDestroyed);
            GameEvents.onGameSceneLoadRequested.Add(this.OnGameSceneLoadRequested);
            GameEvents.onFlightReady.Add(this.OnFlightReady);
            GameEvents.onPartCouple.Add(this.OnVesselDock);
            GameEvents.onCrewBoardVessel.Add(this.OnCrewBoard);
        }

        private void UnregisterGameHooks()
        {
            registered = false;
            GameEvents.onVesselRecovered.Remove(this.OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(this.OnVesselTerminated);
            GameEvents.onVesselDestroy.Remove(this.OnVesselDestroyed);
            GameEvents.onGameSceneLoadRequested.Remove(this.OnGameSceneLoadRequested);
            GameEvents.onFlightReady.Remove(this.OnFlightReady);
            GameEvents.onPartCouple.Remove(this.OnVesselDock);
        }

        private void HandleDocking()
        {
            if (sentDockingDestroyUpdate)
            {
                //One of them will be null, the other one will be the docked craft.
                string dockedID = fromDockedVesselID != null ? fromDockedVesselID : toDockedVesselID;
                //Find the docked craft
                Vessel dockedVessel = FlightGlobals.fetch.vessels.FindLast(v => v.id.ToString() == dockedID);
                if (dockedVessel != null ? !dockedVessel.packed : false)
                {
                    ProtoVessel sendProto = new ProtoVessel(dockedVessel);
                    if (sendProto != null)
                    {
                        DarkLog.Debug("Sending docked protovessel " + dockedID);
                        //Mark the vessel as sent
                        serverVesselsProtoUpdate[dockedID] = UnityEngine.Time.realtimeSinceStartup;
                        serverVesselsPositionUpdate[dockedID] = UnityEngine.Time.realtimeSinceStartup;
                        RegisterServerVessel(dockedID);
                        vesselPartCount[dockedID] = dockedVessel.parts.Count;
                        latestControlLockRequest[dockedID] = UnityEngine.Time.realtimeSinceStartup;
                        vesselNames[dockedID] = dockedVessel.vesselName;
                        vesselTypes[dockedID] = dockedVessel.vesselType;
                        vesselSituations[dockedID] = dockedVessel.situation;
                        //Update status if it's us.
                        if (dockedVessel == FlightGlobals.fetch.activeVessel)
                        {
                            //Force the control lock off any other player
                            LockSystem.fetch.AcquireLock("control-" + dockedID, true);
                            PlayerStatusWorker.fetch.myPlayerStatus.vesselText = FlightGlobals.ActiveVessel.vesselName;
                            if (lastVesselID != FlightGlobals.fetch.activeVessel.id.ToString())
                            {
                                if (LockSystem.fetch.LockIsOurs("control-" + lastVesselID))
                                {
                                    LockSystem.fetch.ReleaseLock("control-" + lastVesselID);
                                }
                                lastVesselID = FlightGlobals.ActiveVessel.id.ToString();
                            }
                        }
                        fromDockedVesselID = null;
                        toDockedVesselID = null;
                        sentDockingDestroyUpdate = false;
                        NetworkWorker.fetch.SendVesselProtoMessage(sendProto, true);
                        if (dockingMessage != null)
                        {
                            dockingMessage.duration = 0f;
                        }
                        dockingMessage = ScreenMessages.PostScreenMessage("Docked!", 3f, ScreenMessageStyle.UPPER_CENTER);
                        DarkLog.Debug("Docking event over!");
                    }
                    else
                    {
                        DarkLog.Debug("Error sending protovessel!");
                        PrintDockingInProgress();
                    }
                }
                else
                {
                    PrintDockingInProgress();
                }
            }
            else
            {
                PrintDockingInProgress();
            }
        }

        private void PrintDockingInProgress()
        {
            if ((UnityEngine.Time.realtimeSinceStartup - lastDockingMessageUpdate) > 1f)
            {
                lastDockingMessageUpdate = UnityEngine.Time.realtimeSinceStartup;
                if (dockingMessage != null)
                {
                    dockingMessage.duration = 0f;
                }
                dockingMessage = ScreenMessages.PostScreenMessage("Docking in progress...", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void HandleSpectatorDocking()
        {
            if (spectatorDockingID == null)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastDockingMessageUpdate) > 1f)
                {
                    lastDockingMessageUpdate = UnityEngine.Time.realtimeSinceStartup;
                    if (dockingMessage != null)
                    {
                        dockingMessage.duration = 0f;
                    }
                    dockingMessage = ScreenMessages.PostScreenMessage("Spectating docking in progress...", 3f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            else
            {
                Vessel switchToVessel = null;
                switchToVessel = FlightGlobals.fetch.vessels.FindLast(v => v.id.ToString() == spectatorDockingID);
                if (switchToVessel != null)
                {
                    KillVessel(FlightGlobals.fetch.activeVessel);
                    FlightGlobals.ForceSetActiveVessel(switchToVessel);
                    isSpectatorDocking = false;
                    spectatorDockingID = null;
                    spectatorDockingPlayer = null;
                }
            }
        }

        private void CheckMasterAcquire(string playerName, string lockName, bool lockResult)
        {
            if (isSpectatorDocking && playerName == spectatorDockingPlayer && lockName.StartsWith("control-") && lockResult)
            {
                //Cut off the control- part of the lock, that's our new masters ID.
                spectatorDockingID = lockName.Substring(8);
            }
        }

        private void ReleaseOldUpdateLocks()
        {
            List<string> removeList = new List<string>();
            foreach (KeyValuePair<string, double> entry in latestUpdateSent)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - entry.Value) > 5f)
                {
                    removeList.Add(entry.Key);
                }
            }
            foreach (string removeEntry in removeList)
            {
                latestUpdateSent.Remove(removeEntry);
                if (LockSystem.fetch.LockIsOurs("update-" + removeEntry))
                {
                    LockSystem.fetch.ReleaseLock("update-" + removeEntry);
                }
            }
        }

        private void ProcessNewVesselMessages()
        {
            Dictionary<string, double> removeList = new Dictionary<string, double>();
            foreach (KeyValuePair<string, Queue<VesselRemoveEntry>> vesselRemoveSubspace in vesselRemoveQueue)
            {
                while (vesselRemoveSubspace.Value.Count > 0 ? (vesselRemoveSubspace.Value.Peek().planetTime < Planetarium.GetUniversalTime()) : false)
                {
                    VesselRemoveEntry removeVessel = vesselRemoveSubspace.Value.Dequeue();
                    RemoveVessel(removeVessel.vesselID, removeVessel.isDockingUpdate, removeVessel.dockingPlayer);
                    removeList[removeVessel.vesselID] = removeVessel.planetTime;
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

            foreach (KeyValuePair<string, Queue<VesselProtoUpdate>> vesselQueue in vesselProtoQueue)
            {
                VesselProtoUpdate vpu = null;
                //Get the latest proto update
                while (vesselQueue.Value.Count > 0 ? (vesselQueue.Value.Peek().planetTime < Planetarium.GetUniversalTime()) : false)
                {
                    VesselProtoUpdate newVpu = vesselQueue.Value.Dequeue();
                    if (newVpu != null)
                    {
                        //Skip any protovessels that have been removed in the future
                        if (removeList.ContainsKey(vesselQueue.Key) ? removeList[vesselQueue.Key] < vpu.planetTime : true)
                        {
                            vpu = newVpu;
                        }
                    }
                }
                //Apply it if there is any
                if (vpu != null ? vpu.vesselNode != null : false)
                {
                    LoadVessel(vpu.vesselNode);
                }
            }
            foreach (KeyValuePair<string, Queue<VesselUpdate>> vesselQueue in vesselUpdateQueue)
            {
                VesselUpdate vu = null;
                //Get the latest position update
                while (vesselQueue.Value.Count > 0 ? (vesselQueue.Value.Peek().planetTime < Planetarium.GetUniversalTime()) : false)
                {
                    vu = vesselQueue.Value.Dequeue();
                }
                //Apply it if there is any
                if (vu != null)
                {
                    ApplyVesselUpdate(vu);
                }
            }
        }

        private void UpdateOnScreenSpectateMessage()
        {
            if ((UnityEngine.Time.realtimeSinceStartup - lastSpectateMessageUpdate) > UPDATE_SCREEN_MESSAGE_INTERVAL)
            {
                lastSpectateMessageUpdate = UnityEngine.Time.realtimeSinceStartup;
                if (isSpectating)
                {
                    if (spectateMessage != null)
                    {
                        spectateMessage.duration = 0f;
                    }
                    switch (spectateType)
                    {
                        case 1:
                            spectateMessage = ScreenMessages.PostScreenMessage("This vessel is controlled by another player...", UPDATE_SCREEN_MESSAGE_INTERVAL * 2, ScreenMessageStyle.UPPER_CENTER);
                            break;
                        case 2:
                            spectateMessage = ScreenMessages.PostScreenMessage("This vessel has been changed in the future...", UPDATE_SCREEN_MESSAGE_INTERVAL * 2, ScreenMessageStyle.UPPER_CENTER);
                            break;
                    }
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
                    if (!LockSystem.fetch.LockExists("control-" + FlightGlobals.ActiveVessel.id.ToString()))
                    {
                        //When we change vessel, send the previous flown vessel as soon as possible.
                        if (lastVesselID != "" && LockSystem.fetch.LockIsOurs("control-" + lastVesselID))
                        {
                            DarkLog.Debug("Resetting last send time for " + lastVesselID);
                            serverVesselsProtoUpdate[lastVesselID] = 0f;
                            LockSystem.fetch.ReleasePlayerLocksWithPrefix(Settings.fetch.playerName, "control-");
                        }
                        //Reset the send time of the vessel we just switched to
                        serverVesselsProtoUpdate[FlightGlobals.ActiveVessel.id.ToString()] = 0f;
                        //Nobody else is flying the vessel - let's take it
                        PlayerStatusWorker.fetch.myPlayerStatus.vesselText = FlightGlobals.ActiveVessel.vesselName;
                        if (latestControlLockRequest.ContainsKey(FlightGlobals.ActiveVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - latestControlLockRequest[FlightGlobals.ActiveVessel.id.ToString()]) > 5f) : true)
                        {
                            latestControlLockRequest[FlightGlobals.ActiveVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                            LockSystem.fetch.AcquireLock("control-" + FlightGlobals.ActiveVessel.id.ToString(), false);
                        }
                        lastVesselID = FlightGlobals.ActiveVessel.id.ToString();
                    }
                }
                else
                {
                    if (lastVesselID != "")
                    {
                        LockSystem.fetch.ReleasePlayerLocksWithPrefix(Settings.fetch.playerName, "control-");
                        lastVesselID = "";
                        PlayerStatusWorker.fetch.myPlayerStatus.vesselText = "";
                    }
                }
            }
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                //Release the vessel if we aren't in flight anymore.
                if (lastVesselID != "")
                {
                    LockSystem.fetch.ReleasePlayerLocksWithPrefix(Settings.fetch.playerName, "control-");
                    lastVesselID = "";
                    PlayerStatusWorker.fetch.myPlayerStatus.vesselText = "";
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
                    if (LockSystem.fetch.LockExists("control-" + vesselID) && !LockSystem.fetch.LockIsOurs("control-" + vesselID))
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

        private void CheckVesselHasChanged()
        {
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.fetch.activeVessel != null)
            {
                //Check all vessel for part count changes
                foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
                {
                    if (!isSpectating && checkVessel.loaded && !checkVessel.packed)
                    {
                        bool partCountChanged = vesselPartCount.ContainsKey(checkVessel.id.ToString()) ? checkVessel.parts.Count != vesselPartCount[checkVessel.id.ToString()] : true;

                        if (partCountChanged)
                        {
                            serverVesselsProtoUpdate[checkVessel.id.ToString()] = 0f;
                            vesselPartCount[checkVessel.id.ToString()] = checkVessel.parts.Count;
                            if (vesselPartsOk.ContainsKey(checkVessel.id.ToString()))
                            {
                                DarkLog.Debug("Forcing parts recheck on " + checkVessel.id.ToString());
                                vesselPartsOk.Remove(checkVessel.id.ToString());
                            }
                        }
                        //Add entries to dictionaries if needed
                        if (!vesselNames.ContainsKey(checkVessel.id.ToString()))
                        {
                            vesselNames.Add(checkVessel.id.ToString(), checkVessel.vesselName);
                        }
                        if (!vesselTypes.ContainsKey(checkVessel.id.ToString()))
                        {
                            vesselTypes.Add(checkVessel.id.ToString(), checkVessel.vesselType);
                        }
                        if (!vesselSituations.ContainsKey(checkVessel.id.ToString()))
                        {
                            vesselSituations.Add(checkVessel.id.ToString(), checkVessel.situation);
                        }
                        //Check active vessel for situation/renames. Throttle send to 10 seconds.
                        bool vesselNotRecentlyUpdated = serverVesselsPositionUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsProtoUpdate[checkVessel.id.ToString()]) > 10f) : true;
                        bool recentlyLanded = vesselSituations[checkVessel.id.ToString()] != Vessel.Situations.LANDED && checkVessel.situation == Vessel.Situations.LANDED;
                        bool recentlySplashed = vesselSituations[checkVessel.id.ToString()] != Vessel.Situations.SPLASHED && checkVessel.situation == Vessel.Situations.SPLASHED;
                        if (vesselNotRecentlyUpdated || recentlyLanded || recentlySplashed)
                        {
                            bool nameChanged = (vesselNames[checkVessel.id.ToString()] != checkVessel.vesselName);
                            bool typeChanged = (vesselTypes[checkVessel.id.ToString()] != checkVessel.vesselType);
                            bool situationChanged = (vesselSituations[checkVessel.id.ToString()] != checkVessel.situation);
                            if (nameChanged || typeChanged || situationChanged)
                            {
                                vesselNames[checkVessel.id.ToString()] = checkVessel.vesselName;
                                vesselTypes[checkVessel.id.ToString()] = checkVessel.vesselType;
                                vesselSituations[checkVessel.id.ToString()] = checkVessel.situation;
                                serverVesselsProtoUpdate[checkVessel.id.ToString()] = 0f;
                            }
                        }
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
                if (spectateType == 2)
                {
                    //We may be able to take the control lock.
                    if (!LockSystem.fetch.LockExists("control-" + FlightGlobals.ActiveVessel.id.ToString()))
                    {
                        if (latestUpdateLockRequest.ContainsKey(FlightGlobals.ActiveVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - latestUpdateLockRequest[FlightGlobals.ActiveVessel.id.ToString()]) > 5f) : true)
                        {
                            latestUpdateLockRequest[FlightGlobals.ActiveVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                            LockSystem.fetch.AcquireLock("control-" + FlightGlobals.ActiveVessel.id.ToString(), false);
                        }
                    }
                }
                return;
            }
            if (fromDockedVesselID != null || toDockedVesselID != null)
            {
                //Don't send updates while docking
                return;
            }
            if (isSpectatorDocking)
            {
                //Definitely dont send updates while spectating a docking
                return;
            }

            if (ModWorker.fetch.modControl)
            {
                if (!vesselPartsOk.ContainsKey(FlightGlobals.fetch.activeVessel.id.ToString()))
                {
                    //Check the vessel parts if we haven't already, shows the warning message in the safety bubble.
                    CheckVesselParts(FlightGlobals.fetch.activeVessel);
                }
            

                if (!vesselPartsOk[FlightGlobals.fetch.activeVessel.id.ToString()])
                {
                    if ((UnityEngine.Time.realtimeSinceStartup - lastBannedPartsMessageUpdate) > UPDATE_SCREEN_MESSAGE_INTERVAL)
                    {
                        lastBannedPartsMessageUpdate = UnityEngine.Time.realtimeSinceStartup;
                        if (bannedPartsMessage != null)
                        {
                            bannedPartsMessage.duration = 0;
                        }
                        bannedPartsMessage = ScreenMessages.PostScreenMessage("Active vessel contains the following banned parts, it will not be saved to the server:\n" + bannedPartsString, 2f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
            }

            if (isInSafetyBubble(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), FlightGlobals.fetch.activeVessel.mainBody))
            {
                //Don't send updates while in the safety bubble
                return;
            }

            SendVesselUpdateIfNeeded(FlightGlobals.fetch.activeVessel);
            SortedList<double, Vessel> secondryVessels = new SortedList<double, Vessel>();

            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                //Only update the vessel if it's loaded and unpacked (not on rails). Skip our vessel.
                if (checkVessel.loaded && !checkVessel.packed && checkVessel.id.ToString() != FlightGlobals.fetch.activeVessel.id.ToString())
                {
                    //Don't update vessels in the safety bubble
                    if (!isInSafetyBubble(checkVessel.GetWorldPos3D(), checkVessel.mainBody))
                    {
                        //Only attempt to update vessels that we have locks for, and ask for locks. Dont bother with controlled vessels
                        bool updateLockIsFree = !LockSystem.fetch.LockExists("update-" + checkVessel.id.ToString());
                        bool updateLockIsOurs = LockSystem.fetch.LockIsOurs("update-" + checkVessel.id.ToString());
                        bool controlledByPlayer = LockSystem.fetch.LockExists("control-" + checkVessel.id.ToString());
                        if ((updateLockIsFree || updateLockIsOurs) && !controlledByPlayer)
                        {
                            //Dont update vessels manipulated in the future
                            if (!VesselUpdatedInFuture(checkVessel.id.ToString()))
                            {
                                double currentDistance = Vector3d.Distance(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), checkVessel.GetWorldPos3D());
                                if (!secondryVessels.ContainsValue(checkVessel))
                                {
                                    secondryVessels.Add(currentDistance, checkVessel);
                                }
                            }
                        }
                    }
                }
            }

            int currentSend = 0;
            foreach (KeyValuePair<double, Vessel> secondryVessel in secondryVessels)
            {
                currentSend++;
                if (currentSend > DynamicTickWorker.fetch.maxSecondryVesselsPerTick)
                {
                    break;
                }
                SendVesselUpdateIfNeeded(secondryVessel.Value);
            }
        }

        private void CheckVesselParts(Vessel checkVessel)
        {
            List<string> allowedParts = ModWorker.fetch.GetAllowedPartsList();
            List<string> bannedParts = new List<string>();
            ProtoVessel checkProto = checkVessel.protoVessel;
            if (!checkVessel.packed)
            {
                checkProto = new ProtoVessel(checkVessel);
            }
            foreach (ProtoPartSnapshot part in checkProto.protoPartSnapshots)
            {
                if (!allowedParts.Contains(part.partName))
                {
                    if (!bannedParts.Contains(part.partName))
                    {
                        bannedParts.Add(part.partName);
                    }
                }
            }
            if (checkVessel.id.ToString() == FlightGlobals.fetch.activeVessel.id.ToString())
            {
                bannedPartsString = "";
                foreach (string bannedPart in bannedParts)
                {
                    bannedPartsString += bannedPart + "\n";
                }
            }
            DarkLog.Debug("Checked vessel " + checkVessel.id.ToString() + " for banned parts, is ok: " + (bannedParts.Count == 0));
            vesselPartsOk[checkVessel.id.ToString()] = (bannedParts.Count == 0);
        }

        private void SendVesselUpdateIfNeeded(Vessel checkVessel)
        {
            //Check vessel parts
            if (ModWorker.fetch.modControl)
            {
                if (!vesselPartsOk.ContainsKey(checkVessel.id.ToString()))
                {
                    CheckVesselParts(checkVessel);
                }
                if (!vesselPartsOk[checkVessel.id.ToString()])
                {
                    //Vessel with bad parts
                    return;
                }
            }

            if (checkVessel.vesselType == VesselType.Flag && checkVessel.id == Guid.Empty && checkVessel.vesselName != "Flag")
            {
                DarkLog.Debug("Fixing flag GUID for " + checkVessel.vesselName);
                checkVessel.id = Guid.NewGuid();
            }

            //Only send updates for craft we have update locks for. Request the lock if it's not taken.
            if (!LockSystem.fetch.LockExists("update-" + checkVessel.id.ToString()))
            {
                if (latestUpdateLockRequest.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - latestUpdateLockRequest[checkVessel.id.ToString()]) > 5f) : true)
                {
                    latestUpdateLockRequest[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    LockSystem.fetch.AcquireLock("update-" + checkVessel.id.ToString(), false);
                }
                //Wait until we have the update lock
                return;
            }

            //Take the update lock off another player if we have the control lock and it's our vessel
            if (checkVessel.id.ToString() == FlightGlobals.fetch.activeVessel.id.ToString())
            {
                if (LockSystem.fetch.LockExists("update-" + checkVessel.id.ToString()) && !LockSystem.fetch.LockIsOurs("update-" + checkVessel.id.ToString()) && LockSystem.fetch.LockIsOurs("control-" + checkVessel.id.ToString()))
                {
                    if (latestUpdateLockRequest.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - latestUpdateLockRequest[checkVessel.id.ToString()]) > 5f) : true)
                    {
                        latestUpdateLockRequest[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        LockSystem.fetch.AcquireLock("update-" + checkVessel.id.ToString(), true);
                    }
                    //Wait until we have the update lock
                    return;
                }
            }

            //Send updates for unpacked vessels that aren't being flown by other players
            bool notRecentlySentProtoUpdate = serverVesselsProtoUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsProtoUpdate[checkVessel.id.ToString()]) > VESSEL_PROTOVESSEL_UPDATE_INTERVAL) : true;
            bool notRecentlySentPositionUpdate = serverVesselsPositionUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsPositionUpdate[checkVessel.id.ToString()]) > (1f / (float)DynamicTickWorker.fetch.sendTickRate)) : true;

            //Check that is hasn't been recently sent
            if (notRecentlySentProtoUpdate && (checkVessel.situation != Vessel.Situations.FLYING))
            {

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
                                    NetworkWorker.fetch.SendKerbalProtoMessage(HighLogic.CurrentGame.CrewRoster.IndexOf(pcm), pcm);
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
                                        NetworkWorker.fetch.SendKerbalProtoMessage(HighLogic.CurrentGame.CrewRoster.IndexOf(pcm), pcm);
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
                        RegisterServerVessel(checkProto.vesselID.ToString());
                        //Mark the update as sent
                        serverVesselsProtoUpdate[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        //Also delay the position send
                        serverVesselsPositionUpdate[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        latestUpdateSent[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        NetworkWorker.fetch.SendVesselProtoMessage(checkProto, false);
                    }
                    else
                    {
                        DarkLog.Debug(checkVessel.vesselName + " does not have a guid!");
                    }
                }
            }
            else if (notRecentlySentPositionUpdate && checkVessel.vesselType != VesselType.Flag)
            {
                //Send a position update - Except for flags. They aren't exactly known for their mobility.
                serverVesselsPositionUpdate[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                latestUpdateSent[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                VesselUpdate update = GetVesselUpdate(checkVessel);
                if (update != null)
                {
                    NetworkWorker.fetch.SendVesselUpdate(update);
                }
            }
        }
        //Also called from PlayerStatusWorker
        public bool isSpectating
        {
            get
            {
                if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                {
                    if (FlightGlobals.fetch.activeVessel != null)
                    {
                        if (isSpectatorDocking)
                        {
                            spectateType = 1;
                            return true;
                        }
                        if (LockSystem.fetch.LockExists("control-" + FlightGlobals.ActiveVessel.id.ToString()) && !LockSystem.fetch.LockIsOurs("control-" + FlightGlobals.ActiveVessel.id.ToString()))
                        {
                            spectateType = 1;
                            return true;
                        }
                        if (VesselUpdatedInFuture(FlightGlobals.fetch.activeVessel.id.ToString()))
                        {
                            spectateType = 2;
                            return true;
                        }
                    }
                }
                spectateType = 0;
                return false;
            }
        }
        //Adapted from KMP. Called from PlayerStatusWorker.
        public bool isInSafetyBubble(Vector3d worlPos, CelestialBody body)
        {
            //If not at Kerbin or past ceiling we're definitely clear
            if (body.name != "Kerbin")
            {
                return false;
            }
            Vector3d landingPadPosition = body.GetWorldSurfacePosition(-0.0971978130377757, 285.44237039111, 60);
            Vector3d runwayPosition = body.GetWorldSurfacePosition(-0.0486001121594686, 285.275552559723, 60);
            double landingPadDistance = Vector3d.Distance(worlPos, landingPadPosition);
            double runwayDistance = Vector3d.Distance(worlPos, runwayPosition);
            return runwayDistance < SAFETY_BUBBLE_DISTANCE || landingPadDistance < SAFETY_BUBBLE_DISTANCE;
        }
        //Adapted from KMP.
        private bool isProtoVesselInSafetyBubble(ProtoVessel protovessel)
        {
            if (protovessel != null)
            {
                //If not kerbin, we aren't in the safety bubble.
                if (protovessel.orbitSnapShot.ReferenceBodyIndex != FlightGlobals.Bodies.FindIndex(body => body.bodyName == "Kerbin"))
                {
                    return false;
                }
                CelestialBody kerbinBody = FlightGlobals.Bodies.Find(b => b.name == "Kerbin");
                Vector3d protoVesselPosition = kerbinBody.GetWorldSurfacePosition(protovessel.latitude, protovessel.longitude, protovessel.altitude);
                return isInSafetyBubble(protoVesselPosition, kerbinBody);
            }
            else
            {
                DarkLog.Debug("isProtoVesselInSafetyBubble: protovessel is null!");
                return true;
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
                    NetworkWorker.fetch.SendKerbalProtoMessage(kerbalID, protoKerbal);
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
            foreach (KeyValuePair<string, Queue<VesselProtoUpdate>> vesselQueue in vesselProtoQueue)
            {
                while (vesselQueue.Value.Count > 0)
                {
                    ConfigNode currentNode = vesselQueue.Value.Dequeue().vesselNode;
                    if (currentNode != null)
                    {
                        LoadVessel(currentNode);
                    }
                }
            }
        }
        //Thanks KMP :)
        private void checkProtoNodeCrew(ref ConfigNode protoNode)
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
            int currentKerbals = 0;
            while (crewEnum.MoveNext())
            {
                currentKerbals++;
            }
            if (currentKerbals <= kerbalID)
            {
                DarkLog.Debug("Generating " + ((kerbalID + 1) - currentKerbals) + " new kerbal for an assigned crew index " + kerbalID);
            }
            while (currentKerbals <= kerbalID)
            {
                ProtoCrewMember protoKerbal = CrewGenerator.RandomCrewMemberPrototype();
                if (!HighLogic.CurrentGame.CrewRoster.ExistsInRoster(protoKerbal.name))
                {
                    DarkLog.Debug("Generated new kerbal " + protoKerbal.name + ", ID: " + currentKerbals);
                    HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoKerbal);
                    int newKerbalID = HighLogic.CurrentGame.CrewRoster.IndexOf(protoKerbal); 
                    serverKerbals[newKerbalID] = new ProtoCrewMember(protoKerbal);
                    NetworkWorker.fetch.SendKerbalProtoMessage(newKerbalID, protoKerbal);
                    currentKerbals++;
                }
            }
        }
        //Also called from QuickSaveLoader
        public void LoadVessel(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                //Fix the kerbals (Tracking station bug)
                checkProtoNodeCrew(ref vesselNode);

                //Can be used for debugging incoming vessel config nodes.
                //vesselNode.Save(Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("DMP-RX", Planetarium.GetUniversalTime() + ".txt")));
                ProtoVessel currentProto = new ProtoVessel(vesselNode, HighLogic.CurrentGame);
                if (currentProto != null)
                {
                    if (currentProto.vesselType == VesselType.SpaceObject)
                    {
                        if (currentProto.protoPartSnapshots != null)
                        {
                            if (currentProto.protoPartSnapshots.Count == 1)
                            {
                                if (currentProto.protoPartSnapshots[0].partName == "PotatoRoid")
                                {
                                    DarkLog.Debug("Registering remote server asteroid");
                                    AsteroidWorker.fetch.RegisterServerAsteroid(currentProto.vesselID.ToString());
                                }
                            }
                        }
                    }

                    if (isProtoVesselInSafetyBubble(currentProto))
                    {
                        DarkLog.Debug("Removing protovessel " + currentProto.vesselID.ToString() + ", name: " + currentProto.vesselName + " from server - In safety bubble!");
                        NetworkWorker.fetch.SendVesselRemove(currentProto.vesselID.ToString(), false);
                        return;
                    }
                    RegisterServerVessel(currentProto.vesselID.ToString());
                    DarkLog.Debug("Loading " + currentProto.vesselID + ", name: " + currentProto.vesselName + ", type: " + currentProto.vesselType);

                    foreach (ProtoPartSnapshot part in currentProto.protoPartSnapshots)
                    {
                        //This line doesn't actually do anything useful, but if you get this reference, you're officially the most geeky person darklight knows.
                        part.temperature = ((part.temperature + 273.15f) * 0.8f) - 273.15f;
                    }

                    bool wasActive = false;
                    bool wasTarget = false;

                    if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                    {
                        if (FlightGlobals.fetch.VesselTarget != null ? FlightGlobals.fetch.VesselTarget.GetVessel() != null : false)
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
                            try
                            {
                                OrbitPhysicsManager.HoldVesselUnpack(5);
                            }
                            catch
                            {
                                //Don't care.
                            }
                            FlightGlobals.fetch.activeVessel.MakeInactive();
                        }
                    }

                    for (int vesselID = FlightGlobals.fetch.vessels.Count - 1; vesselID >= 0; vesselID--)
                    {
                        Vessel oldVessel = FlightGlobals.fetch.vessels[vesselID];
                        if (oldVessel.id.ToString() == currentProto.vesselID.ToString())
                        {
                            KillVessel(oldVessel);
                        }
                    }

                    serverVesselsProtoUpdate[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    currentProto.Load(HighLogic.CurrentGame.flightState);

                    if (currentProto.vesselRef != null)
                    {
                        UpdatePackDistance(currentProto.vesselRef.id.ToString());

                        if (wasActive)
                        {
                            DarkLog.Debug("Set active vessel");
                            switchActiveVesselOnNextUpdate = currentProto.vesselRef;
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

        public void OnVesselDestroyed(Vessel dyingVessel)
        {
            string dyingVesselID = dyingVessel.id.ToString();
            //Docking destructions
            if (dyingVesselID == fromDockedVesselID || dyingVesselID == toDockedVesselID)
            {
                DarkLog.Debug("Removing vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + " from the server: Docked");
                unassignKerbals(dyingVesselID);
                if (serverVessels.Contains(dyingVesselID))
                {
                    serverVessels.Remove(dyingVesselID);
                }
                NetworkWorker.fetch.SendVesselRemove(dyingVesselID, true);
                if (serverVesselsProtoUpdate.ContainsKey(dyingVesselID))
                {
                    serverVesselsProtoUpdate.Remove(dyingVesselID);
                }
                if (serverVesselsPositionUpdate.ContainsKey(dyingVesselID))
                {
                    serverVesselsPositionUpdate.Remove(dyingVesselID);
                }
                if (fromDockedVesselID == dyingVesselID)
                {
                    fromDockedVesselID = null;
                }
                if (toDockedVesselID == dyingVesselID)
                {
                    toDockedVesselID = null;
                }
                sentDockingDestroyUpdate = true;
                return;
            }
            //Normal destructions
            if (destroyIsValid)
            {
                if (!VesselRecentlyKilled(dyingVesselID))
                {
                    if (!VesselUpdatedInFuture(dyingVesselID))
                    {
                        //Remove the vessel from the server if it's not owned by another player.
                        if (!LockSystem.fetch.LockExists("update-" + dyingVesselID) || LockSystem.fetch.LockIsOurs("update-" + dyingVesselID))
                        {
                            if (serverVessels.Contains(dyingVessel.id.ToString()))
                            {
                                DarkLog.Debug("Removing vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + " from the server: Destroyed");
                                unassignKerbals(dyingVesselID);
                                serverVessels.Remove(dyingVesselID);
                                if (serverVesselsProtoUpdate.ContainsKey(dyingVesselID))
                                {
                                    serverVesselsProtoUpdate.Remove(dyingVesselID);
                                }
                                if (serverVesselsPositionUpdate.ContainsKey(dyingVesselID))
                                {
                                    serverVesselsPositionUpdate.Remove(dyingVesselID);
                                }
                                NetworkWorker.fetch.SendVesselRemove(dyingVesselID, false);
                            }
                            else
                            {
                                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", not a server vessel.");
                            }
                        }
                        else
                        {
                            DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", update lock owned by another player.");
                        }
                    }
                    else
                    {
                        DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", vessel has been changed in the future.");
                    }
                }
                else
                {
                    DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", vessel has been recently killed.");
                }

            }
            else
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", destructions are not valid.");
            }
        }

        public void OnVesselRecovered(ProtoVessel recoveredVessel)
        {
            string recoveredVesselID = recoveredVessel.vesselID.ToString();
            //Check the vessel hasn't been changed in the future
            if (!VesselUpdatedInFuture(recoveredVesselID))
            {
                //Remove the vessel from the server if it's not owned by another player.
                if (!LockSystem.fetch.LockExists("update-" + recoveredVesselID) || LockSystem.fetch.LockIsOurs("update-" + recoveredVesselID))
                {
                    //Check that it's a server vessel
                    if (serverVessels.Contains(recoveredVesselID))
                    {
                        DarkLog.Debug("Removing vessel " + recoveredVesselID + ", name: " + recoveredVessel.vesselName + " from the server: Recovered");
                        unassignKerbals(recoveredVesselID);
                        serverVessels.Remove(recoveredVesselID);
                        NetworkWorker.fetch.SendVesselRemove(recoveredVesselID, false);
                    }
                    else
                    {
                        DarkLog.Debug("Cannot recover a non-server vessel!");
                    }
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Cannot recover vessel, the vessel is in use.", 5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            else
            {
                ScreenMessages.PostScreenMessage("Cannot recover vessel, the vessel been changed in the future.", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        public void OnVesselTerminated(ProtoVessel terminatedVessel)
        {
            string terminatedVesselID = terminatedVessel.vesselID.ToString();
            //Check the vessel hasn't been changed in the future
            if (!VesselUpdatedInFuture(terminatedVesselID))
            {
                //Remove the vessel from the server if it's not owned by another player.
                if (!LockSystem.fetch.LockExists("update-" + terminatedVesselID) || LockSystem.fetch.LockIsOurs("update-" + terminatedVesselID))
                {
                    if (serverVessels.Contains(terminatedVesselID))
                    {
                        DarkLog.Debug("Removing vessel " + terminatedVesselID + ", name: " + terminatedVessel.vesselName + " from the server: Terminated");
                        unassignKerbals(terminatedVesselID);
                        serverVessels.Remove(terminatedVesselID);
                        NetworkWorker.fetch.SendVesselRemove(terminatedVesselID, false);
                    }
                    else
                    {
                        DarkLog.Debug("Cannot terminate a non-server vessel!");
                    }
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Cannot terminate vessel, the vessel is in use.", 5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            else
            {
                ScreenMessages.PostScreenMessage("Cannot terminate vessel, the vessel been changed in the future.", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private bool VesselRecentlyKilled(string vesselID)
        {
            return lastKillVesselDestroy.ContainsKey(vesselID) ? ((UnityEngine.Time.realtimeSinceStartup - lastKillVesselDestroy[vesselID]) < 10f) : false;
        }

        private bool VesselUpdatedInFuture(string vesselID)
        {
            return latestVesselUpdate.ContainsKey(vesselID) ? ((latestVesselUpdate[vesselID] + 10f) > Planetarium.GetUniversalTime()) : false;
        }

        public void OnGameSceneLoadRequested(GameScenes scene)
        {
            if (destroyIsValid)
            {
                DarkLog.Debug("Vessel destructions are now invalid");
                destroyIsValid = false;
            }
        }

        public void OnFlightReady()
        {
            if (!destroyIsValid)
            {
                DarkLog.Debug("Vessel destructions are now valid");
                destroyIsValid = true;
            }
        }

        public void OnVesselDock(GameEvents.FromToAction<Part, Part> partAction)
        {
            DarkLog.Debug("Vessel docking detected!");
            if (!isSpectating)
            {
                if (partAction.from.vessel != null && partAction.to.vessel != null)
                {
                    if (partAction.from.vessel == FlightGlobals.fetch.activeVessel || partAction.to.vessel == FlightGlobals.fetch.activeVessel)
                    {                    
                        DarkLog.Debug("Vessel docking, from: " + partAction.from.vessel.id + ", name: " + partAction.from.vessel.vesselName);
                        DarkLog.Debug("Vessel docking, to: " + partAction.to.vessel.id + ", name: " + partAction.to.vessel.vesselName);
                        if (FlightGlobals.fetch.activeVessel != null)
                        {
                            DarkLog.Debug("Vessel docking, our vessel: " + FlightGlobals.fetch.activeVessel.id);
                        }
                        fromDockedVesselID = partAction.from.vessel.id.ToString();
                        toDockedVesselID = partAction.to.vessel.id.ToString();
                        PrintDockingInProgress();
                    }
                }
            }
            else
            {
                //We need to get the spectator to stay spectating until the master has docked.
                DarkLog.Debug("Docked during spectate mode");
                if (LockSystem.fetch.LockExists("control-" + FlightGlobals.ActiveVessel.id.ToString()))
                {
                    isSpectatorDocking = true;
                    spectatorDockingPlayer = LockSystem.fetch.LockOwner("control-" + FlightGlobals.ActiveVessel.id.ToString());
                }
                else
                {
                    HighLogic.LoadScene(GameScenes.TRACKSTATION);
                }
            }
        }

        private void OnCrewBoard(GameEvents.FromToAction<Part, Part> partAction)
        {
            DarkLog.Debug("Crew boarding detected!");
            if (!isSpectating)
            {
                DarkLog.Debug("EVA Boarding, from: " + partAction.from.vessel.id + ", name: " + partAction.from.vessel.vesselName);
                DarkLog.Debug("EVA Boarding, to: " + partAction.to.vessel.id + ", name: " + partAction.to.vessel.vesselName);
                fromDockedVesselID = partAction.from.vessel.id.ToString();
                toDockedVesselID = partAction.to.vessel.id.ToString();
            }
        }

        private void unassignKerbals(string vesselID)
        {
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
                if (!isSpectating)
                {
                    NetworkWorker.fetch.SendKerbalProtoMessage(unassignKerbal, HighLogic.CurrentGame.CrewRoster[unassignKerbal]);
                }
            }
        }

        private void KillVessel(Vessel killVessel)
        {
            if (killVessel != null)
            {
                DarkLog.Debug("Killing vessel: " + killVessel.id.ToString());
                if (!killVessels.Contains(killVessel))
                {
                    killVessels.Add(killVessel);
                }
                lastKillVesselDestroy[killVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                try
                {
                    if (!killVessel.packed)
                    {
                        try
                        {
                            OrbitPhysicsManager.HoldVesselUnpack(2);
                        }
                        catch
                        {
                            //Don't care
                        }
                        killVessel.GoOnRails();
                    }
                    if (killVessel.loaded)
                    {
                        killVessel.Unload();
                    }
                    killVessel.Die();
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Error destroying vessel: " + e);
                }
            }
        }

        private void RemoveVessel(string vesselID, bool isDockingUpdate, string dockingPlayer)
        {
            for (int i = FlightGlobals.fetch.vessels.Count - 1; i >= 0; i--)
            {
                Vessel checkVessel = FlightGlobals.fetch.vessels[i];
                if (checkVessel.id.ToString() == vesselID)
                {
                    if (isDockingUpdate)
                    {
                        if (FlightGlobals.fetch.activeVessel != null ? FlightGlobals.fetch.activeVessel.id.ToString() == checkVessel.id.ToString() : false)
                        {
                            Vessel dockingPlayerVessel = null;
                            foreach (Vessel findVessel in FlightGlobals.fetch.vessels)
                            {
                                if (LockSystem.fetch.LockOwner("control-" + findVessel.id.ToString()) == dockingPlayer)
                                {
                                    dockingPlayerVessel = findVessel;
                                }
                            }
                            if (dockingPlayerVessel != null)
                            {
                                FlightGlobals.ForceSetActiveVessel(dockingPlayerVessel);
                            }
                            else
                            {
                                HighLogic.LoadScene(GameScenes.TRACKSTATION);
                                ScreenMessages.PostScreenMessage("Kicked to tracking station, a player docked with you but they were not loaded into the game.");
                            }
                        }
                        DarkLog.Debug("Removing docked vessel: " + vesselID);
                        KillVessel(checkVessel);
                    }
                    else
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
                            DarkLog.Debug("Removing vessel: " + vesselID);
                            KillVessel(checkVessel);
                        }
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
            string updatePlayer = LockSystem.fetch.LockExists(update.vesselID) ? LockSystem.fetch.LockOwner(update.vesselID) : "Unknown";
            //Ignore updates to our own vessel
            if (!isSpectating && (FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.id.ToString() == update.vesselID : false))
            {
                DarkLog.Debug("ApplyVesselUpdate - Ignoring update for active vessel from " + updatePlayer);
                return;
            }
            Vessel updateVessel = FlightGlobals.fetch.vessels.FindLast(v => v.id.ToString() == update.vesselID);
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
            if (!kerbalProtoQueue.ContainsKey(subspace))
            {
                kerbalProtoQueue.Add(subspace, new Queue<KerbalEntry>());
            }
            kerbalProtoQueue[subspace].Enqueue(newEntry);
        }
        //Called from networkWorker
        public void QueueVesselRemove(string vesselID, double planetTime, bool isDockingUpdate, string dockingPlayer)
        {
            lock (updateQueueLock)
            {
                if (!vesselRemoveQueue.ContainsKey(vesselID))
                {
                    vesselRemoveQueue.Add(vesselID, new Queue<VesselRemoveEntry>());
                }
                VesselRemoveEntry vre = new VesselRemoveEntry();
                vre.planetTime = planetTime;
                vre.vesselID = vesselID;
                vre.isDockingUpdate = isDockingUpdate;
                vre.dockingPlayer = dockingPlayer;
                vesselRemoveQueue[vesselID].Enqueue(vre);
                if (latestVesselUpdate.ContainsKey(vesselID) ? latestVesselUpdate[vesselID] < planetTime : true)
                {
                    latestVesselUpdate[vesselID] = planetTime;
                }
            }
        }

        public void QueueVesselProto(string vesselID, double planetTime, ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                lock (updateQueueLock)
                {
                    DarkLog.Debug("Queueing proto for " + vesselID);
                    DarkLog.Debug("ProtoQueue is ok: " + (vesselProtoQueue != null));
                    if (vesselProtoQueue != null)
                    {
                        DarkLog.Debug("ProtoQueue contains key: " + vesselProtoQueue.ContainsKey(vesselID));
                    }
                    if (!vesselProtoQueue.ContainsKey(vesselID))
                    {
                        DarkLog.Debug("Adding queue for " + vesselID);
                        vesselProtoQueue.Add(vesselID, new Queue<VesselProtoUpdate>());
                    }
                    VesselProtoUpdate vpu = new VesselProtoUpdate();
                    vpu.planetTime = planetTime;
                    vpu.vesselNode = vesselNode;
                    vesselProtoQueue[vesselID].Enqueue(vpu);
                }
            }
            else
            {
                DarkLog.Debug("Refusing to queue proto for " + vesselID + ", it has a null config node");
            }
        }

        public void QueueVesselUpdate(VesselUpdate update)
        {
            lock (updateQueueLock)
            {
                if (!vesselUpdateQueue.ContainsKey(update.vesselID))
                {
                    vesselUpdateQueue.Add(update.vesselID, new Queue<VesselUpdate>());
                }
                vesselUpdateQueue[update.vesselID].Enqueue(update);
                if (latestVesselUpdate.ContainsKey(update.vesselID) ? latestVesselUpdate[update.vesselID] < update.planetTime : true)
                {
                    latestVesselUpdate[update.vesselID] = update.planetTime;
                }
            }
        }

        public void QueueActiveVessel(string player, string vesselID)
        {
            ActiveVesselEntry ave = new ActiveVesselEntry();
            ave.player = player;
            ave.vesselID = vesselID;
            newActiveVessels.Enqueue(ave);
        }

        public void RegisterServerVessel(string vesselID)
        {
            if (!serverVessels.Contains(vesselID))
            {
                serverVessels.Add(vesselID);
            }
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    singleton.workerEnabled = false;
                    Client.fixedUpdateEvent.Remove(singleton.FixedUpdate);
                    if (singleton.registered)
                    {
                        singleton.UnregisterGameHooks();
                    }
                }
                singleton = new VesselWorker();
                Client.fixedUpdateEvent.Add(singleton.FixedUpdate);
                LockSystem.fetch.RegisterAcquireHook(singleton.CheckMasterAcquire);
            }
        }

        public int GetStatistics(string statType)
        {
            switch (statType)
            {
                case "StoredFutureUpdates":
                    {
                        int futureUpdates = 0;
                        foreach (KeyValuePair<string, Queue<VesselUpdate>> vUQ in vesselUpdateQueue)
                        {
                            futureUpdates += vUQ.Value.Count;
                        }
                        return futureUpdates;
                    }
                case "StoredFutureProtoUpdates":
                    {
                        int futureProtoUpdates = 0;
                        foreach (KeyValuePair<string, Queue<VesselProtoUpdate>> vPQ in vesselProtoQueue)
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
        public bool isDockingUpdate;
        public string dockingPlayer;
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

