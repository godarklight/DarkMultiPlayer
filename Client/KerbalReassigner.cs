using System;
using System.Collections.Generic;

namespace DarkMultiPlayer
{
    public class KerbalReassigner
    {
        private static KerbalReassigner singleton;
        private bool registered = false;
        private Dictionary<Guid, List<string>> vesselToKerbal = new Dictionary<Guid, List<string>>();
        private Dictionary<string, Guid> kerbalToVessel = new Dictionary<string, Guid>();

        public static KerbalReassigner fetch
        {
            get
            {
                return singleton;
            }
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
                GameEvents.onFlightReady.Remove(this.OnFlightReady);
            }
        }

        private void OnVesselCreate(Vessel vessel)
        {
            //Kerbals are put in the vessel *after* OnVesselCreate. Thanks squad!.
            if (vesselToKerbal.ContainsKey(vessel.id))
            {
                OnVesselDestroyed(vessel);
            }
            if (vessel.GetCrewCount() > 0)
            {
                vesselToKerbal.Add(vessel.id, new List<string>());
                foreach (ProtoCrewMember pcm in vessel.GetVesselCrew())
                {
                    vesselToKerbal[vessel.id].Add(pcm.name);
                    if (kerbalToVessel.ContainsKey(pcm.name) && kerbalToVessel[pcm.name] != vessel.id)
                    {
                        DarkLog.Debug("Warning, kerbal double take on " + vessel.id + " ( " + vessel.name + " )");
                    }
                    kerbalToVessel[pcm.name] = vessel.id;
                    DarkLog.Debug("OVC " + pcm.name + " belongs to " + vessel.id);
                }
            }
        }

        private void OnVesselWasModified(Vessel vessel)
        {
            OnVesselDestroyed(vessel);
            OnVesselCreate(vessel);
        }

        private void OnVesselDestroyed(Vessel vessel)
        {
            if (vesselToKerbal.ContainsKey(vessel.id))
            {
                foreach (string kerbalName in vesselToKerbal[vessel.id])
                {
                    kerbalToVessel.Remove(kerbalName);
                }
                vesselToKerbal.Remove(vessel.id);
            }
        }

        //Squad workaround - kerbals are assigned after vessel creation for new vessels.
        private void OnFlightReady()
        {
            if (!vesselToKerbal.ContainsKey(FlightGlobals.fetch.activeVessel.id))
            {
                OnVesselCreate(FlightGlobals.fetch.activeVessel);
            }
        }

        public void DodgeKerbals(ConfigNode inputNode, Guid protovesselID)
        {
            List<string> takenKerbals = new List<string>();
            foreach (ConfigNode partNode in inputNode.GetNodes("PART"))
            {
                int crewIndex = 0;
                foreach (string currentKerbalName in partNode.GetValues("crew"))
                {
                    if (kerbalToVessel.ContainsKey(currentKerbalName) ? kerbalToVessel[currentKerbalName] != protovesselID : false)
                    {
                        ProtoCrewMember newKerbal = null;
                        ProtoCrewMember.Gender newKerbalGender = ProtoCrewMember.Gender.Male;
                        string newExperienceTrait = null;
                        if (HighLogic.CurrentGame.CrewRoster.Exists(currentKerbalName))
                        {
                            ProtoCrewMember oldKerbal = HighLogic.CurrentGame.CrewRoster[currentKerbalName];
                            newKerbalGender = oldKerbal.gender;
                            newExperienceTrait = oldKerbal.experienceTrait.TypeName;
                        }
                        foreach (ProtoCrewMember possibleKerbal in HighLogic.CurrentGame.CrewRoster.Crew)
                        {
                            bool kerbalOk = true;
                            if (kerbalOk && kerbalToVessel.ContainsKey(possibleKerbal.name) && (takenKerbals.Contains(possibleKerbal.name) || kerbalToVessel[possibleKerbal.name] != protovesselID))
                            {
                                kerbalOk = false;
                            }
                            if (kerbalOk && possibleKerbal.gender != newKerbalGender)
                            {
                                kerbalOk = false;
                            }
                            if (kerbalOk && newExperienceTrait != null && newExperienceTrait != possibleKerbal.experienceTrait.TypeName)
                            {
                                kerbalOk = false;
                            }
                            if (kerbalOk)
                            {
                                newKerbal = possibleKerbal;
                                break;
                            }
                        }
                        while (newKerbal == null)
                        {
                            bool kerbalOk = true;
                            ProtoCrewMember possibleKerbal = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Crew);
                            if (possibleKerbal.gender != newKerbalGender)
                            {
                                kerbalOk = false;
                            }
                            if (newExperienceTrait != null && newExperienceTrait != possibleKerbal.experienceTrait.TypeName)
                            {
                                kerbalOk = false;
                            }
                            if (kerbalOk)
                            {
                                newKerbal = possibleKerbal;
                            }
                        }
                        partNode.SetValue("crew", newKerbal.name, crewIndex);
                        newKerbal.seatIdx = crewIndex;
                        newKerbal.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                        takenKerbals.Add(newKerbal.name);
                    }
                    else
                    {
                        takenKerbals.Add(currentKerbalName);
                    }
                    crewIndex++;
                }
            }
            vesselToKerbal[protovesselID] = takenKerbals;
            foreach (string name in takenKerbals)
            {
                kerbalToVessel[name] = protovesselID;
            }
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    if (singleton.registered)
                    {
                        singleton.UnregisterGameHooks();
                    }
                }
                singleton = new KerbalReassigner();
            }
        }
    }
}

