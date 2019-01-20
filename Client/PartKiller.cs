using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class PartKiller
    {
        //Bidictionary
        private Dictionary<Vessel, List<uint>> vesselToPart = new Dictionary<Vessel, List<uint>>();
        private Dictionary<uint, List<Vessel>> partToVessel = new Dictionary<uint, List<Vessel>>();
        private Guid loadGuid = Guid.Empty;
        private bool registered = false;
        //Services
        private LockSystem lockSystem;

        public PartKiller(LockSystem lockSystem)
        {
            this.lockSystem = lockSystem;
        }

        public void RegisterGameHooks()
        {
            if (!registered)
            {
                registered = true;
                GameEvents.onVesselCreate.Add(this.OnVesselCreate);
                GameEvents.onVesselWasModified.Add(this.OnVesselWasModified);
                GameEvents.onVesselDestroy.Add(this.OnVesselDestroyed);
                GameEvents.onFlightReady.Add(this.OnFlightReady);
            }
        }

        private void UnregisterGameHooks()
        {
            if (registered)
            {
                registered = false;
                GameEvents.onVesselCreate.Remove(this.OnVesselCreate);
                GameEvents.onVesselWasModified.Remove(this.OnVesselWasModified);
                GameEvents.onVesselDestroy.Remove(this.OnVesselDestroyed);
                GameEvents.onFlightReady.Add(this.OnFlightReady);
            }
        }

        private void OnVesselCreate(Vessel vessel)
        {
            ProtoVessel pv = vessel.BackupVessel();
            List<uint> vesselParts = new List<uint>();
            bool killShip = false;
            bool vesselOk = false;
            foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
            {
                if (pps.flightID == 0)
                {
                    loadGuid = vessel.id;
                    //When you spawn a new vessel in all the part ID's are 0 until OnFlightReady.
                    return;
                }
                vesselParts.Add(pps.flightID);
                if (partToVessel.ContainsKey(pps.flightID))
                {
                    killShip = true;
                    foreach (Vessel otherVessel in partToVessel[pps.flightID])
                    {
                        if (otherVessel.id == vessel.id)
                        {
                            vesselOk = true;
                        }
                        //Either of the locks are ours or neither of the locks exist
                        if (lockSystem.LockIsOurs("control-" + otherVessel.id) || lockSystem.LockIsOurs("update-" + otherVessel.id) || (!lockSystem.LockExists("control-" + otherVessel.id) && !lockSystem.LockExists("update-" + otherVessel.id)))
                        {
                            vesselOk = true;
                        }
                    }
                }
            }
            if (killShip && !vesselOk)
            {
                DarkLog.Debug("PartKiller: Destroying vessel fragment");
                vessel.Die();
            }
            else
            {
                vesselToPart.Add(vessel, vesselParts);
                foreach (uint partID in vesselParts)
                {
                    if (!partToVessel.ContainsKey(partID))
                    {
                        partToVessel.Add(partID, new List<Vessel>());
                    }
                    partToVessel[partID].Add(vessel);
                }
            }

        }

        private void OnVesselWasModified(Vessel vessel)
        {
            if (!vesselToPart.ContainsKey(vessel))
            {
                //Killed as a fragment.
                return;
            }
            List<uint> addList = new List<uint>();
            List<uint> removeList = new List<uint>(vesselToPart[vessel]);
            ProtoVessel pv = vessel.BackupVessel();
            foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
            {
                if (removeList.Contains(pps.flightID))
                {
                    removeList.Remove(pps.flightID);
                }
                else
                {
                    addList.Add(pps.flightID);
                }
            }
            foreach (uint partID in addList)
            {
                if (!partToVessel.ContainsKey(partID))
                {
                    partToVessel.Add(partID, new List<Vessel>());
                }
                partToVessel[partID].Add(vessel);
                vesselToPart[vessel].Add(partID);
            }
            foreach (uint partID in removeList)
            {
                vesselToPart[vessel].Remove(partID);
                partToVessel[partID].Remove(vessel);
                if (partToVessel[partID].Count == 0)
                {
                    partToVessel.Remove(partID);
                }
            }
        }

        private void OnVesselDestroyed(Vessel vessel)
        {
            ForgetVessel(vessel);
        }

        public void ForgetVessel(Vessel vessel)
        {
            if (!vesselToPart.ContainsKey(vessel))
            {
                //Killed as a fragment.
                return;
            }
            foreach (uint partID in vesselToPart[vessel])
            {
                partToVessel[partID].Remove(vessel);
                if (partToVessel[partID].Count == 0)
                {
                    partToVessel.Remove(partID);
                }
            }
            vesselToPart.Remove(vessel);
        }

        private void OnFlightReady()
        {
            if (FlightGlobals.fetch.activeVessel.id == loadGuid)
            {
                loadGuid = Guid.Empty;
                List<uint> vesselParts = new List<uint>();
                ProtoVessel pv = FlightGlobals.fetch.activeVessel.BackupVessel();
                foreach (ProtoPartSnapshot pps in pv.protoPartSnapshots)
                {
                    vesselParts.Add(pps.flightID);
                }
                vesselToPart.Add(FlightGlobals.fetch.activeVessel, vesselParts);
                foreach (uint partID in vesselParts)
                {
                    if (!partToVessel.ContainsKey(partID))
                    {
                        partToVessel.Add(partID, new List<Vessel>());
                    }
                    partToVessel[partID].Add(FlightGlobals.fetch.activeVessel);
                }
            }
        }

        public void Stop()
        {
            if (registered)
            {
                UnregisterGameHooks();
            }
        }
    }
}

