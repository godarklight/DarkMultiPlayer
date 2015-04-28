using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class HackyInAtmoLoader
    {
        private static HackyInAtmoLoader singleton;
        public bool workerEnabled;
        private bool registered;
        private List<Guid> iterateVessels = new List<Guid>();
        private Dictionary<Guid, HackyFlyingVesselLoad> loadingFlyingVessels = new Dictionary<Guid, HackyFlyingVesselLoad>();
        private Dictionary<Guid, float> lastPackTime = new Dictionary<Guid, float>();
        private const float UNPACK_INTERVAL = 3f;
        private const float LANDED_LOAD_DISTANCE_DEFAULT = 2250f;
        private const float LANDED_UNLOAD_DISTANCE_DEFAULT = 2700f;

        public static HackyInAtmoLoader fetch
        {
            get
            {
                return singleton;
            }
        }

        private void FixedUpdate()
        {
            if (!workerEnabled)
            {
                return;
            }
            if (!registered)
            {
                RegisterGameHooks();
            }
            UpdateVessels();
        }

        private void OnVesselUnpack(Vessel vessel)
        {
            if (loadingFlyingVessels.ContainsKey(vessel.id))
            {
                DarkLog.Debug("Hacky load successful: Vessel is off rails");
                HackyFlyingVesselLoad hfvl = loadingFlyingVessels[vessel.id];
                hfvl.flyingVessel.Landed = false;
                hfvl.flyingVessel.Splashed = false;
                hfvl.flyingVessel.landedAt = string.Empty;
                hfvl.flyingVessel.situation = Vessel.Situations.FLYING;
                hfvl.flyingVessel.vesselRanges.landed.load = LANDED_LOAD_DISTANCE_DEFAULT;
                hfvl.flyingVessel.vesselRanges.landed.unload = LANDED_UNLOAD_DISTANCE_DEFAULT;
                if (hfvl.lastVesselUpdate != null)
                {
                    //Stop the vessel from exploding while in unpack range.
                    hfvl.lastVesselUpdate.Apply();
                }
                loadingFlyingVessels.Remove(vessel.id);
            }
        }

        private void OnVesselPack(Vessel vessel)
        {
            if (vessel.situation == Vessel.Situations.FLYING)
            {
                lastPackTime[vessel.id] = UnityEngine.Time.realtimeSinceStartup;
            }
        }

        private void OnVesselWillDestroy(Vessel vessel)
        {
            bool pilotedByAnotherPlayer = LockSystem.fetch.LockExists("control-" + vessel.id) && !LockSystem.fetch.LockIsOurs("control-" + vessel.id);
            bool updatedByAnotherPlayer = LockSystem.fetch.LockExists("update-" + vessel.id) && !LockSystem.fetch.LockIsOurs("update-" + vessel.id);
            bool updatedInTheFuture = VesselWorker.fetch.VesselUpdatedInFuture(vessel.id);
            //Vessel was packed within the last 5 seconds
            if (lastPackTime.ContainsKey(vessel.id) && (UnityEngine.Time.realtimeSinceStartup - lastPackTime[vessel.id]) < 5f)
            {
                lastPackTime.Remove(vessel.id);
                if (vessel.situation == Vessel.Situations.FLYING && (pilotedByAnotherPlayer || updatedByAnotherPlayer || updatedInTheFuture))
                {
                    DarkLog.Debug("Hacky load: Saving player vessel getting packed in atmosphere");
                    ProtoVessel pv = vessel.BackupVessel();
                    ConfigNode savedNode = new ConfigNode();
                    pv.Save(savedNode);
                    VesselWorker.fetch.LoadVessel(savedNode, vessel.id, true);
                }
            }
            if (lastPackTime.ContainsKey(vessel.id))
            {
                lastPackTime.Remove(vessel.id);
            }
        }

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            iterateVessels.Clear();
            iterateVessels.AddRange(loadingFlyingVessels.Keys);
            foreach (Guid vesselID in iterateVessels)
            {
                HackyFlyingVesselLoad hfvl = loadingFlyingVessels[vesselID];
                VesselWorker.fetch.KillVessel(hfvl.flyingVessel);
            }
            loadingFlyingVessels.Clear();
        }

        private void UpdateVessels()
        {
            iterateVessels.Clear();
            iterateVessels.AddRange(loadingFlyingVessels.Keys);
            foreach (Guid vesselID in iterateVessels)
            {
                if (!loadingFlyingVessels.ContainsKey(vesselID))
                {
                    continue;
                }
                HackyFlyingVesselLoad hfvl = loadingFlyingVessels[vesselID];

                if (hfvl.flyingVessel == null || hfvl.flyingVessel.state == Vessel.State.DEAD)
                {
                    DarkLog.Debug("Hacky load failed: Vessel destroyed");
                    loadingFlyingVessels.Remove(vesselID);
                    continue;
                }

                if (!FlightGlobals.fetch.vessels.Contains(hfvl.flyingVessel))
                {
                    DarkLog.Debug("Hacky load failed: Vessel destroyed");
                    loadingFlyingVessels.Remove(vesselID);
                    continue;
                }

                if (!LockSystem.fetch.LockExists("update-" + vesselID) || LockSystem.fetch.LockIsOurs("update-" + vesselID))
                {
                    DarkLog.Debug("Hacky load removed: Vessel stopped being controlled by another player");
                    loadingFlyingVessels.Remove(vesselID);
                    VesselWorker.fetch.KillVessel(hfvl.flyingVessel);
                    continue;
                }

                if (hfvl.flyingVessel.loaded)
                {
                    if ((Time.realtimeSinceStartup - hfvl.lastUnpackTime) > UNPACK_INTERVAL)
                    {
                        DarkLog.Debug("Hacky load attempting to take loaded vessel off rails");
                        hfvl.lastUnpackTime = Time.realtimeSinceStartup;
                        try
                        {
                            hfvl.flyingVessel.GoOffRails();
                        }
                        catch (Exception e)
                        {
                            //Just in case, I don't think this can throw but you never really know with KSP.
                            DarkLog.Debug("Hacky load failed to take vessel of rails: " + e.Message);
                        }
                        continue;
                    }
                }

                if (!hfvl.flyingVessel.packed)
                {
                    DarkLog.Debug("Hacky load successful: Vessel is off rails");
                    loadingFlyingVessels.Remove(vesselID);
                    hfvl.flyingVessel.Landed = false;
                    hfvl.flyingVessel.Splashed = false;
                    hfvl.flyingVessel.landedAt = string.Empty;
                    hfvl.flyingVessel.situation = Vessel.Situations.FLYING;
                    hfvl.flyingVessel.vesselRanges.landed.load = LANDED_LOAD_DISTANCE_DEFAULT;
                    hfvl.flyingVessel.vesselRanges.landed.unload = LANDED_UNLOAD_DISTANCE_DEFAULT;
                    continue;
                }

                double atmoPressure = hfvl.flyingVessel.mainBody.GetPressure(hfvl.flyingVessel.altitude);
                if (atmoPressure < 0.01d)
                {
                    DarkLog.Debug("Hacky load successful: Vessel is now safe from atmo");
                    loadingFlyingVessels.Remove(vesselID);
                    hfvl.flyingVessel.Landed = false;
                    hfvl.flyingVessel.Splashed = false;
                    hfvl.flyingVessel.landedAt = string.Empty;
                    hfvl.flyingVessel.situation = Vessel.Situations.FLYING;
                    hfvl.flyingVessel.vesselRanges.landed.load = LANDED_LOAD_DISTANCE_DEFAULT;
                    hfvl.flyingVessel.vesselRanges.landed.unload = LANDED_UNLOAD_DISTANCE_DEFAULT;
                    continue;
                }

                if (hfvl.lastVesselUpdate != null)
                {
                    hfvl.lastVesselUpdate.Apply();
                }
            }
        }

        public void SetVesselUpdate(Guid guid, VesselUpdate vesselUpdate)
        {
            if (loadingFlyingVessels.ContainsKey(guid))
            {
                loadingFlyingVessels[guid].lastVesselUpdate = vesselUpdate;
            }
        }

        public void AddHackyInAtmoLoad(Vessel hackyVessel)
        {
            if (!loadingFlyingVessels.ContainsKey(hackyVessel.id))
            {
                HackyFlyingVesselLoad hfvl = new HackyFlyingVesselLoad();
                hfvl.flyingVessel = hackyVessel;
                hfvl.flyingVessel.vesselRanges.landed.load = hfvl.flyingVessel.vesselRanges.flying.unload - 100f;
                DarkLog.Debug("Default landed unload: " + hfvl.flyingVessel.vesselRanges.landed.unload);
                hfvl.flyingVessel.vesselRanges.landed.unload = hfvl.flyingVessel.vesselRanges.flying.unload;
                hfvl.lastUnpackTime = Time.realtimeSinceStartup;
                loadingFlyingVessels.Add(hackyVessel.id, hfvl);
            }
        }

        public void ForgetVessel(Vessel hackyVessel)
        {
            if (loadingFlyingVessels.ContainsKey(hackyVessel.id))
            {
                loadingFlyingVessels.Remove(hackyVessel.id);
            }
        }

        private void RegisterGameHooks()
        {
            if (!registered)
            {
                registered = true;
                GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
                GameEvents.onVesselGoOffRails.Add(OnVesselUnpack);
                GameEvents.onVesselGoOnRails.Add(OnVesselPack);
                GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            }
        }

        private void UnregisterGameHooks()
        {
            if (registered)
            {
                registered = true;
                GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
                GameEvents.onVesselGoOffRails.Remove(OnVesselUnpack);
                GameEvents.onVesselGoOnRails.Add(OnVesselPack);
                GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
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
                singleton = new HackyInAtmoLoader();
                Client.fixedUpdateEvent.Add(singleton.FixedUpdate);
            }
        }
    }

    class HackyFlyingVesselLoad
    {
        public Vessel flyingVessel;
        public VesselUpdate lastVesselUpdate;
        public double lastUnpackTime;
    }
}