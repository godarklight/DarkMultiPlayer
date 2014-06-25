using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;

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
        private Dictionary<string, double> latestUpdateSent = new Dictionary<string, double>();
        //Track spectating state
        private bool wasSpectating;
        private int spectateType;
        //KillVessel tracking
        private Dictionary<string, double> lastKillVesselDestroy = new Dictionary<string, double>();
        private Dictionary<string, double> lastLoadVessel = new Dictionary<string, double>();
        private List<Vessel> delayKillVessels = new List<Vessel>();
        //Hacky flying vessel loading
        private Queue<HackyFlyingVesselLoad> loadingFlyingVessels = new Queue<HackyFlyingVesselLoad>();
        //Docking related
        private Vessel newActiveVessel;
        private int activeVesselLoadUpdates;
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
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                return;
            }
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
                //Kill hacky flying vessels that have failed to load
                if (loadingFlyingVessels.Count > 0)
                {
                    HackyFlyingVesselLoad hfvl = loadingFlyingVessels.Peek();
                    if (!FlightGlobals.fetch.vessels.Contains(hfvl.flyingVessel))
                    {
                        //Vessel failed to load
                        DarkLog.Debug("Hacky load failed for " + hfvl.flyingVessel.id.ToString() + ", killed in atmo");
                        loadingFlyingVessels.Dequeue();
                    }
                    else
                    {
                        if (hfvl.flyingVessel.loaded && !hfvl.flyingVessel.packed)
                        {
                            hfvl.flyingVessel.Landed = false;
                            DarkLog.Debug("Hacky load successful for " + hfvl.flyingVessel.id.ToString());
                            loadingFlyingVessels.Dequeue();
                        }
                        else
                        {
                            //Ask to go off rails at 4 seconds
                            if (hfvl.flyingVessel.loaded)
                            {
                                if ((UnityEngine.Time.realtimeSinceStartup - hfvl.loadTime) > 4f && !hfvl.requestedToGoOffRails)
                                {
                                    hfvl.requestedToGoOffRails = true;
                                    DarkLog.Debug("Asking vessel to go off rails");
                                    hfvl.flyingVessel.GoOffRails();
                                }
                            }
                            //Timed out
                            if ((UnityEngine.Time.realtimeSinceStartup - hfvl.loadTime) > 5f)
                            {
                                DarkLog.Debug("Hacky load failed for " + hfvl.flyingVessel.id.ToString() + ", failed to load in time");
                                KillVessel(hfvl.flyingVessel);
                                loadingFlyingVessels.Dequeue();
                            }
                        }
                    }
                }

                //Switch to a new active vessel if needed.
                if (newActiveVessel != null)
                {
                    if (FlightGlobals.fetch.vessels.Contains(newActiveVessel))
                    {
                        //Hold unpack so we don't collide with the old copy
                        try
                        {
                            OrbitPhysicsManager.HoldVesselUnpack(2);
                        }
                        catch
                        {
                        }
                        //If the vessel failed to load in a reasonable time, go through the loading screen
                        if (activeVesselLoadUpdates > 100)
                        {
                            DarkLog.Debug("Active vessel must not be within load distance, go through the loading screen method");
                            activeVesselLoadUpdates = 0;
                            FlightGlobals.ForceSetActiveVessel(newActiveVessel);
                            newActiveVessel = null;
                        }
                        if (!newActiveVessel.loaded)
                        {
                            activeVesselLoadUpdates++;
                            return;
                        }
                        activeVesselLoadUpdates = 0;
                        if (FlightGlobals.fetch.activeVessel.patchedConicRenderer == null || FlightGlobals.fetch.activeVessel.patchedConicRenderer.solver == null || FlightGlobals.fetch.activeVessel.patchedConicRenderer.solver.maneuverNodes == null)
                        {
                            DarkLog.Debug("Waiting for new active vessel to be sane");
                            return;
                        }
                        DarkLog.Debug("Switching to active vessel!");
                        FlightGlobals.ForceSetActiveVessel(newActiveVessel);
                        newActiveVessel = null;
                        return;
                    }
                    else
                    {
                        DarkLog.Debug("switchActiveVesselOnNextUpdate Vessel failed to spawn into game!");
                        ScreenMessages.PostScreenMessage("Active vessel update failed to load into the game!", 5, ScreenMessageStyle.UPPER_CENTER);
                        HighLogic.LoadScene(GameScenes.TRACKSTATION);
                    }
                }

                //Kill any queued vessels
                List<Vessel> deleteList = new List<Vessel>();
                foreach (Vessel dyingVessel in delayKillVessels)
                {
                    if (FlightGlobals.fetch.vessels.Contains(dyingVessel))
                    {
                        DarkLog.Debug("Delay killing " + dyingVessel.id.ToString());
                        KillVessel(dyingVessel);
                    }
                    else
                    {
                        deleteList.Add(dyingVessel);
                    }
                }
                foreach (Vessel deadVessel in deleteList)
                {
                    delayKillVessels.Remove(deadVessel);
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

                //Check for vessel changes
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
            GameEvents.onPartCouple.Add(this.OnVesselDock);
            GameEvents.onCrewBoardVessel.Add(this.OnCrewBoard);
        }

        private void UnregisterGameHooks()
        {
            registered = false;
            GameEvents.onVesselRecovered.Remove(this.OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(this.OnVesselTerminated);
            GameEvents.onVesselDestroy.Remove(this.OnVesselDestroyed);
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
                        vesselNames[dockedID] = dockedVessel.vesselName;
                        vesselTypes[dockedID] = dockedVessel.vesselType;
                        vesselSituations[dockedID] = dockedVessel.situation;
                        //Update status if it's us.
                        if (dockedVessel == FlightGlobals.fetch.activeVessel)
                        {
                            //Release old control locks
                            if (lastVesselID != FlightGlobals.fetch.activeVessel.id.ToString())
                            {
                                LockSystem.fetch.ReleasePlayerLocksWithPrefix(Settings.fetch.playerName, "control-");
                                lastVesselID = FlightGlobals.fetch.activeVessel.id.ToString();
                            }
                            //Force the control lock off any other player
                            LockSystem.fetch.AcquireLock("control-" + dockedID, true);
                            PlayerStatusWorker.fetch.myPlayerStatus.vesselText = FlightGlobals.fetch.activeVessel.vesselName;
                        }
                        fromDockedVesselID = null;
                        toDockedVesselID = null;
                        sentDockingDestroyUpdate = false;
                        bool isFlyingUpdate = (sendProto.situation == Vessel.Situations.FLYING);
                        NetworkWorker.fetch.SendVesselProtoMessage(sendProto, true, isFlyingUpdate);
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
            bool isActiveVesselOk = FlightGlobals.fetch.activeVessel != null ? (FlightGlobals.fetch.activeVessel.loaded && !FlightGlobals.fetch.activeVessel.packed) : false;
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && isActiveVesselOk)
            {
                if (isSpectating && spectateType == 2)
                {
                    if (!LockSystem.fetch.LockExists("control-" + FlightGlobals.fetch.activeVessel.id.ToString()))
                    {
                        LockSystem.fetch.ThrottledAcquireLock("control-" + FlightGlobals.fetch.activeVessel.id.ToString());
                    }
                }

                if (!isSpectating)
                {
                    //When we change vessel, send the previous flown vessel as soon as possible.
                    if (lastVesselID != FlightGlobals.fetch.activeVessel.id.ToString())
                    {
                        if (lastVesselID != "")
                        {
                            DarkLog.Debug("Resetting last send time for " + lastVesselID);
                            serverVesselsProtoUpdate[lastVesselID] = 0f;
                            LockSystem.fetch.ReleasePlayerLocksWithPrefix(Settings.fetch.playerName, "control-");
                        }
                        //Reset the send time of the vessel we just switched to
                        serverVesselsProtoUpdate[FlightGlobals.fetch.activeVessel.id.ToString()] = 0f;
                        //Nobody else is flying the vessel - let's take it
                        PlayerStatusWorker.fetch.myPlayerStatus.vesselText = FlightGlobals.fetch.activeVessel.vesselName;
                        lastVesselID = FlightGlobals.fetch.activeVessel.id.ToString();
                    }
                    if (!LockSystem.fetch.LockExists("control-" + FlightGlobals.fetch.activeVessel.id.ToString()))
                    {
                        LockSystem.fetch.ThrottledAcquireLock("control-" + FlightGlobals.fetch.activeVessel.id.ToString());
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
                    DarkLog.Debug("Releasing " + lastVesselID + " - No longer in flight!");
                    LockSystem.fetch.ReleasePlayerLocksWithPrefix(Settings.fetch.playerName, "control-");
                    lastVesselID = "";
                    PlayerStatusWorker.fetch.myPlayerStatus.vesselText = "";
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
            if (FlightGlobals.fetch.activeVessel == null)
            {
                //We don't have an active vessel
                return;
            }
            if (!FlightGlobals.fetch.activeVessel.loaded || FlightGlobals.fetch.activeVessel.packed)
            {
                //We haven't loaded into the game yet
                return;
            }

            if (ModWorker.fetch.modControl != ModControlMode.DISABLED)
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
                        if (ModWorker.fetch.modControl == ModControlMode.ENABLED_STOP_INVALID_PART_SYNC)
                        {
                            bannedPartsMessage = ScreenMessages.PostScreenMessage("Active vessel contains the following banned parts, it will not be saved to the server:\n" + bannedPartsString, 2f, ScreenMessageStyle.UPPER_CENTER);
                        }
                        if (ModWorker.fetch.modControl == ModControlMode.ENABLED_STOP_INVALID_PART_LAUNCH)
                        {
                            bannedPartsMessage = ScreenMessages.PostScreenMessage("Active vessel contains the following banned parts, you will be unable to launch on this server:\n" + bannedPartsString, 2f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                }
            }

            if (isSpectating)
            {
                //Don't send updates in spectate mode
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

            if (isInSafetyBubble(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), FlightGlobals.fetch.activeVessel.mainBody))
            {
                //Don't send updates while in the safety bubble
                return;
            }

            if (LockSystem.fetch.LockIsOurs("control-" + FlightGlobals.fetch.activeVessel.id.ToString()))
            {
                SendVesselUpdateIfNeeded(FlightGlobals.fetch.activeVessel);
            }
            SortedList<double, Vessel> secondryVessels = new SortedList<double, Vessel>();

            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                //Only update the vessel if it's loaded and unpacked (not on rails). Skip our vessel.
                if (checkVessel.loaded && !checkVessel.packed && (checkVessel.id.ToString() != FlightGlobals.fetch.activeVessel.id.ToString()) && (checkVessel.state != Vessel.State.DEAD))
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
            if (ModWorker.fetch.modControl != ModControlMode.DISABLED)
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

            if (checkVessel.state == Vessel.State.DEAD)
            {
                //Don't send dead vessels
                return;
            }

            if (checkVessel.vesselType == VesselType.Flag && checkVessel.id == Guid.Empty && checkVessel.vesselName != "Flag")
            {
                DarkLog.Debug("Fixing flag GUID for " + checkVessel.vesselName);
                checkVessel.id = Guid.NewGuid();
            }

            //Only send updates for craft we have update locks for. Request the lock if it's not taken.
            if (!LockSystem.fetch.LockExists("update-" + checkVessel.id.ToString()))
            {
                LockSystem.fetch.ThrottledAcquireLock("update-" + checkVessel.id.ToString());
                //Wait until we have the update lock
                return;
            }

            //Take the update lock off another player if we have the control lock and it's our vessel
            if (checkVessel.id.ToString() == FlightGlobals.fetch.activeVessel.id.ToString())
            {
                if (LockSystem.fetch.LockExists("update-" + checkVessel.id.ToString()) && !LockSystem.fetch.LockIsOurs("update-" + checkVessel.id.ToString()) && LockSystem.fetch.LockIsOurs("control-" + checkVessel.id.ToString()))
                {
                    LockSystem.fetch.ThrottledAcquireLock("update-" + checkVessel.id.ToString());
                    //Wait until we have the update lock
                    return;
                }
            }

            //Send updates for unpacked vessels that aren't being flown by other players
            bool notRecentlySentProtoUpdate = serverVesselsProtoUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsProtoUpdate[checkVessel.id.ToString()]) > VESSEL_PROTOVESSEL_UPDATE_INTERVAL) : true;
            bool notRecentlySentPositionUpdate = serverVesselsPositionUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsPositionUpdate[checkVessel.id.ToString()]) > (1f / (float)DynamicTickWorker.fetch.sendTickRate)) : true;

            //Check that is hasn't been recently sent
            if (notRecentlySentProtoUpdate)
            {

                ProtoVessel checkProto = new ProtoVessel(checkVessel);
                //TODO: Fix sending of flying vessels.
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
                                    NetworkWorker.fetch.SendKerbalProtoMessage(HighLogic.CurrentGame.CrewRoster.IndexOf(pcm), pcm);
                                }
                                else
                                {
                                    bool kerbalDifferent = false;
                                    kerbalDifferent = (pcm.name != serverKerbals[kerbalID].name) || kerbalDifferent;
                                    kerbalDifferent = (pcm.courage != serverKerbals[kerbalID].courage) || kerbalDifferent;
                                    kerbalDifferent = (pcm.isBadass != serverKerbals[kerbalID].isBadass) || kerbalDifferent;
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
                        bool isFlyingUpdate = (checkProto.situation == Vessel.Situations.FLYING);
                        NetworkWorker.fetch.SendVesselProtoMessage(checkProto, false, isFlyingUpdate);
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
                        if (LockSystem.fetch.LockExists("control-" + FlightGlobals.fetch.activeVessel.id.ToString()) && !LockSystem.fetch.LockIsOurs("control-" + FlightGlobals.fetch.activeVessel.id.ToString()))
                        {
                            spectateType = 1;
                            return true;
                        }
                        if (VesselUpdatedInFuture(FlightGlobals.fetch.activeVessel.id.ToString()))
                        {
                            spectateType = 2;
                            return true;
                        }
                        if (ModWorker.fetch.modControl == ModControlMode.ENABLED_STOP_INVALID_PART_LAUNCH)
                        {
                            if (vesselPartsOk.ContainsKey(FlightGlobals.fetch.activeVessel.id.ToString()) ? !vesselPartsOk[FlightGlobals.fetch.activeVessel.id.ToString()] : true)
                            {
                                //If mod control prevents invalid launches and our vessel has invalid parts, go into spectate mode.
                                spectateType = 3;
                                return true;
                            }
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
            CelestialBody kerbinBody = FlightGlobals.Bodies.Find(b => b.name == "Kerbin");
            if (kerbinBody == null)
            {
                //We don't know where the safety bubble is if kerbin doesn't exist, let's just disable it.
                return false;
            }
            if (protovessel == null)
            {
                DarkLog.Debug("isProtoVesselInSafetyBubble: protovessel is null!");
                return true;
            }
            if (protovessel.orbitSnapShot == null)
            {
                DarkLog.Debug("isProtoVesselInSafetyBubble: protovessel has no orbit snapshot!");
                return true;
            }
            //If not kerbin, we aren't in the safety bubble.
            if (protovessel.orbitSnapShot.ReferenceBodyIndex != FlightGlobals.Bodies.IndexOf(kerbinBody))
            {
                return false;
            }
            Vector3d protoVesselPosition = kerbinBody.GetWorldSurfacePosition(protovessel.latitude, protovessel.longitude, protovessel.altitude);
            return isInSafetyBubble(protoVesselPosition, kerbinBody);
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
                returnUpdate.rotation[0] = updateVessel.srfRelRotation.x;
                returnUpdate.rotation[1] = updateVessel.srfRelRotation.y;
                returnUpdate.rotation[2] = updateVessel.srfRelRotation.z;
                returnUpdate.rotation[3] = updateVessel.srfRelRotation.w;

                /*
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
                */

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
                    Vector3d srfVel = Quaternion.Inverse(updateVessel.mainBody.bodyTransform.rotation) * updateVessel.srf_velocity;
                    returnUpdate.velocity[0] = srfVel.x;
                    returnUpdate.velocity[1] = srfVel.y;
                    returnUpdate.velocity[2] = srfVel.z;
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
                    //KSP tells us a bunch of lies, there's a ~40 meter error between our actual position and the orbital position
                    Vector3d vesselPos = updateVessel.GetWorldPos3D();
                    Vector3d orbitPos = updateVessel.orbitDriver.orbit.getPositionAtUT(Planetarium.GetUniversalTime());
                    Vector3d positionDelta = Quaternion.Inverse(updateVessel.mainBody.bodyTransform.rotation) * (vesselPos - orbitPos);
                    returnUpdate.orbitalPositionDelta = new double[3];
                    returnUpdate.orbitalPositionDelta[0] = positionDelta.x;
                    returnUpdate.orbitalPositionDelta[1] = positionDelta.y;
                    returnUpdate.orbitalPositionDelta[2] = positionDelta.z;

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
                    protoCrew.rosterStatus = ProtoCrewMember.RosterStatus.AVAILABLE;
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
                            DarkLog.Debug("Loaded kerbal " + kerbalID + ", name: " + protoCrew.name);
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
                DodgeVesselActionGroups(ref vesselNode);

                //Can be used for debugging incoming vessel config nodes.
                //vesselNode.Save(Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("DMP-RX", Planetarium.GetUniversalTime() + ".txt")));
                ProtoVessel currentProto = new ProtoVessel(vesselNode, HighLogic.CurrentGame);

                if (currentProto != null)
                {
                    //Skip already loaded EVA's
                    if ((currentProto.vesselType == VesselType.EVA) && (FlightGlobals.fetch.vessels.Find(v => v.id == currentProto.vesselID) != null))
                    {
                        return;
                    }

                    //Register asteroids from other players
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

                    //Skip vessels that try to load in the safety bubble
                    if (isProtoVesselInSafetyBubble(currentProto))
                    {
                        DarkLog.Debug("Skipped loading protovessel " + currentProto.vesselID.ToString() + ", name: " + currentProto.vesselName + " because it is inside the safety bubble");
                        return;
                    }

                    //Skip flying vessel that are too far away
                    bool usingHackyAtmoLoad = false;
                    if (currentProto.situation == Vessel.Situations.FLYING)
                    {
                        DarkLog.Debug("Got a flying update for " + currentProto.vesselID + ", name: " + currentProto.vesselName);
                        if (currentProto.orbitSnapShot == null)
                        {
                            DarkLog.Debug("Skipping flying vessel load - Protovessel does not have an orbit snapshot");
                            return;
                        }
                        CelestialBody updateBody = FlightGlobals.fetch.bodies[currentProto.orbitSnapShot.ReferenceBodyIndex];
                        if (updateBody == null)
                        {
                            DarkLog.Debug("Skipping flying vessel load - Could not find celestial body index " + currentProto.orbitSnapShot.ReferenceBodyIndex);
                            return;
                        }
                        bool willGetKilledInAtmo = false;
                        if (updateBody.atmosphere)
                        {
                            double atmoPressure = updateBody.staticPressureASL * Math.Pow(Math.E, ((-currentProto.altitude) / (updateBody.atmosphereScaleHeight * 1000)));
                            //KSP magic cut off limit for killing vessels. Works out to be ~23km on kerbin.
                            if (atmoPressure > 0.01f)
                            {
                                willGetKilledInAtmo = true;
                            }
                        }
                        if (willGetKilledInAtmo)
                        {
                            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                            {
                                if (FlightGlobals.fetch.activeVessel == null)
                                {
                                    DarkLog.Debug("Skipping flying vessel load - We do not have an active vessel");
                                    return;
                                }
                                if (FlightGlobals.fetch.activeVessel.mainBody != updateBody)
                                {
                                    DarkLog.Debug("Skipping flying vessel load - We are on a different celestial body");
                                    return;
                                }
                                Vector3d ourPos = FlightGlobals.fetch.activeVessel.mainBody.GetWorldSurfacePosition(FlightGlobals.fetch.activeVessel.latitude, FlightGlobals.fetch.activeVessel.longitude, FlightGlobals.fetch.activeVessel.altitude);
                                Vector3d protoPos = updateBody.GetWorldSurfacePosition(currentProto.latitude, currentProto.longitude, currentProto.altitude);
                                double distance = Vector3d.Distance(ourPos, protoPos);
                                //We'll load the vessel if we are within half the loading distance
                                if (distance > (Vessel.loadDistance / 2f))
                                {
                                    DarkLog.Debug("Skipping flying vessel load - We are not close enough, distance: " + distance);
                                    return;
                                }
                                else
                                {
                                    DarkLog.Debug("Enabling FLYING vessel load!");
                                    //If the vessel is landed it won't be killed by the atmosphere
                                    currentProto.landed = true;
                                    usingHackyAtmoLoad = true;
                                }
                            }
                            else
                            {
                                DarkLog.Debug("Skipping flying vessel load - We cannot load vessels that will get killed in atmosphere while not in flight");
                                return;
                            }
                        }
                    }

                    RegisterServerVessel(currentProto.vesselID.ToString());
                    DarkLog.Debug("Loading " + currentProto.vesselID + ", name: " + currentProto.vesselName + ", type: " + currentProto.vesselType);

                    foreach (ProtoPartSnapshot part in currentProto.protoPartSnapshots)
                    {
                        //This line doesn't actually do anything useful, but if you get this reference, you're officially the most geeky person darklight knows.
                        part.temperature = ((part.temperature + 273.15f) * 0.8f) - 273.15f;

                        //Fix up flag URLS.
                        if (part.flagURL.Length != 0)
                        {
                            string flagFile = Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "GameData"), part.flagURL + ".png");
                            if (!File.Exists(flagFile))
                            {
                                DarkLog.Debug("Flag '" + part.flagURL + "' doesn't exist, setting to default!");
                                part.flagURL = "Squad/Flags/default";
                            }
                        }
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
                        wasActive = (FlightGlobals.fetch.activeVessel != null) ? (FlightGlobals.fetch.activeVessel.id == currentProto.vesselID) : false;
                    }

                    for (int vesselID = FlightGlobals.fetch.vessels.Count - 1; vesselID >= 0; vesselID--)
                    {
                        Vessel oldVessel = FlightGlobals.fetch.vessels[vesselID];
                        if (oldVessel.id.ToString() == currentProto.vesselID.ToString())
                        {
                            //Don't replace the vessel if it's unpacked, not landed, close to the ground, and has the same amount of parts.
                            double hft = oldVessel.GetHeightFromTerrain();
                            if (oldVessel.loaded && !oldVessel.packed && !oldVessel.Landed && (hft < 1000) && (currentProto.protoPartSnapshots.Count == oldVessel.parts.Count))
                            {
                                return;
                            }
                            //Don't kill the active vessel - Kill it after we switch.
                            //Killing the active vessel causes all sorts of crazy problems.
                            if (wasActive)
                            {
                                delayKillVessels.Add(oldVessel);
                            }
                            else
                            {
                                KillVessel(oldVessel);
                            }
                        }
                    }

                    serverVesselsProtoUpdate[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    lastLoadVessel[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    currentProto.Load(HighLogic.CurrentGame.flightState);

                    if (currentProto.vesselRef != null)
                    {
                        if (usingHackyAtmoLoad)
                        {
                            HackyFlyingVesselLoad hfvl = new HackyFlyingVesselLoad();
                            hfvl.flyingVessel = currentProto.vesselRef;
                            hfvl.loadTime = UnityEngine.Time.realtimeSinceStartup;
                            loadingFlyingVessels.Enqueue(hfvl);
                        }

                        if (wasActive)
                        {
                            DarkLog.Debug("ProtoVessel update for active vessel!");
                            try
                            {
                                OrbitPhysicsManager.HoldVesselUnpack(5);
                                FlightGlobals.fetch.activeVessel.GoOnRails();
                                //Put our vessel on rails so we don't collide with the new copy
                            }
                            catch
                            {
                                DarkLog.Debug("WARNING: Something very bad happened trying to replace the vessel, skipping update!");
                                return;
                            }
                            newActiveVessel = currentProto.vesselRef;
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

        private void DodgeVesselActionGroups(ref ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                ConfigNode actiongroupNode = vesselNode.GetNode("ACTIONGROUPS");
                if (actiongroupNode != null)
                {
                    foreach (string keyName in actiongroupNode.values.DistinctNames())
                    {
                        string valueCurrent = actiongroupNode.GetValue(keyName);
                        string valueDodge = DodgeValueIfNeeded(valueCurrent);
                        if (valueCurrent != valueDodge)
                        {
                            DarkLog.Debug("Dodged actiongroup " + keyName);
                            actiongroupNode.SetValue(keyName, valueDodge);
                        }
                    }
                }
            }
        }

        private string DodgeValueIfNeeded(string input)
        {
            string boolValue = input.Substring(0, input.IndexOf(", "));
            string timeValue = input.Substring(input.IndexOf(", ") + 1);
            double vesselPlanetTime = Double.Parse(timeValue);
            double currentPlanetTime = Planetarium.GetUniversalTime();
            if (vesselPlanetTime > currentPlanetTime)
            {
                return boolValue + ", " + currentPlanetTime;
            }
            return input;
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
            if (dyingVessel.state != Vessel.State.DEAD)
            {
                //This is how we can make KSP tell the truth about when a vessel is really dead!
                return;
            }
            if (VesselRecentlyLoaded(dyingVesselID))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", vessel has been recently loaded.");
                return;
            }
            if (VesselRecentlyKilled(dyingVesselID))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", vessel has been recently killed.");
                return;
            }
            if (VesselUpdatedInFuture(dyingVesselID))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", vessel has been changed in the future.");
                return;
            }

            //Don't remove the vessel if it's not owned by another player.
            if (LockSystem.fetch.LockExists("update-" + dyingVesselID) || !LockSystem.fetch.LockIsOurs("update-" + dyingVesselID))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", update lock owned by another player.");
                return;
            }

            if (!serverVessels.Contains(dyingVessel.id.ToString()))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", not a server vessel.");
                return;
            }

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

        public void OnVesselRecovered(ProtoVessel recoveredVessel)
        {
            string recoveredVesselID = recoveredVessel.vesselID.ToString();

            if (LockSystem.fetch.LockExists("control-" + recoveredVesselID) && !LockSystem.fetch.LockIsOurs("control-" + recoveredVesselID))
            {
                ScreenMessages.PostScreenMessage("Cannot recover vessel, the vessel is in use.", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (VesselUpdatedInFuture(recoveredVesselID))
            {
                ScreenMessages.PostScreenMessage("Cannot recover vessel, the vessel been changed in the future.", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (!serverVessels.Contains(recoveredVesselID))
            {
                DarkLog.Debug("Cannot recover a non-server vessel!");
                return;
            }

            DarkLog.Debug("Removing vessel " + recoveredVesselID + ", name: " + recoveredVessel.vesselName + " from the server: Recovered");
            unassignKerbals(recoveredVesselID);
            serverVessels.Remove(recoveredVesselID);
            NetworkWorker.fetch.SendVesselRemove(recoveredVesselID, false);
        }

        public void OnVesselTerminated(ProtoVessel terminatedVessel)
        {
            string terminatedVesselID = terminatedVessel.vesselID.ToString();
            //Check the vessel hasn't been changed in the future
            if (LockSystem.fetch.LockExists("control-" + terminatedVesselID) && !LockSystem.fetch.LockIsOurs("control-" + terminatedVesselID))
            {
                ScreenMessages.PostScreenMessage("Cannot terminate vessel, the vessel is in use.", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (VesselUpdatedInFuture(terminatedVesselID))
            {
                ScreenMessages.PostScreenMessage("Cannot terminate vessel, the vessel been changed in the future.", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (!serverVessels.Contains(terminatedVesselID))
            {
                DarkLog.Debug("Cannot terminate a non-server vessel!");
                return;
            }

            DarkLog.Debug("Removing vessel " + terminatedVesselID + ", name: " + terminatedVessel.vesselName + " from the server: Terminated");
            unassignKerbals(terminatedVesselID);
            serverVessels.Remove(terminatedVesselID);
            NetworkWorker.fetch.SendVesselRemove(terminatedVesselID, false);
        }

        private bool VesselRecentlyLoaded(string vesselID)
        {
            return lastLoadVessel.ContainsKey(vesselID) ? ((UnityEngine.Time.realtimeSinceStartup - lastLoadVessel[vesselID]) < 10f) : false;
        }

        private bool VesselRecentlyKilled(string vesselID)
        {
            return lastKillVesselDestroy.ContainsKey(vesselID) ? ((UnityEngine.Time.realtimeSinceStartup - lastKillVesselDestroy[vesselID]) < 10f) : false;
        }

        private bool VesselUpdatedInFuture(string vesselID)
        {
            return latestVesselUpdate.ContainsKey(vesselID) ? ((latestVesselUpdate[vesselID] + 10f) > Planetarium.GetUniversalTime()) : false;
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
                if (LockSystem.fetch.LockExists("control-" + FlightGlobals.fetch.activeVessel.id.ToString()))
                {
                    isSpectatorDocking = true;
                    spectatorDockingPlayer = LockSystem.fetch.LockOwner("control-" + FlightGlobals.fetch.activeVessel.id.ToString());
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
                //Add it to the delay kill list to make sure it dies.
                //With KSP, If it doesn't work first time, keeping doing it until it does.
                if (!delayKillVessels.Contains(killVessel))
                {
                    delayKillVessels.Add(killVessel);
                }
                lastKillVesselDestroy[killVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                try
                {
                    if (killVessel.parts != null)
                    {
                        for (int partID = killVessel.parts.Count - 1; partID >= 0; partID--)
                        {
                            Part killPart = killVessel.parts[partID];
                            killPart.explosionPotential = 0f;
                            killPart.Die();
                        }
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
            //Ignore updates to our own vessel if we are in flight and we aren't spectating
            if (!isSpectating && (FlightGlobals.fetch.activeVessel != null ? FlightGlobals.fetch.activeVessel.id.ToString() == update.vesselID : false) && HighLogic.LoadedScene == GameScenes.FLIGHT)
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
                //Get the new position/velocity
                Vector3d updatePostion = updateBody.GetWorldSurfacePosition(update.position[0], update.position[1], update.position[2]);
                Vector3d updateVelocity = updateBody.bodyTransform.rotation * new Vector3d(update.velocity[0], update.velocity[1], update.velocity[2]);
                //Figure out how far away we are if we can
                double updateDistance = Double.PositiveInfinity;
                if ((HighLogic.LoadedScene == GameScenes.FLIGHT) && (FlightGlobals.fetch.activeVessel != null))
                {
                    if (FlightGlobals.fetch.activeVessel.mainBody == updateVessel.mainBody)
                    {
                        Vector3d ourPos = FlightGlobals.fetch.activeVessel.mainBody.GetWorldSurfacePosition(FlightGlobals.fetch.activeVessel.latitude, FlightGlobals.fetch.activeVessel.longitude, FlightGlobals.fetch.activeVessel.altitude);
                        Vector3d theirPos = updateVessel.mainBody.GetWorldSurfacePosition(updateVessel.latitude, updateVessel.longitude, updateVessel.altitude);
                        updateDistance = Vector3.Distance(ourPos, theirPos);
                    }
                }
                bool isLoading = (updateDistance < Vessel.loadDistance) && !updateVessel.loaded;
                bool isUnpacking = (updateDistance < updateVessel.distanceUnpackThreshold) && updateVessel.packed && updateVessel.loaded;
                if (updateVessel.HoldPhysics)
                {
                    DarkLog.Debug("Skipping update, physics held");
                    return;
                }
                //Don't set the position while the vessel is unpacking / loading.
                if (isUnpacking || isLoading)
                {
                    DarkLog.Debug("Skipping update, isUnpacking: " + isUnpacking + " isLoading: " + isLoading);
                    return;
                }
                if (updateVessel.packed)
                {
                    updateVessel.latitude = update.position[0];
                    updateVessel.longitude = update.position[1];
                    updateVessel.altitude = update.position[2];
                    Vector3d orbitalPos = updatePostion - updateBody.position;
                    Vector3d surfaceOrbitVelDiff = updateBody.getRFrmVel(updatePostion);
                    Vector3d orbitalVel = updateVelocity + surfaceOrbitVelDiff;
                    updateVessel.orbitDriver.orbit.UpdateFromStateVectors(orbitalPos.xzy, orbitalVel.xzy, updateBody, Planetarium.GetUniversalTime());
                }
                else
                {
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
                    Vector3d ourCoMDiff = Vector3d.zero;
                    if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.fetch.activeVessel != null)
                    {
                            ourCoMDiff = FlightGlobals.fetch.activeVessel.findWorldCenterOfMass() - FlightGlobals.fetch.activeVessel.GetWorldPos3D();
                    }
                    //KSP's inaccuracy hurts me.
                    Vector3d orbitalPositionDelta = updateBody.bodyTransform.rotation * new Vector3d(update.orbitalPositionDelta[0], update.orbitalPositionDelta[1], update.orbitalPositionDelta[2]);
                    updateVessel.SetPosition(updateOrbit.getPositionAtUT(Planetarium.GetUniversalTime()) + orbitalPositionDelta + ourCoMDiff);
                    Vector3d velocityOffset = updateOrbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime()).xzy - updateVessel.orbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime()).xzy;
                    updateVessel.ChangeWorldVelocity(velocityOffset);
                }

            }
            Quaternion updateRotation = new Quaternion(update.rotation[0], update.rotation[1], update.rotation[2], update.rotation[3]);
            updateVessel.SetRotation(updateVessel.mainBody.bodyTransform.rotation * updateRotation);

            Vector3 angularVelocity = new Vector3(update.angularVelocity[0], update.angularVelocity[1], update.angularVelocity[2]);

            if (updateVessel.LandedOrSplashed)
            {
                updateVessel.angularVelocity = Vector3.zero;
                if (updateVessel.rootPart != null && updateVessel.rootPart.rb != null)
                {
                    updateVessel.rootPart.rb.angularVelocity = Vector3.zero;
                }
            }
            else
            {
                updateVessel.angularVelocity = angularVelocity;
                if (updateVessel.parts != null)
                {
                    Vector3 newAng = updateVessel.ReferenceTransform.rotation * angularVelocity;
                    foreach (Part vesselPart in updateVessel.parts)
                    {
                        if (vesselPart.rb != null && vesselPart.State == PartStates.ACTIVE)
                        {
                            vesselPart.rb.angularVelocity = newAng;
                        }
                    }
                }
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
                    if (!vesselProtoQueue.ContainsKey(vesselID))
                    {
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
                        if (singleton.wasSpectating)
                        {
                            InputLockManager.RemoveControlLock(DARK_SPECTATE_LOCK);
                        }
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

    class HackyFlyingVesselLoad
    {
        public double loadTime;
        public bool requestedToGoOffRails = false;
        public Vessel flyingVessel;
    }

    public class VesselUpdate
    {
        public string vesselID;
        public double planetTime;
        public string bodyName;
        //Found out KSP's magic rotation setting by browsing Vessel.FixedUpdate()
        public float[] rotation;
        //public float[] vesselForward;
        //public float[] vesselUp;
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
        //KSP tells us a bunch of fibs. Lets keep it honest.
        public double[] orbitalPositionDelta;
    }
}

