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
        private const float UNPACK_INTERVAL = 3f;

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
            lock (loadingFlyingVessels)
            {
                if (loadingFlyingVessels.ContainsKey(vessel.id))
                {
                    DarkLog.Debug("Hacky load successful: Vessel is off rails");
                    HackyFlyingVesselLoad hfvl = loadingFlyingVessels[vessel.id];
                    hfvl.flyingVessel.Landed = false;
                    hfvl.flyingVessel.Splashed = false;
                    hfvl.flyingVessel.landedAt = string.Empty;
                    hfvl.flyingVessel.situation = Vessel.Situations.FLYING;
                    if (hfvl.lastVesselUpdate != null)
                    {
                        //Stop the vessel from exploding while in unpack range.
                        hfvl.lastVesselUpdate.Apply();
                    }
                    loadingFlyingVessels.Remove(vessel.id);
                }
            }
        }


        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            lock (loadingFlyingVessels)
            {
                foreach (KeyValuePair<Guid, HackyFlyingVesselLoad> kvp in loadingFlyingVessels)
                {
                    VesselWorker.fetch.KillVessel(kvp.Value.flyingVessel);
                }
                loadingFlyingVessels.Clear();
            }
        }

        private void UpdateVessels()
        {
            lock (loadingFlyingVessels)
            {
                foreach (KeyValuePair<Guid, HackyFlyingVesselLoad> kvp in loadingFlyingVessels)
                {
                    HackyFlyingVesselLoad hfvl = kvp.Value;

                    if (hfvl.flyingVessel == null || hfvl.flyingVessel.state == Vessel.State.DEAD)
                    {
                        DarkLog.Debug("Hacky load failed: Vessel destroyed");
                        deleteList.Add(kvp.Key);
                        continue;
                    }

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
                        }
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
        }

        public void SetVesselUpdate(Guid guid, VesselUpdate vesselUpdate)
        {
            lock (loadingFlyingVessels)
            {
                if (loadingFlyingVessels.ContainsKey(guid))
                {
                    loadingFlyingVessels[guid].lastVesselUpdate = vesselUpdate;
                }
            }
        }

        public void AddHackyInAtmoLoad(Vessel hackyVessel)
        {
            lock (loadingFlyingVessels)
            {
                if (!loadingFlyingVessels.ContainsKey(hackyVessel.id))
                {
                    HackyFlyingVesselLoad hfvl = new HackyFlyingVesselLoad();
                    hfvl.flyingVessel = hackyVessel;
                    hfvl.lastUnpackTime = Time.realtimeSinceStartup;
                    loadingFlyingVessels.Add(hackyVessel.id, hfvl);
                }
            }
        }

        private void RegisterGameHooks()
        {
            if (!registered)
            {
                registered = true;
                GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
                GameEvents.onVesselGoOffRails.Add(OnVesselUnpack);
            }
        }

        private void UnregisterGameHooks()
        {
            if (registered)
            {
                registered = true;
                GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
                GameEvents.onVesselGoOffRails.Remove(OnVesselUnpack);
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