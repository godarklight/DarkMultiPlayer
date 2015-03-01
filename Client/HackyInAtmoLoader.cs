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
        private Dictionary<Guid, HackyFlyingVesselLoad> loadingFlyingVessels = new Dictionary<Guid, HackyFlyingVesselLoad>();
        private List<Guid> deleteList = new List<Guid>();

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

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            foreach (KeyValuePair<Guid, HackyFlyingVesselLoad> kvp in loadingFlyingVessels)
            {
                VesselWorker.fetch.KillVessel(kvp.Value.flyingVessel);
            }
            loadingFlyingVessels.Clear();
        }

        private void UpdateVessels()
        {
            foreach (KeyValuePair<Guid, HackyFlyingVesselLoad> kvp in loadingFlyingVessels)
            {
                HackyFlyingVesselLoad hfvl = kvp.Value;
                if (!FlightGlobals.fetch.vessels.Contains(hfvl.flyingVessel))
                {
                    DarkLog.Debug("Hacky load failed: Vessel destroyed");
                    deleteList.Add(kvp.Key);
                    continue;
                }

                string vesselID = hfvl.flyingVessel.id.ToString();

                if (!LockSystem.fetch.LockExists("update-" + vesselID) || LockSystem.fetch.LockIsOurs("update-" + vesselID))
                {
                    DarkLog.Debug("Hacky load removed: Vessel stopped being controlled by another player");
                    deleteList.Add(kvp.Key);
                    VesselWorker.fetch.KillVessel(hfvl.flyingVessel);
                    continue;
                }

                if (!hfvl.flyingVessel.packed)
                {
                    DarkLog.Debug("Hacky load successful: Vessel is off rails");
                    deleteList.Add(kvp.Key);
                    hfvl.flyingVessel.Landed = false;
                    hfvl.flyingVessel.Splashed = false;
                    hfvl.flyingVessel.landedAt = string.Empty;
                    hfvl.flyingVessel.situation = Vessel.Situations.FLYING;
                    continue;
                }

                double atmoPressure = hfvl.flyingVessel.mainBody.staticPressureASL * Math.Pow(Math.E, ((-hfvl.flyingVessel.altitude) / (hfvl.flyingVessel.mainBody.atmosphereScaleHeight * 1000)));
                if (atmoPressure < 0.01)
                {
                    DarkLog.Debug("Hacky load successful: Vessel is now safe from atmo");
                    deleteList.Add(kvp.Key);
                    hfvl.flyingVessel.Landed = false;
                    hfvl.flyingVessel.Splashed = false;
                    hfvl.flyingVessel.landedAt = string.Empty;
                    hfvl.flyingVessel.situation = Vessel.Situations.FLYING;
                    continue;
                }

                if (hfvl.lastVesselUpdate != null)
                {
                    hfvl.lastVesselUpdate.Apply();
                }
            }

            foreach (var deleteID in deleteList)
            {
                loadingFlyingVessels.Remove(deleteID);
            }
            deleteList.Clear();
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
                loadingFlyingVessels.Add(hackyVessel.id, hfvl);
            }
        }

        private void RegisterGameHooks()
        {
            if (!registered)
            {
                registered = true;
                GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
            }
        }

        private void UnregisterGameHooks()
        {
            if (registered)
            {
                registered = true;
                GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
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
    }
}