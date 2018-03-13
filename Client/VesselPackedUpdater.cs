﻿using System;
using System.Collections.Generic;
using DarkMultiPlayer;
namespace DarkMultiPlayer
{
    public class VesselPackedUpdater
    {
        private PosistionStatistics posistionStatistics;
        private LockSystem lockSystem;
        private Dictionary<Guid, VesselUpdate> currentVesselUpdates = new Dictionary<Guid, VesselUpdate>();
        private Dictionary<Guid, VesselUpdate> previousVesselUpdates = new Dictionary<Guid, VesselUpdate>();

        public VesselPackedUpdater (LockSystem lockSystem, PosistionStatistics posistionStatistics)
        {
            this.lockSystem = lockSystem;
            this.posistionStatistics = posistionStatistics;
        }

        public void SetVesselUpdate(Guid vesselID, VesselUpdate vesselUpdate, VesselUpdate previousUpdate)
        {
            currentVesselUpdates[vesselID] = vesselUpdate;
            previousVesselUpdates[vesselID] = previousUpdate;
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
                VesselUpdate vu = currentVesselUpdates[vesselID];
                VesselUpdate pu = null;
                if (previousVesselUpdates.ContainsKey(vesselID))
                {
                    pu = previousVesselUpdates[vesselID];
                }
                //Apply updates for up to 5 seconds
                double timeDiff = Planetarium.GetUniversalTime() - vu.planetTime;
                if (timeDiff < 5f && timeDiff > -5f)
                {
                    vu.Apply(posistionStatistics, null, pu);
                }
            }
        }
    }
}