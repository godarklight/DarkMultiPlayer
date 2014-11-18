using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        private Dictionary<string, Queue<KerbalEntry>> kerbalProtoQueue = new Dictionary<string, Queue<KerbalEntry>>();
        private Queue<ActiveVesselEntry> newActiveVessels = new Queue<ActiveVesselEntry>();
        private List<string> serverVessels = new List<string>();
        private Dictionary<string, bool> vesselPartsOk = new Dictionary<string, bool>();
        //Vessel state tracking
        private string lastVesselID;
        private Dictionary <string, int> vesselPartCount = new Dictionary<string, int>();
        private Dictionary <string, string> vesselNames = new Dictionary<string, string>();
        private Dictionary <string, VesselType> vesselTypes = new Dictionary<string, VesselType>();
        private Dictionary <string, Vessel.Situations> vesselSituations = new Dictionary<string, Vessel.Situations>();
        //Part tracking
        private Dictionary<string, string> vesselParts = new Dictionary<string, string>();
        //Known kerbals
        private Dictionary<string, ProtoCrewMember> serverKerbals = new Dictionary<string, ProtoCrewMember>();
        private Dictionary<string, string> assignedKerbals = new Dictionary<string, string>();
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
        private List<HackyFlyingVesselLoad> loadingFlyingVessels = new List<HackyFlyingVesselLoad>();
        private List<HackyFlyingVesselLoad> loadingFlyingVesselsDeleteList = new List<HackyFlyingVesselLoad>();
        //Docking related
        private Vessel newActiveVessel;
        private int activeVesselLoadUpdates;
        private string fromDockedVesselID;
        private string toDockedVesselID;
        private bool sentDockingDestroyUpdate;
        private bool isSpectatorDocking;
        private string spectatorDockingPlayer;
        private string spectatorDockingID;
        //System.Reflection hackiness for loading kerbals into the crew roster:
        private delegate bool AddCrewMemberToRosterDelegate(ProtoCrewMember pcm);

        private AddCrewMemberToRosterDelegate AddCrewMemberToRoster;

        public static VesselWorker fetch
        {
            get
            {
                return singleton;
            }
        }
        //Called from main
        public void Update()
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                return;
            }

            if (Time.timeSinceLevelLoad < 1f)
            {
                return;
            }

            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                if (!FlightGlobals.ready)
                {
                    return;
                }
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
                foreach (HackyFlyingVesselLoad hfvl in loadingFlyingVessels)
                {
                    if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                    {
                        //Scene change
                        DarkLog.Debug("Hacky load failed for " + hfvl.flyingVessel.id.ToString() + ", failed to load in time");
                        KillVessel(hfvl.flyingVessel);
                        loadingFlyingVesselsDeleteList.Add(hfvl);
                        continue;
                    }
                    if (!FlightGlobals.fetch.vessels.Contains(hfvl.flyingVessel))
                    {
                        //Vessel failed to load
                        DarkLog.Debug("Hacky load failed for " + hfvl.flyingVessel.id.ToString() + ", killed in atmo");
                        loadingFlyingVesselsDeleteList.Add(hfvl);
                        continue;
                    }
                    if (FlightGlobals.fetch.activeVessel != null)
                    {
                        double ourDistance = Vector3d.Distance(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), hfvl.flyingVessel.GetWorldPos3D());
                        if (ourDistance > hfvl.flyingVessel.distanceLandedUnpackThreshold)
                        {
                            DarkLog.Debug("Hacky load failed, distance: " + ourDistance + ", max: " + hfvl.flyingVessel.distanceUnpackThreshold);
                            KillVessel(hfvl.flyingVessel);
                            loadingFlyingVesselsDeleteList.Add(hfvl);
                            continue;
                        }
                    }
                    //Everything is ok, attempt to load.
                    if ((UnityEngine.Time.realtimeSinceStartup - hfvl.loadTime) < 10f)
                    {
                        if (hfvl.flyingVessel.loaded)
                        {
                            if (hfvl.flyingVessel.packed)
                            {
                                if (((UnityEngine.Time.realtimeSinceStartup - hfvl.loadTime) > 5f) && ((UnityEngine.Time.realtimeSinceStartup - hfvl.unpackTime) > 0.5f))
                                {
                                    //Ask to go off rails 5 seconds after loading
                                    hfvl.unpackTime = UnityEngine.Time.realtimeSinceStartup;
                                    DarkLog.Debug("Asking vessel to go off rails");
                                    hfvl.flyingVessel.GoOffRails();
                                }
                            }
                            else
                            {
                                if ((UnityEngine.Time.realtimeSinceStartup - hfvl.unpackTime) > 0.5f)
                                {
                                    //Vessel is off rails 1 second after asking, things must have worked!
                                    hfvl.flyingVessel.Landed = false;
                                    hfvl.flyingVessel.Splashed = false;
                                    hfvl.flyingVessel.landedAt = "";
                                    hfvl.flyingVessel.situation = Vessel.Situations.FLYING;
                                    DarkLog.Debug("Hacky load successful for " + hfvl.flyingVessel.id.ToString());
                                    loadingFlyingVesselsDeleteList.Add(hfvl);
                                }
                            }
                        }
                    }
                    else
                    {
                        //Timed out
                        DarkLog.Debug("Hacky load failed for " + hfvl.flyingVessel.id.ToString() + ", failed to load in time");
                        KillVessel(hfvl.flyingVessel);
                        loadingFlyingVesselsDeleteList.Add(hfvl);
                    }
                }

                foreach (HackyFlyingVesselLoad hfvl in loadingFlyingVesselsDeleteList)
                {
                    loadingFlyingVessels.Remove(hfvl);
                }
                loadingFlyingVesselsDeleteList.Clear();

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
            GameEvents.onVesselCreate.Add(this.OnVesselCreate);
            GameEvents.onVesselLoaded.Add(this.OnVesselLoaded);
            GameEvents.onPartDestroyed.Add(this.OnPartDestroy);
            GameEvents.onPartCouple.Add(this.OnVesselDock);
            GameEvents.onCrewBoardVessel.Add(this.OnCrewBoard);
        }

        private void UnregisterGameHooks()
        {
            registered = false;
            GameEvents.onVesselRecovered.Remove(this.OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(this.OnVesselTerminated);
            GameEvents.onVesselDestroy.Remove(this.OnVesselDestroyed);
            GameEvents.onVesselCreate.Remove(this.OnVesselCreate);
            GameEvents.onVesselLoaded.Remove(this.OnVesselLoaded);
            GameEvents.onPartDestroyed.Remove(this.OnPartDestroy);
            GameEvents.onPartCouple.Remove(this.OnVesselDock);
            GameEvents.onCrewBoardVessel.Remove(this.OnCrewBoard);
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

            foreach (KeyValuePair<string, Queue<KerbalEntry>> kerbalProtoSubspace in kerbalProtoQueue)
            {
                while (kerbalProtoSubspace.Value.Count > 0 ? (kerbalProtoSubspace.Value.Peek().planetTime < Planetarium.GetUniversalTime()) : false)
                {
                    KerbalEntry kerbalEntry = kerbalProtoSubspace.Value.Dequeue();
                    LoadKerbal(kerbalEntry.kerbalNode);
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
                    vu.Apply();
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
                            spectateMessage = ScreenMessages.PostScreenMessage("This vessel is controlled by another player.", UPDATE_SCREEN_MESSAGE_INTERVAL * 2, ScreenMessageStyle.UPPER_CENTER);
                            break;
                        case 2:
                            spectateMessage = ScreenMessages.PostScreenMessage("This vessel has been changed in the future.", UPDATE_SCREEN_MESSAGE_INTERVAL * 2, ScreenMessageStyle.UPPER_CENTER);
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
                                //If there's 2 vessels at the exact same distance.
                                if (!secondryVessels.ContainsKey(currentDistance) && !secondryVessels.ContainsValue(checkVessel))
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
                                if (!serverKerbals.ContainsKey(pcm.name))
                                {
                                    //New kerbal
                                    DarkLog.Debug("Found new kerbal, sending...");
                                    serverKerbals[pcm.name] = new ProtoCrewMember(pcm);
                                    NetworkWorker.fetch.SendKerbalProtoMessage(pcm);
                                }
                                else
                                {
                                    bool kerbalDifferent = false;
                                    kerbalDifferent = (pcm.name != serverKerbals[pcm.name].name) || kerbalDifferent;
                                    kerbalDifferent = (pcm.courage != serverKerbals[pcm.name].courage) || kerbalDifferent;
                                    kerbalDifferent = (pcm.isBadass != serverKerbals[pcm.name].isBadass) || kerbalDifferent;
                                    kerbalDifferent = (pcm.seatIdx != serverKerbals[pcm.name].seatIdx) || kerbalDifferent;
                                    kerbalDifferent = (pcm.stupidity != serverKerbals[pcm.name].stupidity) || kerbalDifferent;
                                    kerbalDifferent = (pcm.UTaR != serverKerbals[pcm.name].UTaR) || kerbalDifferent;
                                    if (kerbalDifferent)
                                    {
                                        DarkLog.Debug("Found changed kerbal, sending...");
                                        NetworkWorker.fetch.SendKerbalProtoMessage(pcm);
                                        serverKerbals[pcm.name].name = pcm.name;
                                        serverKerbals[pcm.name].courage = pcm.courage;
                                        serverKerbals[pcm.name].isBadass = pcm.isBadass;
                                        serverKerbals[pcm.name].rosterStatus = pcm.rosterStatus;
                                        serverKerbals[pcm.name].seatIdx = pcm.seatIdx;
                                        serverKerbals[pcm.name].stupidity = pcm.stupidity;
                                        serverKerbals[pcm.name].UTaR = pcm.UTaR;
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
                VesselUpdate update = VesselUpdate.CopyFromVessel(checkVessel);
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
            
        //Called from main
        public void LoadKerbalsIntoGame()
        {
            DarkLog.Debug("Loading kerbals into game");
            MethodInfo addMemberToCrewRosterMethod = typeof(KerbalRoster).GetMethod("AddCrewMember", BindingFlags.NonPublic | BindingFlags.Instance);
            AddCrewMemberToRoster = (AddCrewMemberToRosterDelegate)Delegate.CreateDelegate(typeof(AddCrewMemberToRosterDelegate), HighLogic.CurrentGame.CrewRoster, addMemberToCrewRosterMethod);
            if (AddCrewMemberToRoster == null)
            {
                throw new Exception("Failed to load AddCrewMember delegate!");
            }

            foreach (KeyValuePair<string, Queue<KerbalEntry>> kerbalQueue in kerbalProtoQueue)
            {
                while (kerbalQueue.Value.Count > 0)
                {
                    KerbalEntry kerbalEntry = kerbalQueue.Value.Dequeue();
                    LoadKerbal(kerbalEntry.kerbalNode);
                }
            }

            if (serverKerbals.Count == 0)
            {
                KerbalRoster newRoster = KerbalRoster.GenerateInitialCrewRoster();
                foreach (ProtoCrewMember pcm in newRoster.Crew)
                {
                    AddCrewMemberToRoster(pcm);
                    serverKerbals[pcm.name] = new ProtoCrewMember(pcm);
                    NetworkWorker.fetch.SendKerbalProtoMessage(pcm);
                }
            }

            int generateKerbals = 0;
            if (serverKerbals.Count < 20)
            {
                generateKerbals = 20 - serverKerbals.Count;
                DarkLog.Debug("Generating " + generateKerbals + " new kerbals");
            }

            while (generateKerbals > 0)
            {
                ProtoCrewMember protoKerbal = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Crew);
                if (!HighLogic.CurrentGame.CrewRoster.Exists(protoKerbal.name))
                {
                    ProtoCrewMember newKerbal = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                    AddCrewMemberToRoster(newKerbal);
                    serverKerbals[protoKerbal.name] = new ProtoCrewMember(protoKerbal);
                    NetworkWorker.fetch.SendKerbalProtoMessage(protoKerbal);
                    generateKerbals--;
                }
            }
            DarkLog.Debug("Kerbals loaded");
        }

        private void LoadKerbal(ConfigNode crewNode)
        {
            if (crewNode != null)
            {
                ProtoCrewMember protoCrew = new ProtoCrewMember(crewNode);
                if (protoCrew != null)
                {
                    if (!String.IsNullOrEmpty(protoCrew.name))
                    {
                        protoCrew.type = ProtoCrewMember.KerbalType.Crew;
                        if (assignedKerbals.ContainsKey(protoCrew.name))
                        {
                            protoCrew.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                        }
                        else
                        {
                            protoCrew.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                        }
                        if (!HighLogic.CurrentGame.CrewRoster.Exists(protoCrew.name))
                        {
                            AddCrewMemberToRoster(protoCrew);
                            serverKerbals[protoCrew.name] = (new ProtoCrewMember(protoCrew));
                        }
                        else
                        {
                            HighLogic.CurrentGame.CrewRoster[protoCrew.name].name = protoCrew.name;
                            HighLogic.CurrentGame.CrewRoster[protoCrew.name].courage = protoCrew.courage;
                            HighLogic.CurrentGame.CrewRoster[protoCrew.name].isBadass = protoCrew.isBadass;
                            HighLogic.CurrentGame.CrewRoster[protoCrew.name].rosterStatus = protoCrew.rosterStatus;
                            HighLogic.CurrentGame.CrewRoster[protoCrew.name].seatIdx = protoCrew.seatIdx;
                            HighLogic.CurrentGame.CrewRoster[protoCrew.name].stupidity = protoCrew.stupidity;
                            HighLogic.CurrentGame.CrewRoster[protoCrew.name].UTaR = protoCrew.UTaR;
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
            DarkLog.Debug("Loading vessels into game");
            int numberOfLoads = 0;

            foreach (KeyValuePair<string, Queue<VesselProtoUpdate>> vesselQueue in vesselProtoQueue)
            {
                while (vesselQueue.Value.Count > 0)
                {
                    ConfigNode currentNode = vesselQueue.Value.Dequeue().vesselNode;
                    if (currentNode != null)
                    {
                        DodgeVesselActionGroups(currentNode);
                        DodgeVesselCrewValues(currentNode);
                        RemoveManeuverNodesFromProtoVessel(currentNode);
                        ProtoVessel pv = new ProtoVessel(currentNode, HighLogic.CurrentGame);
                        if (pv != null)
                        {
                            bool protovesselIsOk = true;
                            try
                            {
                                ConfigNode cn = new ConfigNode();
                                pv.Save(cn);
                            }
                            catch
                            {
                                DarkLog.Debug("WARNING: Protovessel " + pv.vesselID + ", name: " + pv.vesselName + " is DAMAGED!. Skipping load.");
                                ChatWorker.fetch.PMMessageServer("WARNING: Protovessel " + pv.vesselID + ", name: " + pv.vesselName + " is DAMAGED!. Skipping load.");
                                protovesselIsOk = false;
                            }
                            if (protovesselIsOk)
                            {
                                RegisterServerVessel(pv.vesselID.ToString());
                                RegisterServerAsteriodIfVesselIsAsteroid(pv);
                                HighLogic.CurrentGame.flightState.protoVessels.Add(pv);
                                numberOfLoads++;
                            }
                        }
                    }
                }
            }
            DarkLog.Debug("Vessels (" + numberOfLoads + ") loaded into game");
        }
        //Also called from QuickSaveLoader
        public void LoadVessel(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                //Fix crew value numbers to Kerbal Names
                bool kerbalsDodged = DodgeVesselCrewValues(vesselNode);

                //Fix the "cannot control actiongroups bug" by dodging the last used time.
                DodgeVesselActionGroups(vesselNode);

                //Fix a bug where maneuver nodes make KSP throw an NRE flood.
                RemoveManeuverNodesFromProtoVessel(vesselNode);

                //Can be used for debugging incoming vessel config nodes.
                //vesselNode.Save(Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("DMP-RX", Planetarium.GetUniversalTime() + ".txt")));
                ProtoVessel currentProto = new ProtoVessel(vesselNode, HighLogic.CurrentGame);

                if (kerbalsDodged && (NetworkWorker.fetch.state == ClientState.STARTING) && !LockSystem.fetch.LockExists("control-" + currentProto.vesselID) && !LockSystem.fetch.LockExists("update-" + currentProto.vesselID))
                {
                    DarkLog.Debug("Sending kerbal-dodged vessel " + currentProto.vesselID + ", name: " + currentProto.vesselName);
                    NetworkWorker.fetch.SendVesselProtoMessage(currentProto, false, false);
                    foreach (ProtoPartSnapshot pps in currentProto.protoPartSnapshots)
                    {
                        if (pps.protoModuleCrew != null)
                        {
                            foreach (ProtoCrewMember pcm in pps.protoModuleCrew)
                            {
                                if (pcm != null)
                                {
                                    NetworkWorker.fetch.SendKerbalProtoMessage(pcm);
                                }
                            }
                        }
                    }
                }

                if (currentProto != null)
                {
                    //Skip already loaded EVA's
                    if ((currentProto.vesselType == VesselType.EVA) && (FlightGlobals.fetch.vessels.Find(v => v.id == currentProto.vesselID) != null))
                    {
                        return;
                    }

                    RegisterServerAsteriodIfVesselIsAsteroid(currentProto);

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
                                if ((FlightGlobals.fetch.vessels.Find(v => v.id == currentProto.vesselID) != null) && vesselPartCount.ContainsKey(currentProto.vesselID.ToString()) ? currentProto.protoPartSnapshots.Count == vesselPartCount[currentProto.vesselID.ToString()] : false)
                                {
                                    DarkLog.Debug("Skipping flying vessel load - Vessel has the same part count");
                                    return;
                                }
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
                                //We'll load the vessel if possible
                                if (distance > Vessel.loadDistance)
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
                        if (currentProto.vesselType != VesselType.EVA)
                        {
                            //We want to be cool.
                            part.temperature = ((part.temperature + 273.15f) * 0.8f) - 273.15f;
                        }
                        else
                        {
                            //But not so cool that we show off.
                            part.temperature = ((part.temperature + 273.15f) * 1.2f) - 273.15f;
                        }

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
                            if (oldVessel.loaded && !oldVessel.packed && !oldVessel.Landed && (hft != -1) && (hft < 1000) && (currentProto.protoPartSnapshots.Count == oldVessel.parts.Count))
                            {
                                DarkLog.Debug("Skipped loading protovessel " + currentProto.vesselID.ToString() + " because it is flying close to the ground and may get destroyed");
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
                                /*
                                 * Sorry guys - KSP's protovessel positioning is not as accurate as it could be.
                                 * 
                                 * The loading vessel needs to come off rails in order for the error to be corrected,
                                 * but taking it off rails will allow the vessel to collide with others while it's in the incorrect spot for that fixed update.
                                 * 
                                 * If the vessel is the selected target, close (unpacked), and has the same number of parts, we'll skip the protovessel load.
                                 */

                                if (wasTarget && !oldVessel.LandedOrSplashed && oldVessel.loaded && !oldVessel.packed && (oldVessel.parts.Count == currentProto.protoPartSnapshots.Count))
                                {

                                    DarkLog.Debug("Skipping loading protovessel " + currentProto.vesselID.ToString() + " because it is the selected target and may crash into us");
                                    return;
                                }

                                KillVessel(oldVessel);
                            }
                        }
                    }

                    vesselPartCount[currentProto.vesselID.ToString()] = currentProto.protoPartSnapshots.Count;
                    serverVesselsProtoUpdate[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    lastLoadVessel[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    currentProto.Load(HighLogic.CurrentGame.flightState);


                    if (currentProto.vesselRef != null)
                    {
                        if (usingHackyAtmoLoad)
                        {
                            //Dodge unpack/pack distances
                            currentProto.vesselRef.distanceUnpackThreshold = Vessel.loadDistance - 300;
                            currentProto.vesselRef.distanceLandedUnpackThreshold = Vessel.loadDistance - 300;
                            currentProto.vesselRef.distancePackThreshold = Vessel.loadDistance - 100;
                            currentProto.vesselRef.distanceLandedPackThreshold = Vessel.loadDistance - 100;
                            HackyFlyingVesselLoad hfvl = new HackyFlyingVesselLoad();
                            hfvl.flyingVessel = currentProto.vesselRef;
                            hfvl.loadTime = UnityEngine.Time.realtimeSinceStartup;
                            loadingFlyingVessels.Add(hfvl);
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

        private void RegisterServerAsteriodIfVesselIsAsteroid(ProtoVessel possibleAsteroid)
        {
            //Register asteroids from other players
            if (possibleAsteroid.vesselType == VesselType.SpaceObject)
            {
                if (possibleAsteroid.protoPartSnapshots != null)
                {
                    if (possibleAsteroid.protoPartSnapshots.Count == 1)
                    {
                        if (possibleAsteroid.protoPartSnapshots[0].partName == "PotatoRoid")
                        {
                            DarkLog.Debug("Registering remote server asteroid");
                            AsteroidWorker.fetch.RegisterServerAsteroid(possibleAsteroid.vesselID.ToString());
                        }
                    }
                }
            }
        }

        private void DodgeVesselActionGroups(ConfigNode vesselNode)
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

        private void RemoveManeuverNodesFromProtoVessel(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                ConfigNode flightPlanNode = vesselNode.GetNode("FLIGHTPLAN");
                if (flightPlanNode != null)
                {
                    flightPlanNode.ClearData();
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

        private bool DodgeVesselCrewValues(ConfigNode vesselNode)
        {
            bool dodged = false;
            string vesselID = Common.ConvertConfigStringToGUIDString(vesselNode.GetValue("pid"));
            foreach (ConfigNode partNode in vesselNode.GetNodes("PART"))
            {
                int crewIndex = 0;
                foreach (string configNodeValue in partNode.GetValues("crew"))
                {
                    string assignName = configNodeValue;
                    int configNodeValueInt = 0;
                    if (Int32.TryParse(configNodeValue, out configNodeValueInt))
                    {
                        ProtoCrewMember pcm = null;
                        while (pcm == null || HighLogic.CurrentGame.CrewRoster.Exists(pcm.name))
                        {
                            pcm = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Crew);
                        }
                        pcm.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                        pcm.seatIdx = crewIndex;
                        AddCrewMemberToRoster(pcm);
                        partNode.SetValue("crew", pcm.name, crewIndex);
                        DarkLog.Debug("Created kerbal " + pcm.name + " for crew index " + configNodeValue + " for vessel " + vesselID + ", Updated vessel to 0.24");
                        assignName = pcm.name;
                        dodged = true;
                    }
                    if (!assignedKerbals.ContainsKey(assignName))
                    {
                        assignedKerbals.Add(assignName, vesselID);
                    }
                    else
                    {
                        if (assignedKerbals[assignName] != vesselID)
                        {
                            ProtoCrewMember freeKerbal = null;
                            foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew)
                            {
                                if (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Available)
                                {
                                    freeKerbal = pcm;
                                    DarkLog.Debug("Assigned kerbal " + freeKerbal.name + " replacing " + configNodeValue + " for vessel " + vesselID + ", Kerbal was taken");
                                    break;
                                }
                            }
                            if (freeKerbal == null)
                            {
                                freeKerbal = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                                DarkLog.Debug("Created kerbal " + freeKerbal.name + " replacing " + configNodeValue + " for vessel " + vesselID + ", Kerbal was taken");
                            }
                            freeKerbal.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                            freeKerbal.seatIdx = crewIndex;
                            partNode.SetValue("crew", freeKerbal.name, crewIndex);
                            assignName = freeKerbal.name;
                            dodged = true;
                        }
                    }

                    if (!HighLogic.CurrentGame.CrewRoster.Exists(assignName))
                    {
                        ProtoCrewMember pcm = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Crew);
                        pcm.name = assignName;
                        pcm.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                        pcm.seatIdx = crewIndex;
                        AddCrewMemberToRoster(pcm);
                        DarkLog.Debug("Created kerbal " + pcm.name + " for vessel " + vesselID + ", Kerbal was missing");
                        dodged = true;
                    }
                    else
                    {
                        ProtoCrewMember pcm = HighLogic.CurrentGame.CrewRoster[assignName];
                        pcm.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                        pcm.type = ProtoCrewMember.KerbalType.Crew;
                        pcm.seatIdx = crewIndex;
                    }
                    crewIndex++;
                }
            }
            return dodged;
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
            if (LockSystem.fetch.LockExists("update-" + dyingVesselID) && !LockSystem.fetch.LockIsOurs("update-" + dyingVesselID))
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

        public void OnVesselCreate(Vessel createdVessel)
        {
            try
            {
                //DarkLog.Debug("Vessel creation detected: " + createdVessel.id + ", name: " + createdVessel.vesselName);
                ProtoVessel pv = createdVessel.BackupVessel();
                bool killShip = false;
                bool spawnDebris = false;
                string partOwner = null;
                string createdVesselID = pv.vesselID.ToString();
                foreach (ProtoPartSnapshot vesselPart in pv.protoPartSnapshots)
                {
                    if (vesselPart != null)
                    {
                        if (vesselParts.ContainsKey(vesselPart.flightID.ToString()))
                        {
                            partOwner = vesselParts[vesselPart.flightID.ToString()];
                            if (!killShip && (createdVesselID != partOwner))
                            {
                                if (LockSystem.fetch.LockIsOurs("control-" + partOwner) || LockSystem.fetch.LockIsOurs("update-" + partOwner) || !LockSystem.fetch.LockExists("update-" + partOwner))
                                {
                                    //Vessel is ours, update the part owner.
                                    spawnDebris = true;
                                    vesselParts[vesselPart.flightID.ToString()] = createdVesselID;
                                }
                                else
                                {
                                    DarkLog.Debug("Detected debris for a vessel we do not control, removing " + createdVesselID);
                                    killShip = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (killShip)
                {
                    createdVessel.Die();
                }
                if (spawnDebris)
                {
                    //DarkLog.Debug("Spawned debris " + createdVesselID + " from " + partOwner);
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Threw in OnVesselCreate: " + e);
            }
        }

        public void OnVesselLoaded(Vessel createdVessel)
        {
            try
            {
                //DarkLog.Debug("Vessel load detected: " + createdVessel.id + ", name: " + createdVessel.vesselName + ", parts: " + createdVessel.parts.Count);
                string loadedVesselID = createdVessel.id.ToString();
                foreach (Part vesselPart in createdVessel.parts)
                {
                    if (vesselPart != null)
                    {
                        if (!vesselParts.ContainsKey(vesselPart.flightID.ToString()))
                        {
                            //DarkLog.Debug("Loaded part " + vesselPart.name + ", id " + vesselPart.flightID + " belongs to " + loadedVesselID);
                            vesselParts.Add(vesselPart.flightID.ToString(), loadedVesselID);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Threw in OnVesselLoaded: " + e);
            }
        }

        public void OnPartDestroy(Part dyingPart)
        {
            if (vesselParts.ContainsKey(dyingPart.flightID.ToString()))
            {
                //DarkLog.Debug("Dying part " + dyingPart.name + ", id " + dyingPart.flightID + " belongs to " + vesselParts[dyingPart.flightID.ToString()]);
                vesselParts.Remove(dyingPart.flightID.ToString());
            }
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
            return latestVesselUpdate.ContainsKey(vesselID) ? ((latestVesselUpdate[vesselID] + 3f) > Planetarium.GetUniversalTime()) : false;
        }

        public void OnVesselDock(GameEvents.FromToAction<Part, Part> partAction)
        {
            DarkLog.Debug("Vessel docking detected!");
            if (!isSpectating)
            {
                if (partAction.from.vessel != null && partAction.to.vessel != null)
                {
                    bool fromVesselUpdateLockExists = LockSystem.fetch.LockExists("update-" + partAction.from.vessel.id.ToString());
                    bool toVesselUpdateLockExists = LockSystem.fetch.LockExists("update-" + partAction.to.vessel.id.ToString());
                    bool fromVesselUpdateLockIsOurs = LockSystem.fetch.LockIsOurs("update-" + partAction.from.vessel.id.ToString());
                    bool toVesselUpdateLockIsOurs = LockSystem.fetch.LockIsOurs("update-" + partAction.to.vessel.id.ToString());
                    if (fromVesselUpdateLockIsOurs || toVesselUpdateLockIsOurs || !fromVesselUpdateLockExists || !toVesselUpdateLockExists)
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
                    else
                    {
                        DarkLog.Debug("Inconsistent docking state detected, killing both vessels if possible.");
                        if (partAction.from.vessel != FlightGlobals.fetch.activeVessel)
                        {
                            delayKillVessels.Add(partAction.from.vessel);
                        }
                        if (partAction.to.vessel != FlightGlobals.fetch.activeVessel)
                        {
                            delayKillVessels.Add(partAction.to.vessel);
                        }
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
            List<string> unassignKerbals = new List<string>();
            foreach (KeyValuePair<string, string> kerbalAssignment in assignedKerbals)
            {
                if (kerbalAssignment.Value == vesselID)
                {
                    DarkLog.Debug("Kerbal " + kerbalAssignment.Key + " unassigned from " + vesselID);
                    unassignKerbals.Add(kerbalAssignment.Key);
                }
            }
            foreach (string unassignKerbal in unassignKerbals)
            {
                assignedKerbals.Remove(unassignKerbal);
                if (!isSpectating)
                {
                    foreach (ProtoCrewMember pcm in HighLogic.CurrentGame.CrewRoster.Crew)
                    {
                        if (pcm.name == unassignKerbal)
                        {
                            NetworkWorker.fetch.SendKerbalProtoMessage(pcm);
                        }
                    }
                }
            }
        }

        private void KillVessel(Vessel killVessel)
        {
            if (killVessel != null)
            {
                //TODO: Deselect the vessel in the tracking station if we are about to kill it.
                DarkLog.Debug("Killing vessel: " + killVessel.id.ToString());
                killVessel.DespawnCrew();
                //Add it to the delay kill list to make sure it dies.
                //With KSP, If it doesn't work first time, keeping doing it until it does.
                if (!delayKillVessels.Contains(killVessel))
                {
                    delayKillVessels.Add(killVessel);
                }
                lastKillVesselDestroy[killVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                if (killVessel.loaded)
                {
                    try
                    {
                        killVessel.Unload();
                    }
                    catch (Exception unloadException)
                    {
                        DarkLog.Debug("Error unloading vessel: " + unloadException);
                    }
                }
                foreach (ProtoPartSnapshot pps in killVessel.protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoCrewMember pcm in pps.protoModuleCrew)
                    {
                        DarkLog.Debug("Unassigning " + pcm.name + " from " + killVessel.id.ToString());
                        pcm.rosterStatus = ProtoCrewMember.RosterStatus.Available;
                        pcm.seatIdx = -1;
                    }
                    pps.protoModuleCrew.Clear();
                }
                try
                {
                    killVessel.Die();
                }
                catch (Exception killException)
                {
                    DarkLog.Debug("Error destroying vessel: " + killException);
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



        //Called from networkWorker
        public void QueueKerbal(double planetTime, string kerbalName, ConfigNode kerbalNode)
        {
            KerbalEntry newEntry = new KerbalEntry();
            newEntry.planetTime = planetTime;
            newEntry.kerbalNode = kerbalNode;
            if (!kerbalProtoQueue.ContainsKey(kerbalName))
            {
                kerbalProtoQueue.Add(kerbalName, new Queue<KerbalEntry>());
            }
            kerbalProtoQueue[kerbalName].Enqueue(newEntry);
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
                    Client.updateEvent.Remove(singleton.Update);
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
                Client.updateEvent.Add(singleton.Update);
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

    class KerbalEntry
    {
        public double planetTime;
        public ConfigNode kerbalNode;
    }

    class HackyFlyingVesselLoad
    {
        public double loadTime;
        public double unpackTime;
        public Vessel flyingVessel;
    }
}

