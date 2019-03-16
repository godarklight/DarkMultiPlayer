using System;
using System.Collections.Generic;
using DarkMultiPlayer;
namespace DarkMultiPlayer
{
    public class VesselInterFrameUpdater
    {
        private PosistionStatistics posistionStatistics;
        private LockSystem lockSystem;
        private Settings dmpSettings;
        private VesselRecorder vesselRecorder;
        private Dictionary<Guid, VesselUpdate> nextVesselUpdates = new Dictionary<Guid, VesselUpdate>();
        private Dictionary<Guid, VesselUpdate> currentVesselUpdates = new Dictionary<Guid, VesselUpdate>();
        private Dictionary<Guid, VesselUpdate> previousVesselUpdates = new Dictionary<Guid, VesselUpdate>();

        public VesselInterFrameUpdater(LockSystem lockSystem, PosistionStatistics posistionStatistics, Settings dmpSettings)
        {
            this.lockSystem = lockSystem;
            this.posistionStatistics = posistionStatistics;
            this.dmpSettings = dmpSettings;
        }

        /*
        public void SetVesselRecoder(VesselRecorder vesselRecorder)
        {
            this.vesselRecorder = vesselRecorder;
        }
        */

        public void SetVesselUpdate(Guid vesselID, VesselUpdate vesselUpdate, VesselUpdate previousUpdate, VesselUpdate nextUpdate)
        {
            nextVesselUpdates[vesselID] = nextUpdate;
            currentVesselUpdates[vesselID] = vesselUpdate;
            previousVesselUpdates[vesselID] = previousUpdate;
        }

        public void SetNextUpdate(Guid vesselID, VesselUpdate nextUpdate)
        {
            if (nextVesselUpdates.ContainsKey(vesselID))
            {
                nextVesselUpdates[vesselID] = nextUpdate;
            }
        }

        public void Update()
        {
            foreach (Guid vesselID in currentVesselUpdates.Keys)
            {
                //Not sure if this can happen, but we don't want to apply updates to vessels we are supposed to be updating.
                if (lockSystem.LockIsOurs("update-" + vesselID))
                {
                    continue;
                }
                Vessel updateVessel = FlightGlobals.fetch.vessels.Find(v => v.id == vesselID);
                if (updateVessel == null)
                {
                    continue;
                }
                if (!updateVessel.packed && !dmpSettings.interframeEnabled)
                {
                    return;
                }
                VesselUpdate vu = currentVesselUpdates[vesselID];
                VesselUpdate pu = null;
                if (previousVesselUpdates.ContainsKey(vesselID))
                {
                    pu = previousVesselUpdates[vesselID];
                }
                VesselUpdate nu = null;
                if (nextVesselUpdates.ContainsKey(vesselID))
                {
                    nu = nextVesselUpdates[vesselID];
                }
                //Apply updates for up to 5 seconds
                double timeDiff = Planetarium.GetUniversalTime() - vu.planetTime;
                if (timeDiff < 5f && timeDiff > -5f)
                {
                    vu.Apply(posistionStatistics, null, pu, nu, dmpSettings);
                }
            }
            //vesselRecorder.DisplayUpdateVesselOffset();
        }
    }
}