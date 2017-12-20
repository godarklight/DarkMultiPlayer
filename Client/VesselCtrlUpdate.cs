using System;
using System.Collections.Generic;
namespace DarkMultiPlayer
{
    public class VesselCtrlUpdate
    {
        private double planetTimeValid;
        private FlightCtrlState newFcs;
        private Vessel vessel;
        private Dictionary<Guid, VesselCtrlUpdate> ctrlUpdates;

        public VesselCtrlUpdate(Vessel vessel, Dictionary<Guid, VesselCtrlUpdate> ctrlUpdates, double planetTimeValid, FlightCtrlState oldFcs)
        {
            this.vessel = vessel;
            this.ctrlUpdates = ctrlUpdates;
            this.planetTimeValid = planetTimeValid;
            newFcs = new FlightCtrlState();
            newFcs.CopyFrom(oldFcs);
        }

        public void UpdateControls(FlightCtrlState vesselFcs)
        {
            if (Planetarium.GetUniversalTime() < planetTimeValid)
            {
                vesselFcs.CopyFrom(newFcs);
            }
            else
            {
                vessel.OnFlyByWire -= UpdateControls;
                ctrlUpdates.Remove(vessel.id);
            }
        }
    }
}
