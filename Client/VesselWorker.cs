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
        //Update frequency
        private const float VESSEL_PROTOVESSEL_UPDATE_INTERVAL = 30f;
        public float safetyBubbleDistance = 100f;
        //Debug delay
        public float delayTime = 0f;
        //Spectate stuff
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
        private Dictionary<Guid, Queue<VesselRemoveEntry>> vesselRemoveQueue = new Dictionary<Guid, Queue<VesselRemoveEntry>>();
        private Dictionary<Guid, Queue<VesselProtoUpdate>> vesselProtoQueue = new Dictionary<Guid, Queue<VesselProtoUpdate>>();
        private Dictionary<Guid, Queue<VesselUpdate>> vesselUpdateQueue = new Dictionary<Guid, Queue<VesselUpdate>>();
        private Dictionary<string, Queue<KerbalEntry>> kerbalProtoQueue = new Dictionary<string, Queue<KerbalEntry>>();
        private Dictionary<Guid, VesselUpdate> previousUpdates = new Dictionary<Guid, VesselUpdate>();
        //Incoming revert support
        private Dictionary<Guid, List<VesselRemoveEntry>> vesselRemoveHistory = new Dictionary<Guid, List<VesselRemoveEntry>>();
        private Dictionary<Guid, double> vesselRemoveHistoryTime = new Dictionary<Guid, double>();
        private Dictionary<Guid, List<VesselProtoUpdate>> vesselProtoHistory = new Dictionary<Guid, List<VesselProtoUpdate>>();
        private Dictionary<Guid, double> vesselProtoHistoryTime = new Dictionary<Guid, double>();
        private Dictionary<Guid, List<VesselUpdate>> vesselUpdateHistory = new Dictionary<Guid, List<VesselUpdate>>();
        private Dictionary<Guid, double> vesselUpdateHistoryTime = new Dictionary<Guid, double>();
        private Dictionary<string, List<KerbalEntry>> kerbalProtoHistory = new Dictionary<string, List<KerbalEntry>>();
        private Dictionary<string, double> kerbalProtoHistoryTime = new Dictionary<string, double>();
        private Dictionary<Guid, VesselCtrlUpdate> vesselControlUpdates = new Dictionary<Guid, VesselCtrlUpdate>();
        private double lastUniverseTime = double.NegativeInfinity;
        //Vessel tracking
        private Queue<ActiveVesselEntry> newActiveVessels = new Queue<ActiveVesselEntry>();
        private HashSet<Guid> serverVessels = new HashSet<Guid>();
        private Dictionary<Guid, bool> vesselPartsOk = new Dictionary<Guid, bool>();
        //Vessel state tracking
        private Guid lastVesselID;
        private Dictionary<Guid, int> vesselPartCount = new Dictionary<Guid, int>();
        private Dictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();
        private Dictionary<Guid, VesselType> vesselTypes = new Dictionary<Guid, VesselType>();
        private Dictionary<Guid, Vessel.Situations> vesselSituations = new Dictionary<Guid, Vessel.Situations>();
        //Known kerbals
        private Dictionary<string, string> serverKerbals = new Dictionary<string, string>();
        //Known vessels and last send/receive time
        private Dictionary<Guid, float> serverVesselsProtoUpdate = new Dictionary<Guid, float>();
        private Dictionary<Guid, float> serverVesselsPositionUpdate = new Dictionary<Guid, float>();
        //Track when the vessel was last controlled.
        private Dictionary<Guid, double> latestVesselUpdate = new Dictionary<Guid, double>();
        private Dictionary<Guid, double> latestUpdateSent = new Dictionary<Guid, double>();
        //Track spectating state
        private bool wasSpectating;
        private int spectateType;
        //KillVessel tracking
        private Dictionary<Guid, double> lastKillVesselDestroy = new Dictionary<Guid, double>();
        private Dictionary<Guid, double> lastLoadVessel = new Dictionary<Guid, double>();
        private List<Vessel> delayKillVessels = new List<Vessel>();
        private List<Guid> ignoreVessels = new List<Guid>();
        //Docking related
        private Vessel newActiveVessel;
        private int activeVesselLoadUpdates;
        private Guid fromDockedVesselID;
        private Guid toDockedVesselID;
        private bool sentDockingDestroyUpdate;
        //Services
        private DMPGame dmpGame;
        private Settings dmpSettings;
        private ModWorker modWorker;
        private LockSystem lockSystem;
        private NetworkWorker networkWorker;
        private HackyInAtmoLoader hackyInAtmoLoader;
        private TimeSyncer timeSyncer;
        private DynamicTickWorker dynamicTickWorker;
        private ConfigNodeSerializer configNodeSerializer;
        private ChatWorker chatWorker;
        private KerbalReassigner kerbalReassigner;
        private AsteroidWorker asteroidWorker;
        private PartKiller partKiller;
        private PlayerStatusWorker playerStatusWorker;
        private PosistionStatistics posistionStatistics;
        private VesselInterFrameUpdater vesselPackedUpdater;

        public VesselWorker(DMPGame dmpGame, Settings dmpSettings, ModWorker modWorker, LockSystem lockSystem, NetworkWorker networkWorker, ConfigNodeSerializer configNodeSerializer, DynamicTickWorker dynamicTickWorker, KerbalReassigner kerbalReassigner, PartKiller partKiller, PosistionStatistics posistionStatistics, VesselInterFrameUpdater vesselPackedUpdater)
        {
            this.dmpGame = dmpGame;
            this.dmpSettings = dmpSettings;
            this.modWorker = modWorker;
            this.lockSystem = lockSystem;
            this.networkWorker = networkWorker;
            this.configNodeSerializer = configNodeSerializer;
            this.dynamicTickWorker = dynamicTickWorker;
            this.kerbalReassigner = kerbalReassigner;
            this.partKiller = partKiller;
            this.posistionStatistics = posistionStatistics;
            this.vesselPackedUpdater = vesselPackedUpdater;
            dmpGame.fixedUpdateEvent.Add(FixedUpdate);
        }

        public void SetDependencies(HackyInAtmoLoader hackyInAtmoLoader, TimeSyncer timeSyncer, AsteroidWorker asteroidWorker, ChatWorker chatWorker, PlayerStatusWorker playerStatusWorker)
        {
            this.hackyInAtmoLoader = hackyInAtmoLoader;
            this.timeSyncer = timeSyncer;
            this.asteroidWorker = asteroidWorker;
            this.chatWorker = chatWorker;
            this.playerStatusWorker = playerStatusWorker;
        }

        //Called from main
        private void FixedUpdate()
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
                        //Wait 10 updates maybe?
                        if (activeVesselLoadUpdates < 10)
                        {
                            activeVesselLoadUpdates++;
                            return;
                        }
                        activeVesselLoadUpdates = 0;
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
                foreach (Vessel dyingVessel in delayKillVessels.ToArray())
                {
                    if (FlightGlobals.fetch.vessels.Contains(dyingVessel) && dyingVessel.state != Vessel.State.DEAD)
                    {
                        DarkLog.Debug("Delay killing " + dyingVessel.id.ToString());
                        KillVessel(dyingVessel);
                    }
                    else
                    {
                        delayKillVessels.Remove(dyingVessel);
                    }
                }

                if (fromDockedVesselID != Guid.Empty || toDockedVesselID != Guid.Empty)
                {
                    HandleDocking();
                }

                //Process new messages
                lock (updateQueueLock)
                {
                    ProcessNewVesselMessages();
                }

                //Apply updates to packed vessel
                vesselPackedUpdater.Update();

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
            GameEvents.onVesselRename.Add(this.OnVesselRenamed);
            GameEvents.onPartCouple.Add(this.OnVesselDock);
            GameEvents.onCrewBoardVessel.Add(this.OnCrewBoard);
            GameEvents.onKerbalRemoved.Add(OnKerbalRemoved);
        }

        private void UnregisterGameHooks()
        {
            registered = false;
            GameEvents.onVesselRecovered.Remove(this.OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(this.OnVesselTerminated);
            GameEvents.onVesselDestroy.Remove(this.OnVesselDestroyed);
            GameEvents.onVesselRename.Remove(this.OnVesselRenamed);
            GameEvents.onPartCouple.Remove(this.OnVesselDock);
            GameEvents.onCrewBoardVessel.Remove(this.OnCrewBoard);
            GameEvents.onKerbalRemoved.Remove(OnKerbalRemoved);
        }

        private void HandleDocking()
        {
            if (sentDockingDestroyUpdate)
            {
                //One of them will be null, the other one will be the docked craft.
                Guid dockedID = fromDockedVesselID != Guid.Empty ? fromDockedVesselID : toDockedVesselID;
                //Find the docked craft
                Vessel dockedVessel = FlightGlobals.fetch.vessels.FindLast(v => v.id == dockedID);
                if (dockedVessel != null ? !dockedVessel.packed : false)
                {
                    if (modWorker.modControl != ModControlMode.DISABLED)
                    {
                        CheckVesselParts(dockedVessel);
                    }
                    ProtoVessel sendProto = new ProtoVessel(dockedVessel);
                    if (sendProto != null)
                    {
                        if (modWorker.modControl == ModControlMode.DISABLED || vesselPartsOk[dockedVessel.id])
                        {
                            DarkLog.Debug("Sending docked protovessel " + dockedID);
                            //Mark the vessel as sent
                            serverVesselsProtoUpdate[dockedID] = Client.realtimeSinceStartup;
                            serverVesselsPositionUpdate[dockedID] = Client.realtimeSinceStartup;
                            RegisterServerVessel(dockedID);
                            vesselPartCount[dockedID] = dockedVessel.parts.Count;
                            vesselNames[dockedID] = dockedVessel.vesselName;
                            vesselTypes[dockedID] = dockedVessel.vesselType;
                            vesselSituations[dockedID] = dockedVessel.situation;
                            //Update status if it's us.
                            if (dockedVessel == FlightGlobals.fetch.activeVessel)
                            {
                                //Release old control locks
                                if (lastVesselID != FlightGlobals.fetch.activeVessel.id)
                                {
                                    lockSystem.ReleasePlayerLocksWithPrefix(dmpSettings.playerName, "control-");
                                    lastVesselID = FlightGlobals.fetch.activeVessel.id;
                                }
                                //Force the control lock off any other player
                                lockSystem.AcquireLock("control-" + dockedID, true);
                                playerStatusWorker.myPlayerStatus.vesselText = FlightGlobals.fetch.activeVessel.vesselName;
                            }
                            fromDockedVesselID = Guid.Empty;
                            toDockedVesselID = Guid.Empty;
                            sentDockingDestroyUpdate = false;

                            bool isFlyingUpdate = (sendProto.situation == Vessel.Situations.FLYING);
                            networkWorker.SendVesselProtoMessage(sendProto, true, isFlyingUpdate);
                            if (dockingMessage != null)
                            {
                                dockingMessage.duration = 0f;
                            }
                            dockingMessage = ScreenMessages.PostScreenMessage("Docked!", 3f, ScreenMessageStyle.UPPER_CENTER);
                            DarkLog.Debug("Docking event over!");
                        }
                        else
                        {
                            fromDockedVesselID = Guid.Empty;
                            toDockedVesselID = Guid.Empty;
                            sentDockingDestroyUpdate = false;
                            if (dockingMessage != null)
                            {
                                dockingMessage.duration = 0f;
                            }
                            dockingMessage = ScreenMessages.PostScreenMessage("Error sending vessel - vessel contains invalid parts!", 3f, ScreenMessageStyle.UPPER_CENTER);
                            DarkLog.Debug("Docking event over - vessel contained invalid parts!");
                        }
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
            if ((Client.realtimeSinceStartup - lastDockingMessageUpdate) > 1f)
            {
                lastDockingMessageUpdate = Client.realtimeSinceStartup;
                if (dockingMessage != null)
                {
                    dockingMessage.duration = 0f;
                }
                dockingMessage = ScreenMessages.PostScreenMessage("Docking in progress...", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void ReleaseOldUpdateLocks()
        {
            List<Guid> removeList = new List<Guid>();
            foreach (KeyValuePair<Guid, double> entry in latestUpdateSent)
            {
                if ((Client.realtimeSinceStartup - entry.Value) > 5f)
                {
                    removeList.Add(entry.Key);
                }
            }
            foreach (Guid removeEntry in removeList)
            {
                latestUpdateSent.Remove(removeEntry);
                if (lockSystem.LockIsOurs("update-" + removeEntry))
                {
                    Vessel sendVessel = FlightGlobals.fetch.vessels.FindLast(v => v.id == removeEntry);
                    if (sendVessel != null)
                    {
                        if (modWorker.modControl != ModControlMode.DISABLED)
                        {
                            CheckVesselParts(sendVessel);
                        }
                        if (modWorker.modControl == ModControlMode.DISABLED || vesselPartsOk[removeEntry])
                        {
                            ProtoVessel sendProto = sendVessel.BackupVessel();
                            if (sendProto != null)
                            {
                                bool isFlyingUpdate = (sendProto.situation == Vessel.Situations.FLYING);
                                networkWorker.SendVesselProtoMessage(sendProto, false, isFlyingUpdate);
                            }
                        }
                    }
                    lockSystem.ReleaseLock("update-" + removeEntry);
                }
            }
        }

        private void ProcessNewVesselMessages()
        {
            double interpolatorDelay = 0f;
            if (dmpSettings.interpolatorType == InterpolatorType.INTERPOLATE1S)
            {
                interpolatorDelay = 1f;
            }
            if (dmpSettings.interpolatorType == InterpolatorType.INTERPOLATE3S)
            {
                interpolatorDelay = 3f;
            }

            double thisPlanetTime = Planetarium.GetUniversalTime();
            double thisDelayTime = delayTime - interpolatorDelay;
            if (thisDelayTime < 0f)
            {
                thisDelayTime = 0f;
            }

            Dictionary<Guid, double> removeList = new Dictionary<Guid, double>();
            lock (vesselRemoveQueue)
            {
                foreach (KeyValuePair<Guid, Queue<VesselRemoveEntry>> vesselRemoveSubspace in vesselRemoveQueue)
                {
                    while (vesselRemoveSubspace.Value.Count > 0 && ((vesselRemoveSubspace.Value.Peek().planetTime + thisDelayTime + interpolatorDelay) < thisPlanetTime))
                    {
                        VesselRemoveEntry removeVessel = vesselRemoveSubspace.Value.Dequeue();
                        RemoveVessel(removeVessel.vesselID, removeVessel.isDockingUpdate, removeVessel.dockingPlayer);
                        removeList[removeVessel.vesselID] = removeVessel.planetTime;
                    }
                }
            }

            foreach (KeyValuePair<string, Queue<KerbalEntry>> kerbalProtoSubspace in kerbalProtoQueue)
            {
                while (kerbalProtoSubspace.Value.Count > 0 && ((kerbalProtoSubspace.Value.Peek().planetTime + thisDelayTime + interpolatorDelay) < thisPlanetTime))
                {
                    KerbalEntry kerbalEntry = kerbalProtoSubspace.Value.Dequeue();
                    LoadKerbal(kerbalEntry.kerbalNode);
                }
            }

            foreach (KeyValuePair<Guid, Queue<VesselProtoUpdate>> vesselQueue in vesselProtoQueue)
            {
                VesselProtoUpdate vpu = null;
                //Get the latest proto update
                bool applyDelay = vesselUpdateQueue.ContainsKey(vesselQueue.Key) && vesselUpdateQueue[vesselQueue.Key].Count > 0 && vesselUpdateQueue[vesselQueue.Key].Peek().isSurfaceUpdate;
                double thisInterpolatorDelay = 0f;
                if (applyDelay)
                {
                    thisInterpolatorDelay = interpolatorDelay;
                }
                while (vesselQueue.Value.Count > 0)
                {
                    if ((vesselQueue.Value.Peek().planetTime + thisDelayTime + thisInterpolatorDelay) < thisPlanetTime)
                    {
                        VesselProtoUpdate newVpu = vesselQueue.Value.Dequeue();
                        if (newVpu != null)
                        {
                            //Skip any protovessels that have been removed in the future
                            if (!removeList.ContainsKey(vesselQueue.Key) || removeList[vesselQueue.Key] < vpu.planetTime)
                            {
                                vpu = newVpu;
                            }
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                //Apply it if there is any
                if (vpu != null && vpu.vesselNode != null)
                {
                    LoadVessel(vpu.vesselNode, vpu.vesselID, false);
                }
            }

            foreach (KeyValuePair<Guid, Queue<VesselUpdate>> vesselQueue in vesselUpdateQueue)
            {
                VesselUpdate vu = null;
                //Get the latest position update
                while (vesselQueue.Value.Count > 0)
                {
                    double thisInterpolatorDelay = 0f;
                    if (vesselQueue.Value.Peek().isSurfaceUpdate)
                    {
                        thisInterpolatorDelay = interpolatorDelay;
                    }
                    if ((vesselQueue.Value.Peek().planetTime + thisDelayTime + thisInterpolatorDelay) < thisPlanetTime)
                    {
                        vu = vesselQueue.Value.Dequeue();
                    }
                    else
                    {
                        break;
                    }
                }
                //Apply it if there is any
                if (vu != null)
                {
                    VesselUpdate previousUpdate = null;
                    if (previousUpdates.ContainsKey(vu.vesselID))
                    {
                        previousUpdate = previousUpdates[vu.vesselID];
                    }
                    VesselUpdate nextUpdate = null;
                    if (vesselQueue.Value.Count > 0)
                    {
                        nextUpdate = vesselQueue.Value.Peek();
                    }
                    vu.Apply(posistionStatistics, vesselControlUpdates, previousUpdate, nextUpdate, dmpSettings);
                    vesselPackedUpdater.SetVesselUpdate(vu.vesselID, vu, previousUpdate, nextUpdate);
                    previousUpdates[vu.vesselID] = vu;
                }
            }
        }

        public void DetectReverting()
        {
            double newUniverseTime = Planetarium.GetUniversalTime();
            //10 second fudge to ignore TimeSyncer skips
            if (newUniverseTime < (lastUniverseTime - 10f))
            {
                int updatesReverted = 0;
                DarkLog.Debug("Revert detected!");
                timeSyncer.UnlockSubspace();
                if (!dmpSettings.revertEnabled)
                {
                    DarkLog.Debug("Unsafe revert detected!");
                    ScreenMessages.PostScreenMessage("Unsafe revert detected!", 5f, ScreenMessageStyle.UPPER_CENTER);
                }
                else
                {
                    kerbalProtoQueue.Clear();
                    vesselProtoQueue.Clear();
                    vesselUpdateQueue.Clear();
                    vesselRemoveQueue.Clear();
                    //Kerbal queue
                    KerbalEntry lastKerbalEntry = null;
                    foreach (KeyValuePair<string, List<KerbalEntry>> kvp in kerbalProtoHistory)
                    {
                        bool adding = false;
                        kerbalProtoQueue.Add(kvp.Key, new Queue<KerbalEntry>());
                        foreach (KerbalEntry ke in kvp.Value)
                        {
                            if (ke.planetTime > newUniverseTime)
                            {
                                if (!adding)
                                {
                                    //One shot - add the previous update before the time to apply instantly
                                    if (lastKerbalEntry != null)
                                    {
                                        kerbalProtoQueue[kvp.Key].Enqueue(lastKerbalEntry);
                                        updatesReverted++;
                                    }
                                }
                                adding = true;
                            }
                            if (adding)
                            {
                                kerbalProtoQueue[kvp.Key].Enqueue(ke);
                                updatesReverted++;
                            }
                            lastKerbalEntry = ke;
                        }
                    }
                    //Vessel proto queue
                    VesselProtoUpdate lastVesselProtoEntry = null;
                    foreach (KeyValuePair<Guid, List<VesselProtoUpdate>> kvp in vesselProtoHistory)
                    {
                        bool adding = false;
                        vesselProtoQueue.Add(kvp.Key, new Queue<VesselProtoUpdate>());
                        foreach (VesselProtoUpdate vpu in kvp.Value)
                        {
                            if (vpu.planetTime > newUniverseTime)
                            {
                                if (!adding)
                                {
                                    //One shot - add the previous update before the time to apply instantly
                                    if (lastVesselProtoEntry != null)
                                    {
                                        vesselProtoQueue[kvp.Key].Enqueue(lastVesselProtoEntry);
                                        updatesReverted++;
                                    }
                                }
                                adding = true;
                            }
                            if (adding)
                            {
                                vesselProtoQueue[kvp.Key].Enqueue(vpu);
                                updatesReverted++;
                            }
                            lastVesselProtoEntry = vpu;
                        }
                    }
                    //Vessel update queue
                    VesselUpdate lastVesselUpdateEntry = null;
                    foreach (KeyValuePair<Guid, List<VesselUpdate>> kvp in vesselUpdateHistory)
                    {
                        bool adding = false;
                        vesselUpdateQueue.Add(kvp.Key, new Queue<VesselUpdate>());
                        foreach (VesselUpdate vu in kvp.Value)
                        {
                            if (vu.planetTime > newUniverseTime)
                            {
                                if (!adding)
                                {
                                    //One shot - add the previous update before the time to apply instantly
                                    if (lastVesselUpdateEntry != null)
                                    {
                                        vesselUpdateQueue[kvp.Key].Enqueue(lastVesselUpdateEntry);
                                        updatesReverted++;
                                    }
                                }
                                adding = true;
                            }
                            if (adding)
                            {
                                vesselUpdateQueue[kvp.Key].Enqueue(vu);
                                updatesReverted++;
                            }
                            lastVesselUpdateEntry = vu;
                        }
                    }
                    //Remove entries
                    VesselRemoveEntry lastRemoveEntry = null;
                    foreach (KeyValuePair<Guid, List<VesselRemoveEntry>> kvp in vesselRemoveHistory)
                    {
                        bool adding = false;
                        vesselRemoveQueue.Add(kvp.Key, new Queue<VesselRemoveEntry>());
                        foreach (VesselRemoveEntry vre in kvp.Value)
                        {
                            if (vre.planetTime > newUniverseTime)
                            {
                                if (!adding)
                                {
                                    //One shot - add the previous update before the time to apply instantly
                                    if (lastRemoveEntry != null)
                                    {
                                        vesselRemoveQueue[kvp.Key].Enqueue(lastRemoveEntry);
                                        updatesReverted++;
                                    }
                                }
                                adding = true;
                            }
                            if (adding)
                            {
                                vesselRemoveQueue[kvp.Key].Enqueue(vre);
                                updatesReverted++;
                            }
                            lastRemoveEntry = vre;
                        }
                    }
                }
                DarkLog.Debug("Reverted " + updatesReverted + " updates");
                ScreenMessages.PostScreenMessage("Reverted " + updatesReverted + " updates", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            lastUniverseTime = newUniverseTime;
        }

        private void UpdateOnScreenSpectateMessage()
        {
            if ((Client.realtimeSinceStartup - lastSpectateMessageUpdate) > UPDATE_SCREEN_MESSAGE_INTERVAL)
            {
                lastSpectateMessageUpdate = Client.realtimeSinceStartup;
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
                    InputLockManager.SetControlLock(DMPGuiUtil.BLOCK_ALL_CONTROLS, DARK_SPECTATE_LOCK);
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
                    if (!lockSystem.LockExists("control-" + FlightGlobals.fetch.activeVessel.id.ToString()))
                    {
                        lockSystem.ThrottledAcquireLock("control-" + FlightGlobals.fetch.activeVessel.id.ToString());
                    }
                }

                if (!isSpectating)
                {
                    //When we change vessel, send the previous flown vessel as soon as possible.
                    if (lastVesselID != FlightGlobals.fetch.activeVessel.id)
                    {
                        if (lastVesselID != Guid.Empty)
                        {
                            DarkLog.Debug("Resetting last send time for " + lastVesselID);
                            serverVesselsProtoUpdate[lastVesselID] = 0f;
                            lockSystem.ReleasePlayerLocksWithPrefix(dmpSettings.playerName, "control-");
                        }
                        //Reset the send time of the vessel we just switched to
                        serverVesselsProtoUpdate[FlightGlobals.fetch.activeVessel.id] = 0f;
                        //Nobody else is flying the vessel - let's take it
                        playerStatusWorker.myPlayerStatus.vesselText = FlightGlobals.fetch.activeVessel.vesselName;
                        lastVesselID = FlightGlobals.fetch.activeVessel.id;
                    }
                    if (!lockSystem.LockExists("control-" + FlightGlobals.fetch.activeVessel.id.ToString()))
                    {
                        lockSystem.ThrottledAcquireLock("control-" + FlightGlobals.fetch.activeVessel.id.ToString());
                    }
                }
                else
                {
                    if (lastVesselID != Guid.Empty)
                    {
                        lockSystem.ReleasePlayerLocksWithPrefix(dmpSettings.playerName, "control-");
                        lastVesselID = Guid.Empty;
                        playerStatusWorker.myPlayerStatus.vesselText = "";
                    }
                }
            }
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                //Release the vessel if we aren't in flight anymore.
                if (lastVesselID != Guid.Empty)
                {
                    DarkLog.Debug("Releasing " + lastVesselID + " - No longer in flight!");
                    lockSystem.ReleasePlayerLocksWithPrefix(dmpSettings.playerName, "control-");
                    lastVesselID = Guid.Empty;
                    playerStatusWorker.myPlayerStatus.vesselText = "";
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
                        bool partCountChanged = vesselPartCount.ContainsKey(checkVessel.id) ? checkVessel.parts.Count != vesselPartCount[checkVessel.id] : true;

                        if (partCountChanged)
                        {
                            serverVesselsProtoUpdate[checkVessel.id] = 0f;
                            vesselPartCount[checkVessel.id] = checkVessel.parts.Count;
                            if (vesselPartsOk.ContainsKey(checkVessel.id))
                            {
                                DarkLog.Debug("Forcing parts recheck on " + checkVessel.id.ToString());
                                vesselPartsOk.Remove(checkVessel.id);
                            }
                        }
                        //Add entries to dictionaries if needed
                        if (!vesselNames.ContainsKey(checkVessel.id))
                        {
                            vesselNames.Add(checkVessel.id, checkVessel.vesselName);
                        }
                        if (!vesselTypes.ContainsKey(checkVessel.id))
                        {
                            vesselTypes.Add(checkVessel.id, checkVessel.vesselType);
                        }
                        if (!vesselSituations.ContainsKey(checkVessel.id))
                        {
                            vesselSituations.Add(checkVessel.id, checkVessel.situation);
                        }
                        //Check active vessel for situation/renames. Throttle send to 10 seconds.
                        bool vesselNotRecentlyUpdated = serverVesselsPositionUpdate.ContainsKey(checkVessel.id) ? ((Client.realtimeSinceStartup - serverVesselsProtoUpdate[checkVessel.id]) > 10f) : true;
                        bool recentlyLanded = vesselSituations[checkVessel.id] != Vessel.Situations.LANDED && checkVessel.situation == Vessel.Situations.LANDED;
                        bool recentlySplashed = vesselSituations[checkVessel.id] != Vessel.Situations.SPLASHED && checkVessel.situation == Vessel.Situations.SPLASHED;
                        if (vesselNotRecentlyUpdated || recentlyLanded || recentlySplashed)
                        {
                            bool nameChanged = (vesselNames[checkVessel.id] != checkVessel.vesselName);
                            bool typeChanged = (vesselTypes[checkVessel.id] != checkVessel.vesselType);
                            bool situationChanged = (vesselSituations[checkVessel.id] != checkVessel.situation);
                            if (nameChanged || typeChanged || situationChanged)
                            {
                                vesselNames[checkVessel.id] = checkVessel.vesselName;
                                vesselTypes[checkVessel.id] = checkVessel.vesselType;
                                vesselSituations[checkVessel.id] = checkVessel.situation;
                                serverVesselsProtoUpdate[checkVessel.id] = 0f;
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

            if (modWorker.modControl != ModControlMode.DISABLED)
            {
                if (!vesselPartsOk.ContainsKey(FlightGlobals.fetch.activeVessel.id))
                {
                    //Check the vessel parts if we haven't already, shows the warning message in the safety bubble.
                    CheckVesselParts(FlightGlobals.fetch.activeVessel);
                }


                if (!vesselPartsOk[FlightGlobals.fetch.activeVessel.id])
                {
                    if ((Client.realtimeSinceStartup - lastBannedPartsMessageUpdate) > UPDATE_SCREEN_MESSAGE_INTERVAL)
                    {
                        lastBannedPartsMessageUpdate = Client.realtimeSinceStartup;
                        if (bannedPartsMessage != null)
                        {
                            bannedPartsMessage.duration = 0;
                        }
                        if (modWorker.modControl == ModControlMode.ENABLED_STOP_INVALID_PART_SYNC)
                        {
                            bannedPartsMessage = ScreenMessages.PostScreenMessage("Active vessel contains the following banned parts, it will not be saved to the server:\n" + bannedPartsString, 2f, ScreenMessageStyle.UPPER_CENTER);
                        }
                        if (modWorker.modControl == ModControlMode.ENABLED_STOP_INVALID_PART_LAUNCH)
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

            if (fromDockedVesselID != Guid.Empty || toDockedVesselID != Guid.Empty)
            {
                //Don't send updates while docking
                return;
            }

            if (isInSafetyBubble(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), FlightGlobals.fetch.activeVessel.mainBody))
            {
                //Don't send updates while in the safety bubble
                return;
            }

            if (lockSystem.LockIsOurs("control-" + FlightGlobals.fetch.activeVessel.id.ToString()))
            {
                SendVesselUpdateIfNeeded(FlightGlobals.fetch.activeVessel);
            }

            SortedList<double, Vessel> secondryVessels = new SortedList<double, Vessel>();

            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                if (ignoreVessels.Contains(checkVessel.id))
                {
                    continue;
                }
                //Only update the vessel if it's loaded and unpacked (not on rails). Skip our vessel.
                if (checkVessel.loaded && !checkVessel.packed && (checkVessel.id.ToString() != FlightGlobals.fetch.activeVessel.id.ToString()) && (checkVessel.state != Vessel.State.DEAD))
                {
                    //Don't update vessels in the safety bubble
                    if (!isInSafetyBubble(checkVessel.GetWorldPos3D(), checkVessel.mainBody))
                    {
                        //Only attempt to update vessels that we have locks for, and ask for locks. Dont bother with controlled vessels
                        bool updateLockIsFree = !lockSystem.LockExists("update-" + checkVessel.id.ToString());
                        bool updateLockIsOurs = lockSystem.LockIsOurs("update-" + checkVessel.id.ToString());
                        bool controlledByPlayer = lockSystem.LockExists("control-" + checkVessel.id.ToString());
                        if ((updateLockIsFree || updateLockIsOurs) && !controlledByPlayer)
                        {
                            //Dont update vessels manipulated in the future
                            if (!VesselUpdatedInFuture(checkVessel.id))
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
                if (currentSend > dynamicTickWorker.maxSecondryVesselsPerTick)
                {
                    break;
                }
                SendVesselUpdateIfNeeded(secondryVessel.Value);
            }
        }

        private void CheckVesselParts(Vessel checkVessel)
        {
            List<string> allowedParts = modWorker.GetAllowedPartsList();
            List<string> bannedParts = new List<string>();
            ProtoVessel checkProto = checkVessel.BackupVessel();
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
            if (checkVessel.isActiveVessel)
            {
                bannedPartsString = "";
                foreach (string bannedPart in bannedParts)
                {
                    bannedPartsString += bannedPart + "\n";
                }
            }
            DarkLog.Debug("Checked vessel " + checkVessel.id.ToString() + " for banned parts, is ok: " + (bannedParts.Count == 0));
            vesselPartsOk[checkVessel.id] = (bannedParts.Count == 0);
        }

        public void SendVesselUpdateIfNeeded(Vessel checkVessel)
        {
            //Check vessel parts
            if (modWorker.modControl != ModControlMode.DISABLED)
            {
                if (!vesselPartsOk.ContainsKey(checkVessel.id))
                {
                    CheckVesselParts(checkVessel);
                }
                if (!vesselPartsOk[checkVessel.id])
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
            if (!lockSystem.LockExists("update-" + checkVessel.id.ToString()))
            {
                lockSystem.ThrottledAcquireLock("update-" + checkVessel.id.ToString());
            }

            // Check if it isn't null before fetching it
            if (FlightGlobals.fetch.activeVessel != null)
            {
                //Take the update lock off another player if we have the control lock and it's our vessel
                if (checkVessel.id == FlightGlobals.fetch.activeVessel.id)
                {
                    if (lockSystem.LockExists("update-" + checkVessel.id.ToString()) && !lockSystem.LockIsOurs("update-" + checkVessel.id.ToString()) && lockSystem.LockIsOurs("control-" + checkVessel.id.ToString()))
                    {
                        lockSystem.ThrottledAcquireLock("update-" + checkVessel.id.ToString());
                        //Wait until we have the update lock
                        return;
                    }
                }
            }

            //Send updates for unpacked vessels that aren't being flown by other players
            bool notRecentlySentProtoUpdate = serverVesselsProtoUpdate.ContainsKey(checkVessel.id) ? ((Client.realtimeSinceStartup - serverVesselsProtoUpdate[checkVessel.id]) > VESSEL_PROTOVESSEL_UPDATE_INTERVAL) : true;
            bool notRecentlySentPositionUpdate = serverVesselsPositionUpdate.ContainsKey(checkVessel.id) ? ((Client.realtimeSinceStartup - serverVesselsPositionUpdate[checkVessel.id]) > (1f / (float)dynamicTickWorker.sendTickRate)) : true;

            //Check that is hasn't been recently sent
            if (notRecentlySentProtoUpdate)
            {
                ProtoVessel checkProto = checkVessel.BackupVessel();
                //TODO: Fix sending of flying vessels.
                if (checkProto != null)
                {
                    if (checkProto.vesselID != Guid.Empty)
                    {
                        //Also check for kerbal state changes
                        foreach (ProtoPartSnapshot part in checkProto.protoPartSnapshots)
                        {
                            foreach (ProtoCrewMember pcm in part.protoModuleCrew.ToArray())
                            {
                                SendKerbalIfDifferent(pcm);
                            }
                        }
                        RegisterServerVessel(checkProto.vesselID);
                        //Mark the update as sent
                        serverVesselsProtoUpdate[checkVessel.id] = Client.realtimeSinceStartup;
                        //Also delay the position send
                        serverVesselsPositionUpdate[checkVessel.id] = Client.realtimeSinceStartup;
                        latestUpdateSent[checkVessel.id] = Client.realtimeSinceStartup;
                        bool isFlyingUpdate = (checkProto.situation == Vessel.Situations.FLYING);
                        networkWorker.SendVesselProtoMessage(checkProto, false, isFlyingUpdate);
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
                serverVesselsPositionUpdate[checkVessel.id] = Client.realtimeSinceStartup;
                latestUpdateSent[checkVessel.id] = Client.realtimeSinceStartup;
                VesselUpdate update = VesselUpdate.CopyFromVessel(this, checkVessel);
                if (update != null)
                {
                    networkWorker.SendVesselUpdate(update);
                }
            }
        }

        public void SendKerbalIfDifferent(ProtoCrewMember pcm)
        {
            ConfigNode kerbalNode = new ConfigNode();
            pcm.Save(kerbalNode);
            if (pcm.type == ProtoCrewMember.KerbalType.Tourist || pcm.type == ProtoCrewMember.KerbalType.Unowned)
            {
                ConfigNode dmpNode = new ConfigNode();
                dmpNode.AddValue("contractOwner", dmpSettings.playerPublicKey);
                kerbalNode.AddNode("DarkMultiPlayer", dmpNode);
            }
            byte[] kerbalBytes = configNodeSerializer.Serialize(kerbalNode);
            if (kerbalBytes == null || kerbalBytes.Length == 0)
            {
                DarkLog.Debug("VesselWorker: Error sending kerbal - bytes are null or 0");
                return;
            }
            string kerbalHash = Common.CalculateSHA256Hash(kerbalBytes);
            bool kerbalDifferent = false;
            if (!serverKerbals.ContainsKey(pcm.name))
            {
                //New kerbal
                DarkLog.Debug("Found new kerbal, sending...");
                kerbalDifferent = true;
            }
            else if (serverKerbals[pcm.name] != kerbalHash)
            {
                DarkLog.Debug("Found changed kerbal (" + pcm.name + "), sending...");
                kerbalDifferent = true;
            }
            if (kerbalDifferent)
            {
                serverKerbals[pcm.name] = kerbalHash;
                networkWorker.SendKerbalProtoMessage(pcm.name, kerbalBytes);
            }
        }

        public void SendKerbalRemove(string kerbalName)
        {
            if (serverKerbals.ContainsKey(kerbalName))
            {
                DarkLog.Debug("Found kerbal " + kerbalName + ", sending remove...");
                serverKerbals.Remove(kerbalName);
                networkWorker.SendKerbalRemove(kerbalName);
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
                        if (lockSystem.LockExists("control-" + FlightGlobals.fetch.activeVessel.id.ToString()) && !lockSystem.LockIsOurs("control-" + FlightGlobals.fetch.activeVessel.id.ToString()))
                        {
                            spectateType = 1;
                            return true;
                        }
                        if (VesselUpdatedInFuture(FlightGlobals.fetch.activeVessel.id))
                        {
                            spectateType = 2;
                            return true;
                        }
                        if (modWorker.modControl == ModControlMode.ENABLED_STOP_INVALID_PART_LAUNCH)
                        {
                            if (vesselPartsOk.ContainsKey(FlightGlobals.fetch.activeVessel.id) ? !vesselPartsOk[FlightGlobals.fetch.activeVessel.id] : true)
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
            return runwayDistance < safetyBubbleDistance || landingPadDistance < safetyBubbleDistance;
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
                KerbalRoster newRoster = KerbalRoster.GenerateInitialCrewRoster(HighLogic.CurrentGame.Mode);
                foreach (ProtoCrewMember pcm in newRoster.Crew)
                    SendKerbalIfDifferent(pcm);
            }

            int generateKerbals = 0;
            if (serverKerbals.Count < 20)
            {
                generateKerbals = 20 - serverKerbals.Count;
                DarkLog.Debug("Generating " + generateKerbals + " new kerbals");
            }

            while (generateKerbals > 0)
            {
                ProtoCrewMember protoKerbal = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                SendKerbalIfDifferent(protoKerbal);
                generateKerbals--;
            }
            DarkLog.Debug("Kerbals loaded");
        }

        private void LoadKerbal(ConfigNode crewNode)
        {
            if (crewNode == null)
            {
                DarkLog.Debug("crewNode is null!");
                return;
            }

            if (crewNode.GetValue("type") == "Tourist")
            {
                ConfigNode dmpNode = null;
                if (crewNode.TryGetNode("DarkMultiPlayer", ref dmpNode))
                {
                    string dmpOwner = null;
                    if (dmpNode.TryGetValue("contractOwner", ref dmpOwner))
                    {
                        if (dmpOwner != dmpSettings.playerPublicKey)
                        {
                            DarkLog.Debug("Skipping load of tourist that belongs to another player");
                            return;
                        }
                    }
                }
            }

            ProtoCrewMember protoCrew = null;
            string kerbalName = null;
            //Debugging for damaged kerbal bug
            try
            {
                kerbalName = crewNode.GetValue("name");
                string traitName = crewNode.GetValue("trait");
                if (!GameDatabase.Instance.ExperienceConfigs.TraitNames.Contains(traitName))
                {
                    DarkLog.Debug("Skipping load of " + kerbalName + ", we are missing the '" + traitName + "' trait.");
                    return;
                }
                protoCrew = new ProtoCrewMember(HighLogic.CurrentGame.Mode, crewNode);
            }
            catch
            {
                DarkLog.Debug("protoCrew creation failed for " + crewNode.GetValue("name") + " (damaged kerbal type 1)");
                chatWorker.PMMessageServer("WARNING: Kerbal " + kerbalName + " is DAMAGED!. Skipping load.");
             
            }
            if (protoCrew == null)
            {
                DarkLog.Debug("protoCrew is null!");
                return;
            }
            if (string.IsNullOrEmpty(protoCrew.name))
            {
                DarkLog.Debug("protoName is blank!");
                return;
            }
            if (!HighLogic.CurrentGame.CrewRoster.Exists(protoCrew.name))
            {
                HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoCrew);
                ConfigNode kerbalNode = new ConfigNode();
                protoCrew.Save(kerbalNode);
                byte[] kerbalBytes = configNodeSerializer.Serialize(kerbalNode);
                if (kerbalBytes != null && kerbalBytes.Length != 0)
                {
                    serverKerbals[protoCrew.name] = Common.CalculateSHA256Hash(kerbalBytes);
                }
            }
            else
            {
                ConfigNode careerLogNode = crewNode.GetNode("CAREER_LOG");
                if (careerLogNode != null)
                {
                    //Insert wolf howling at the moon here
                    HighLogic.CurrentGame.CrewRoster[protoCrew.name].careerLog.Entries.Clear();
                    HighLogic.CurrentGame.CrewRoster[protoCrew.name].careerLog.Load(careerLogNode);
                }
                else
                {
                    DarkLog.Debug("Career log node for " + protoCrew.name + " is empty!");
                }

                ConfigNode flightLogNode = crewNode.GetNode("FLIGHT_LOG");
                if (flightLogNode != null)
                {
                    //And here. Someone "cannot into" lists and how to protect them.
                    HighLogic.CurrentGame.CrewRoster[protoCrew.name].flightLog.Entries.Clear();
                    HighLogic.CurrentGame.CrewRoster[protoCrew.name].flightLog.Load(flightLogNode);
                }

                HighLogic.CurrentGame.CrewRoster[protoCrew.name].courage = protoCrew.courage;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].experience = protoCrew.experience;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].experienceLevel = protoCrew.experienceLevel;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].experienceTrait = protoCrew.experienceTrait;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].gender = protoCrew.gender;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].gExperienced = protoCrew.gExperienced;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].hasToured = protoCrew.hasToured;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].isBadass = protoCrew.isBadass;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].KerbalRef = protoCrew.KerbalRef;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].outDueToG = protoCrew.outDueToG;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].rosterStatus = protoCrew.rosterStatus;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].seat = protoCrew.seat;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].seatIdx = protoCrew.seatIdx;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].stupidity = protoCrew.stupidity;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].trait = protoCrew.trait;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].type = protoCrew.type;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].UTaR = protoCrew.UTaR;
                HighLogic.CurrentGame.CrewRoster[protoCrew.name].veteran = protoCrew.veteran;
            }
        }
        //Called from main
        public void LoadVesselsIntoGame()
        {
            lock (updateQueueLock)
            {
                DarkLog.Debug("Loading vessels into game");
                int numberOfLoads = 0;

                foreach (KeyValuePair<Guid, Queue<VesselProtoUpdate>> vesselQueue in vesselProtoQueue)
                {
                    while (vesselQueue.Value.Count > 0)
                    {
                        VesselProtoUpdate vpu = vesselQueue.Value.Dequeue();
                        ConfigNode dmpNode = null;
                        if (vpu.vesselNode.TryGetNode("DarkMultiPlayer", ref dmpNode))
                        {
                            string contractOwner = null;
                            if (dmpNode.TryGetValue("contractOwner", ref contractOwner))
                            {
                                if (contractOwner != dmpSettings.playerPublicKey)
                                {
                                    DarkLog.Debug("Skipping load of contract vessel that belongs to another player");
                                    continue;
                                }
                            }
                        }
                        ProtoVessel pv = CreateSafeProtoVesselFromConfigNode(vpu.vesselNode, vpu.vesselID);
                        if (pv != null && pv.vesselID == vpu.vesselID)
                        {
                            RegisterServerVessel(pv.vesselID);
                            RegisterServerAsteriodIfVesselIsAsteroid(pv);
                            HighLogic.CurrentGame.flightState.protoVessels.Add(pv);
                            numberOfLoads++;
                        }
                        else
                        {
                            DarkLog.Debug("WARNING: Protovessel " + vpu.vesselID + " is DAMAGED!. Skipping load.");
                            chatWorker.PMMessageServer("WARNING: Protovessel " + vpu.vesselID + " is DAMAGED!. Skipping load.");
                        }
                    }
                }
                DarkLog.Debug("Vessels (" + numberOfLoads + ") loaded into game");
            }
        }
        //Also called from QuickSaveLoader
        public void LoadVessel(ConfigNode vesselNode, Guid protovesselID, bool ignoreFlyingKill)
        {
            if (vesselNode == null)
            {
                DarkLog.Debug("vesselNode is null!");
                return;
            }
            ProtoVessel currentProto = CreateSafeProtoVesselFromConfigNode(vesselNode, protovesselID);
            if (currentProto == null)
            {
                DarkLog.Debug("protoVessel is null!");
                return;
            }
            //Skip already loaded EVA's
            if ((currentProto.vesselType == VesselType.EVA) && (FlightGlobals.fetch.vessels.Find(v => v.id == currentProto.vesselID) != null))
            {
                return;
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
                if (!HighLogic.LoadedSceneIsFlight)
                {
                    //Skip hackyload if we aren't in flight.
                    DarkLog.Debug("Skipping flying vessel load - We are not in flight");
                    return;
                }
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
                    double atmoPressure = updateBody.GetPressure(-currentProto.altitude);
                    //KSP magic cut off limit for killing vessels. Works out to be ~23km on kerbin.
                    if (atmoPressure > 0.01f)
                    {
                        willGetKilledInAtmo = true;
                    }
                }
                if (willGetKilledInAtmo)
                {
                    if (!ignoreFlyingKill && (FlightGlobals.fetch.vessels.Find(v => v.id == currentProto.vesselID) != null) && vesselPartCount.ContainsKey(currentProto.vesselID) ? currentProto.protoPartSnapshots.Count == vesselPartCount[currentProto.vesselID] : false)
                    {
                        DarkLog.Debug("Skipping flying vessel load - Vessel has the same part count");
                        return;
                    }
                    DarkLog.Debug("Enabling FLYING vessel load!");
                    //If the vessel is landed it won't be killed by the atmosphere
                    currentProto.landed = true;
                    usingHackyAtmoLoad = true;
                }
            }

            RegisterServerAsteriodIfVesselIsAsteroid(currentProto);
            RegisterServerVessel(currentProto.vesselID);
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
                    string flagFile = Path.Combine(Client.dmpClient.gameDataDir, part.flagURL + ".png");
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
                if (oldVessel.id == currentProto.vesselID)
                {
                    //Don't replace the vessel if it's unpacked, not landed, close to the ground, and has the same amount of parts.
                    double hft = oldVessel.GetHeightFromTerrain();
                    if (oldVessel.loaded && !oldVessel.packed && !oldVessel.Landed && (hft > 0) && (hft < 1000) && (currentProto.protoPartSnapshots.Count == oldVessel.parts.Count))
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
                        KillVessel(oldVessel);
                    }
                }
            }

            vesselPartCount[currentProto.vesselID] = currentProto.protoPartSnapshots.Count;
            serverVesselsProtoUpdate[currentProto.vesselID] = Client.realtimeSinceStartup;
            lastLoadVessel[currentProto.vesselID] = Client.realtimeSinceStartup;
            currentProto.Load(HighLogic.CurrentGame.flightState);

            if (currentProto.vesselRef == null)
            {
                DarkLog.Debug("Protovessel " + currentProto.vesselID + " failed to create a vessel!");
                return;
            }
            if (usingHackyAtmoLoad)
            {
                hackyInAtmoLoader.AddHackyInAtmoLoad(currentProto.vesselRef);
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

        private ProtoVessel CreateSafeProtoVesselFromConfigNode(ConfigNode inputNode, Guid protovesselID)
        {
            ProtoVessel pv = null;
            try
            {
                DodgeVesselActionGroups(inputNode);
                DodgeVesselLandedStatus(inputNode);
                kerbalReassigner.DodgeKerbals(inputNode, protovesselID);
                pv = new ProtoVessel(inputNode, HighLogic.CurrentGame);
                ConfigNode cn = new ConfigNode();
                pv.Save(cn);
                List<string> partsList = null;
                PartResourceLibrary partResourceLibrary = PartResourceLibrary.Instance;
                if (modWorker.modControl != ModControlMode.DISABLED)
                {
                    partsList = modWorker.GetAllowedPartsList();
                }

                foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
                {
                    if (modWorker.modControl != ModControlMode.DISABLED)
                    {
                        if (!partsList.Contains(pps.partName))
                        {
                            DarkLog.Debug("WARNING: Protovessel " + protovesselID + " (" + pv.vesselName + ") contains the banned part '" + pps.partName + "'!. Skipping load.");
                            chatWorker.PMMessageServer("WARNING: Protovessel " + protovesselID + " (" + pv.vesselName + ") contains the banned part '" + pps.partName + "'!. Skipping load.");
                            pv = null;
                            break;
                        }
                    }
                    if (pps.partInfo == null)
                    {
                        DarkLog.Debug("WARNING: Protovessel " + protovesselID + " (" + pv.vesselName + ") contains the missing part '" + pps.partName + "'!. Skipping load.");
                        chatWorker.PMMessageServer("WARNING: Protovessel " + protovesselID + " (" + pv.vesselName + ") contains the missing part '" + pps.partName + "'!. Skipping load.");
                        ScreenMessages.PostScreenMessage("Cannot load '" + pv.vesselName + "' - you are missing " + pps.partName, 10f, ScreenMessageStyle.UPPER_CENTER);
                        pv = null;
                        break;
                    }
                    foreach (ProtoPartResourceSnapshot resource in pps.resources)
                    {
                        if (!partResourceLibrary.resourceDefinitions.Contains(resource.resourceName))
                        {
                            DarkLog.Debug("WARNING: Protovessel " + protovesselID + " (" + pv.vesselName + ") contains the missing resource '" + resource.resourceName + "'!. Skipping load.");
                            chatWorker.PMMessageServer("WARNING: Protovessel " + protovesselID + " (" + pv.vesselName + ") contains the missing resource '" + resource.resourceName + "'!. Skipping load.");
                            ScreenMessages.PostScreenMessage("Cannot load '" + pv.vesselName + "' - you are missing the resource " + resource.resourceName, 10f, ScreenMessageStyle.UPPER_CENTER);
                            pv = null;
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Damaged vessel " + protovesselID + ", exception: " + e);
                pv = null;
            }
            return pv;
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
                            //Noise. Ugh.
                            //DarkLog.Debug("Registering remote server asteroid");
                            asteroidWorker.RegisterServerAsteroid(possibleAsteroid.vesselID.ToString());
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

        private void DodgeVesselLandedStatus(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                string situation = vesselNode.GetValue("sit");
                switch (situation)
                {
                    case "LANDED":
                        vesselNode.SetValue("landed", "True");
                        vesselNode.SetValue("splashed", "False");
                        break;
                    case "SPLASHED":
                        vesselNode.SetValue("splashed", "True");
                        vesselNode.SetValue("landed", "False");
                        break;
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

        private void FixVesselManeuverNodes(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                ConfigNode flightPlanNode = vesselNode.GetNode("FLIGHTPLAN");
                List<ConfigNode> expiredManeuverNodes = new List<ConfigNode>();
                if (flightPlanNode != null)
                {
                    foreach (ConfigNode maneuverNode in flightPlanNode.GetNodes("MANEUVER"))
                    {
                        double maneuverUT = double.Parse(maneuverNode.GetValue("UT"));
                        double currentTime = Planetarium.GetUniversalTime();
                        if (currentTime > maneuverUT)
                            expiredManeuverNodes.Add(maneuverNode);
                    }

                    if (expiredManeuverNodes.Count != 0)
                    {
                        foreach (ConfigNode removeNode in expiredManeuverNodes)
                        {
                            DarkLog.Debug("Removed maneuver node from vessel, it was expired!");
                            flightPlanNode.RemoveNode(removeNode);
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

        public void OnVesselRenamed(GameEvents.HostedFromToAction<Vessel, string> eventData)
        {
            Vessel renamedVessel = eventData.host;
            string toName = eventData.to;
            DarkLog.Debug("Sending vessel [" + renamedVessel.name + "] renamed to [" + toName + "]");
            SendVesselUpdateIfNeeded(renamedVessel);
        }

        public void OnVesselDestroyed(Vessel dyingVessel)
        {
            Guid dyingVesselID = dyingVessel.id;
            //Docking destructions
            if (dyingVesselID == fromDockedVesselID || dyingVesselID == toDockedVesselID)
            {
                DarkLog.Debug("Removing vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + " from the server: Docked");
                if (serverVessels.Contains(dyingVesselID))
                {
                    serverVessels.Remove(dyingVesselID);
                }
                networkWorker.SendVesselRemove(dyingVesselID, true);
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
                    fromDockedVesselID = Guid.Empty;
                }
                if (toDockedVesselID == dyingVesselID)
                {
                    toDockedVesselID = Guid.Empty;
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
            if (lockSystem.LockExists("update-" + dyingVesselID) && !lockSystem.LockIsOurs("update-" + dyingVesselID))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", update lock owned by another player.");
                return;
            }

            if (!serverVessels.Contains(dyingVessel.id))
            {
                DarkLog.Debug("Skipping the removal of vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + ", not a server vessel.");
                return;
            }

            DarkLog.Debug("Removing vessel " + dyingVesselID + ", name: " + dyingVessel.vesselName + " from the server: Destroyed");
            SendKerbalsInVessel(dyingVessel);
            serverVessels.Remove(dyingVesselID);
            if (serverVesselsProtoUpdate.ContainsKey(dyingVesselID))
            {
                serverVesselsProtoUpdate.Remove(dyingVesselID);
            }
            if (serverVesselsPositionUpdate.ContainsKey(dyingVesselID))
            {
                serverVesselsPositionUpdate.Remove(dyingVesselID);
            }
            networkWorker.SendVesselRemove(dyingVesselID, false);
        }

        //TODO: I don't know what this bool does?
        public void OnVesselRecovered(ProtoVessel recoveredVessel, bool something)
        {
            Guid recoveredVesselID = recoveredVessel.vesselID;

            if (lockSystem.LockExists("control-" + recoveredVesselID) && !lockSystem.LockIsOurs("control-" + recoveredVesselID))
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
            SendKerbalsInVessel(recoveredVessel);
            serverVessels.Remove(recoveredVesselID);
            networkWorker.SendVesselRemove(recoveredVesselID, false);
        }

        public void OnVesselTerminated(ProtoVessel terminatedVessel)
        {
            Guid terminatedVesselID = terminatedVessel.vesselID;
            //Check the vessel hasn't been changed in the future
            if (lockSystem.LockExists("control-" + terminatedVesselID) && !lockSystem.LockIsOurs("control-" + terminatedVesselID))
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
            SendKerbalsInVessel(terminatedVessel);
            serverVessels.Remove(terminatedVesselID);
            networkWorker.SendVesselRemove(terminatedVesselID, false);
        }

        public void SendKerbalsInVessel(ProtoVessel vessel)
        {
            if (vessel == null)
            {
                return;
            }
            if (vessel.protoPartSnapshots == null)
            {
                return;
            }
            foreach (ProtoPartSnapshot part in vessel.protoPartSnapshots)
            {
                if (part == null)
                {
                    continue;
                }
                foreach (ProtoCrewMember pcm in part.protoModuleCrew)
                {
                    // Ignore the tourists except those that haven't yet toured
                    if ((pcm.type == ProtoCrewMember.KerbalType.Tourist && !pcm.hasToured) || pcm.type != ProtoCrewMember.KerbalType.Tourist)
                        SendKerbalIfDifferent(pcm);
                }
            }
        }

        public void SendKerbalsInVessel(Vessel vessel)
        {
            if (vessel == null)
            {
                return;
            }
            if (vessel.parts == null)
            {
                return;
            }
            foreach (Part part in vessel.parts)
            {
                if (part == null)
                {
                    continue;
                }
                foreach (ProtoCrewMember pcm in part.protoModuleCrew)
                {
                    // Ignore the tourists except those that haven't yet toured
                    if ((pcm.type == ProtoCrewMember.KerbalType.Tourist && !pcm.hasToured) || pcm.type != ProtoCrewMember.KerbalType.Tourist)
                        SendKerbalIfDifferent(pcm);
                }
            }
        }

        public bool VesselRecentlyLoaded(Guid vesselID)
        {
            return lastLoadVessel.ContainsKey(vesselID) ? ((Client.realtimeSinceStartup - lastLoadVessel[vesselID]) < 10f) : false;
        }

        public bool VesselRecentlyKilled(Guid vesselID)
        {
            return lastKillVesselDestroy.ContainsKey(vesselID) ? ((Client.realtimeSinceStartup - lastKillVesselDestroy[vesselID]) < 10f) : false;
        }

        public bool VesselUpdatedInFuture(Guid vesselID)
        {
            return latestVesselUpdate.ContainsKey(vesselID) ? ((latestVesselUpdate[vesselID] + 3f) > Planetarium.GetUniversalTime()) : false;
        }

        public bool LenientVesselUpdatedInFuture(Guid vesselID)
        {
            return latestVesselUpdate.ContainsKey(vesselID) ? ((latestVesselUpdate[vesselID] - 3f) > Planetarium.GetUniversalTime()) : false;
        }

        public void OnVesselDock(GameEvents.FromToAction<Part, Part> partAction)
        {
            DarkLog.Debug("Vessel docking detected!");
            if (!isSpectating)
            {
                if (partAction.from.vessel != null && partAction.to.vessel != null)
                {
                    bool fromVesselUpdateLockExists = lockSystem.LockExists("update-" + partAction.from.vessel.id.ToString());
                    bool toVesselUpdateLockExists = lockSystem.LockExists("update-" + partAction.to.vessel.id.ToString());
                    bool fromVesselUpdateLockIsOurs = lockSystem.LockIsOurs("update-" + partAction.from.vessel.id.ToString());
                    bool toVesselUpdateLockIsOurs = lockSystem.LockIsOurs("update-" + partAction.to.vessel.id.ToString());
                    if (fromVesselUpdateLockIsOurs || toVesselUpdateLockIsOurs || !fromVesselUpdateLockExists || !toVesselUpdateLockExists)
                    {
                        DarkLog.Debug("Vessel docking, from: " + partAction.from.vessel.id + ", name: " + partAction.from.vessel.vesselName);
                        DarkLog.Debug("Vessel docking, to: " + partAction.to.vessel.id + ", name: " + partAction.to.vessel.vesselName);
                        if (FlightGlobals.fetch.activeVessel != null)
                        {
                            DarkLog.Debug("Vessel docking, our vessel: " + FlightGlobals.fetch.activeVessel.id);
                        }
                        fromDockedVesselID = partAction.from.vessel.id;
                        toDockedVesselID = partAction.to.vessel.id;
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
                DarkLog.Debug("Spectator docking happened. This needs to be fixed later.");
            }
        }

        private void OnCrewBoard(GameEvents.FromToAction<Part, Part> partAction)
        {
            DarkLog.Debug("Crew boarding detected!");
            if (!isSpectating)
            {
                DarkLog.Debug("EVA Boarding, from: " + partAction.from.vessel.id + ", name: " + partAction.from.vessel.vesselName);
                DarkLog.Debug("EVA Boarding, to: " + partAction.to.vessel.id + ", name: " + partAction.to.vessel.vesselName);
                fromDockedVesselID = partAction.from.vessel.id;
                toDockedVesselID = partAction.to.vessel.id;
            }
        }

        private void OnKerbalRemoved(ProtoCrewMember pcm)
        {
            SendKerbalRemove(pcm.name);
        }

        public void KillVessel(Vessel killVessel)
        {
            if (killVessel != null)
            {
                DarkLog.Debug("Killing vessel: " + killVessel.id.ToString());

                //Forget the dying vessel
                partKiller.ForgetVessel(killVessel);
                hackyInAtmoLoader.ForgetVessel(killVessel);

                //Try to unload the vessel first.
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

                //Remove the kerbal from the craft
                foreach (ProtoPartSnapshot pps in killVessel.protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoCrewMember pcm in pps.protoModuleCrew.ToArray())
                    {
                        pps.RemoveCrew(pcm);
                    }
                }
                //Add it to the delay kill list to make sure it dies.
                //With KSP, If it doesn't work first time, keeping doing it until it does.
                if (!delayKillVessels.Contains(killVessel))
                {
                    delayKillVessels.Add(killVessel);
                }
                lastKillVesselDestroy[killVessel.id] = Client.realtimeSinceStartup;
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

        private void RemoveVessel(Guid vesselID, bool isDockingUpdate, string dockingPlayer)
        {
            for (int i = FlightGlobals.fetch.vessels.Count - 1; i >= 0; i--)
            {
                Vessel checkVessel = FlightGlobals.fetch.vessels[i];
                if (checkVessel.id == vesselID)
                {
                    if (isDockingUpdate)
                    {
                        if (FlightGlobals.fetch.activeVessel != null ? FlightGlobals.fetch.activeVessel.id.ToString() == checkVessel.id.ToString() : false)
                        {
                            Vessel dockingPlayerVessel = null;
                            foreach (Vessel findVessel in FlightGlobals.fetch.vessels)
                            {
                                if (lockSystem.LockOwner("control-" + findVessel.id.ToString()) == dockingPlayer)
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
            lock (updateQueueLock)
            {
                KerbalEntry newEntry = new KerbalEntry();
                newEntry.planetTime = planetTime;
                newEntry.kerbalNode = kerbalNode;
                if (!kerbalProtoQueue.ContainsKey(kerbalName))
                {
                    kerbalProtoQueue.Add(kerbalName, new Queue<KerbalEntry>());
                }

                Queue<KerbalEntry> keQueue = kerbalProtoQueue[kerbalName];
                if (kerbalProtoHistoryTime.ContainsKey(kerbalName))
                {
                    //If we get a remove older than the current queue peek, then someone has gone back in time and the timeline needs to be fixed.
                    if (planetTime < kerbalProtoHistoryTime[kerbalName])
                    {
                        DarkLog.Debug("Kerbal " + kerbalName + " went back in time - rewriting the remove history for it.");
                        Queue<KerbalEntry> newQueue = new Queue<KerbalEntry>();
                        while (keQueue.Count > 0)
                        {
                            KerbalEntry oldKe = keQueue.Dequeue();
                            //Save the updates from before the revert
                            if (oldKe.planetTime < planetTime)
                            {
                                newQueue.Enqueue(oldKe);
                            }
                        }
                        keQueue = newQueue;
                        kerbalProtoQueue[kerbalName] = newQueue;
                        //Clean the history too
                        if (dmpSettings.revertEnabled)
                        {
                            if (kerbalProtoHistory.ContainsKey(kerbalName))
                            {
                                List<KerbalEntry> keh = kerbalProtoHistory[kerbalName];
                                foreach (KerbalEntry oldKe in keh.ToArray())
                                {
                                    if (oldKe.planetTime > planetTime)
                                    {
                                        keh.Remove(oldKe);
                                    }
                                }
                            }
                        }
                    }
                }

                keQueue.Enqueue(newEntry);
                if (dmpSettings.revertEnabled)
                {
                    if (!kerbalProtoHistory.ContainsKey(kerbalName))
                    {
                        kerbalProtoHistory.Add(kerbalName, new List<KerbalEntry>());
                    }
                    kerbalProtoHistory[kerbalName].Add(newEntry);
                }
                kerbalProtoHistoryTime[kerbalName] = planetTime;
            }
        }
        //Called from networkWorker
        public void QueueVesselRemove(Guid vesselID, double planetTime, bool isDockingUpdate, string dockingPlayer)
        {
            lock (updateQueueLock)
            {

                if (!vesselRemoveQueue.ContainsKey(vesselID))
                {
                    vesselRemoveQueue.Add(vesselID, new Queue<VesselRemoveEntry>());
                }

                Queue<VesselRemoveEntry> vrQueue = vesselRemoveQueue[vesselID];
                if (vesselRemoveHistoryTime.ContainsKey(vesselID))
                {
                    //If we get a remove older than the current queue peek, then someone has gone back in time and the timeline needs to be fixed.
                    if (planetTime < vesselRemoveHistoryTime[vesselID])
                    {
                        DarkLog.Debug("Vessel " + vesselID + " went back in time - rewriting the remove history for it.");
                        Queue<VesselRemoveEntry> newQueue = new Queue<VesselRemoveEntry>();
                        while (vrQueue.Count > 0)
                        {
                            VesselRemoveEntry oldVre = vrQueue.Dequeue();
                            //Save the updates from before the revert
                            if (oldVre.planetTime < planetTime)
                            {
                                newQueue.Enqueue(oldVre);
                            }
                        }
                        vrQueue = newQueue;
                        vesselRemoveQueue[vesselID] = newQueue;
                        //Clean the history too
                        if (dmpSettings.revertEnabled)
                        {
                            if (vesselRemoveHistory.ContainsKey(vesselID))
                            {
                                List<VesselRemoveEntry> vrh = vesselRemoveHistory[vesselID];
                                foreach (VesselRemoveEntry oldVr in vrh.ToArray())
                                {
                                    if (oldVr.planetTime > planetTime)
                                    {
                                        vrh.Remove(oldVr);
                                    }
                                }
                            }
                        }
                    }
                }

                VesselRemoveEntry vre = new VesselRemoveEntry();
                vre.planetTime = planetTime;
                vre.vesselID = vesselID;
                vre.isDockingUpdate = isDockingUpdate;
                vre.dockingPlayer = dockingPlayer;
                vrQueue.Enqueue(vre);
                if (latestVesselUpdate.ContainsKey(vesselID) ? latestVesselUpdate[vesselID] < planetTime : true)
                {
                    latestVesselUpdate[vesselID] = planetTime;
                }
                if (dmpSettings.revertEnabled)
                {
                    if (!vesselRemoveHistory.ContainsKey(vesselID))
                    {
                        vesselRemoveHistory.Add(vesselID, new List<VesselRemoveEntry>());
                    }
                    vesselRemoveHistory[vesselID].Add(vre);
                }
                vesselRemoveHistoryTime[vesselID] = planetTime;
            }
        }

        public void QueueVesselProto(Guid vesselID, double planetTime, ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                lock (updateQueueLock)
                {
                    if (!vesselProtoQueue.ContainsKey(vesselID))
                    {
                        vesselProtoQueue.Add(vesselID, new Queue<VesselProtoUpdate>());
                    }
                    Queue<VesselProtoUpdate> vpuQueue = vesselProtoQueue[vesselID];
                    if (vesselProtoHistoryTime.ContainsKey(vesselID))
                    {
                        //If we get an update older than the current queue peek, then someone has gone back in time and the timeline needs to be fixed.
                        if (planetTime < vesselProtoHistoryTime[vesselID])
                        {
                            DarkLog.Debug("Vessel " + vesselID + " went back in time - rewriting the proto update history for it.");
                            Queue<VesselProtoUpdate> newQueue = new Queue<VesselProtoUpdate>();
                            while (vpuQueue.Count > 0)
                            {
                                VesselProtoUpdate oldVpu = vpuQueue.Dequeue();
                                //Save the updates from before the revert
                                if (oldVpu.planetTime < planetTime)
                                {
                                    newQueue.Enqueue(oldVpu);
                                }
                            }
                            vpuQueue = newQueue;
                            vesselProtoQueue[vesselID] = newQueue;
                            //Clean the history too
                            if (dmpSettings.revertEnabled)
                            {
                                if (vesselProtoHistory.ContainsKey(vesselID))
                                {
                                    List<VesselProtoUpdate> vpuh = vesselProtoHistory[vesselID];
                                    foreach (VesselProtoUpdate oldVpu in vpuh.ToArray())
                                    {
                                        if (oldVpu.planetTime > planetTime)
                                        {
                                            vpuh.Remove(oldVpu);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    //Create new VPU to be stored.
                    VesselProtoUpdate vpu = new VesselProtoUpdate();
                    vpu.vesselID = vesselID;
                    vpu.planetTime = planetTime;
                    vpu.vesselNode = vesselNode;
                    vpuQueue.Enqueue(vpu);
                    //Revert support
                    if (dmpSettings.revertEnabled)
                    {
                        if (!vesselProtoHistory.ContainsKey(vesselID))
                        {
                            vesselProtoHistory.Add(vesselID, new List<VesselProtoUpdate>());
                        }
                        vesselProtoHistory[vesselID].Add(vpu);
                    }
                    vesselProtoHistoryTime[vesselID] = planetTime;
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
                Queue<VesselUpdate> vuQueue = vesselUpdateQueue[update.vesselID];
                if (vesselUpdateHistoryTime.ContainsKey(update.vesselID))
                {
                    //If we get an update older than the current queue peek, then someone has gone back in time and the timeline needs to be fixed.
                    if (update.planetTime < vesselUpdateHistoryTime[update.vesselID])
                    {
                        DarkLog.Debug("Vessel " + update.vesselID + " went back in time - rewriting the update history for it.");
                        Queue<VesselUpdate> newQueue = new Queue<VesselUpdate>();
                        while (vuQueue.Count > 0)
                        {
                            VesselUpdate oldVu = vuQueue.Dequeue();
                            //Save the updates from before the revert
                            if (oldVu.planetTime < update.planetTime)
                            {
                                newQueue.Enqueue(oldVu);
                            }
                        }
                        vuQueue = newQueue;
                        vesselUpdateQueue[update.vesselID] = newQueue;
                        //Clean the history too
                        if (dmpSettings.revertEnabled)
                        {
                            if (vesselUpdateHistory.ContainsKey(update.vesselID))
                            {
                                List<VesselUpdate> vuh = vesselUpdateHistory[update.vesselID];
                                foreach (VesselUpdate oldVu in vuh.ToArray())
                                {
                                    if (oldVu.planetTime > update.planetTime)
                                    {
                                        vuh.Remove(oldVu);
                                    }
                                }
                            }
                        }
                    }
                }
                //We might have gotten the update late, so set the next update if we've ran out.
                if (vuQueue.Count == 0)
                {
                    vesselPackedUpdater.SetNextUpdate(update.vesselID, update);
                }
                vuQueue.Enqueue(update);
                //Mark the last update time
                if (latestVesselUpdate.ContainsKey(update.vesselID) ? latestVesselUpdate[update.vesselID] < update.planetTime : true)
                {
                    latestVesselUpdate[update.vesselID] = update.planetTime;
                }
                //Revert support
                if (dmpSettings.revertEnabled)
                {
                    if (!vesselUpdateHistory.ContainsKey(update.vesselID))
                    {
                        vesselUpdateHistory.Add(update.vesselID, new List<VesselUpdate>());
                    }
                    vesselUpdateHistory[update.vesselID].Add(update);
                }
                vesselUpdateHistoryTime[update.vesselID] = update.planetTime;
            }
        }

        public void QueueActiveVessel(string player, Guid vesselID)
        {
            ActiveVesselEntry ave = new ActiveVesselEntry();
            ave.player = player;
            ave.vesselID = vesselID;
            newActiveVessels.Enqueue(ave);
        }

        public void IgnoreVessel(Guid vesselID)
        {
            if (ignoreVessels.Contains(vesselID))
            {
                ignoreVessels.Add(vesselID);
            }
        }

        public void RegisterServerVessel(Guid vesselID)
        {
            if (!serverVessels.Contains(vesselID))
            {
                serverVessels.Add(vesselID);
            }
        }

        public void Stop()
        {
            workerEnabled = false;
            dmpGame.fixedUpdateEvent.Remove(FixedUpdate);
            if (registered)
            {
                registered = false;
                UnregisterGameHooks();
                if (wasSpectating)
                {
                    InputLockManager.RemoveControlLock(DARK_SPECTATE_LOCK);
                }
            }
        }

        public int GetStatistics(string statType)
        {
            switch (statType)
            {
                case "StoredFutureUpdates":
                    {
                        int futureUpdates = 0;
                        foreach (KeyValuePair<Guid, Queue<VesselUpdate>> vUQ in vesselUpdateQueue)
                        {
                            futureUpdates += vUQ.Value.Count;
                        }
                        return futureUpdates;
                    }
                case "StoredFutureProtoUpdates":
                    {
                        int futureProtoUpdates = 0;
                        foreach (KeyValuePair<Guid, Queue<VesselProtoUpdate>> vPQ in vesselProtoQueue)
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
        public Guid vesselID;
    }

    class VesselRemoveEntry
    {
        public Guid vesselID;
        public double planetTime;
        public bool isDockingUpdate;
        public string dockingPlayer;
    }

    class KerbalEntry
    {
        public double planetTime;
        public ConfigNode kerbalNode;
    }
}

