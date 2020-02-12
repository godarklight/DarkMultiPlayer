using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselRangeBumper
    {
        public const float BUMP_FLYING_LOAD_DISTANCE = 25000f;
        public const float BUMP_LANDED_LOAD_DISTANCE = 5000f;
        private const float BUMP_TIME = 10f;
        public bool workerEnabled = false;
        private bool registered = false;
        private Dictionary<Guid, float> bumpedVessels = new Dictionary<Guid, float>();
        private DMPGame dmpGame;
        private NamedAction updateEvent;
        private VesselRanges defaultRanges = new VesselRanges();

        public VesselRangeBumper(DMPGame dmpGame)
        {
            this.dmpGame = dmpGame;
            this.updateEvent = new NamedAction(Update);
            dmpGame.updateEvent.Add(updateEvent);
        }

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
            if (workerEnabled && !registered)
            {
                RegisterGameHooks();
            }
            if (!workerEnabled && registered)
            {
                UnregisterGameHooks();
            }
            if (workerEnabled)
            {
                UpdateRanges();
            }
        }

        public void ReportVesselUpdate(Guid vesselID)
        {
            bumpedVessels[vesselID] = Client.realtimeSinceStartup;
        }

        public void SetVesselRanges(Vessel v)
        {
            if (!workerEnabled)
            {
                return;
            }
            ReportVesselUpdate(v.id);
            if (v != null)
            {
                DarkLog.Debug("Setting vessel " + v.id + " to bumped ranges");
                bumpedVessels[v.id] = Client.realtimeSinceStartup;
                v.vesselRanges.flying.unpack = BUMP_FLYING_LOAD_DISTANCE;
                v.vesselRanges.flying.load = BUMP_FLYING_LOAD_DISTANCE + 1000f;
                v.vesselRanges.flying.pack = BUMP_FLYING_LOAD_DISTANCE + 2000f;
                v.vesselRanges.flying.unload = BUMP_FLYING_LOAD_DISTANCE + 3000f;
                v.vesselRanges.landed.unpack = BUMP_LANDED_LOAD_DISTANCE;
                v.vesselRanges.landed.load = BUMP_LANDED_LOAD_DISTANCE + 1000f;
                v.vesselRanges.landed.pack = BUMP_LANDED_LOAD_DISTANCE + 2000f;
                v.vesselRanges.landed.unload = BUMP_LANDED_LOAD_DISTANCE + 3000f;
            }
        }

        List<Guid> removeList = new List<Guid>();
        private void UpdateRanges()
        {
            foreach (KeyValuePair<Guid, float> kvp in bumpedVessels)
            {
                if (Client.realtimeSinceStartup > kvp.Value + BUMP_TIME)
                {
                    removeList.Add(kvp.Key);
                }
            }
            foreach (Guid removeID in removeList)
            {
                bumpedVessels.Remove(removeID);
                Vessel v = FlightGlobals.fetch.vessels.FindLast(testVessel => testVessel.id == removeID);
                if (v != null)
                {
                    DarkLog.Debug("Setting vessel " + removeID + " to default ranges");
                    v.vesselRanges.flying.load = defaultRanges.flying.load;
                    v.vesselRanges.flying.pack = defaultRanges.flying.pack;
                    v.vesselRanges.flying.unload = defaultRanges.flying.unload;
                    v.vesselRanges.flying.unpack = defaultRanges.flying.unpack;
                    v.vesselRanges.landed.load = defaultRanges.landed.load;
                    v.vesselRanges.landed.pack = defaultRanges.landed.pack;
                    v.vesselRanges.landed.unload = defaultRanges.landed.unload;
                    v.vesselRanges.landed.unpack = defaultRanges.landed.unpack;
                }
            }
            removeList.Clear();
        }

        private void ReleaseRanges(GameScenes data)
        {
            bumpedVessels.Clear();
        }

        private void RegisterGameHooks()
        {
            registered = true;
            GameEvents.onGameSceneLoadRequested.Add(ReleaseRanges);
        }

        private void UnregisterGameHooks()
        {
            registered = false;
            GameEvents.onGameSceneLoadRequested.Remove(ReleaseRanges);
        }

        public void Stop()
        {
            dmpGame.updateEvent.Remove(updateEvent);
        }
    }
}
