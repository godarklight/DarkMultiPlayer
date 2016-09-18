using System;
using System.Collections.Generic;
using System.Reflection;

namespace DarkMultiPlayer
{
    public class KerbalReassigner
    {
        private static KerbalReassigner singleton;
        private bool registered = false;
        private Dictionary<Guid, List<string>> vesselToKerbal = new Dictionary<Guid, List<string>>();
        private Dictionary<string, Guid> kerbalToVessel = new Dictionary<string, Guid>();

        private delegate bool AddCrewMemberToRosterDelegate(ProtoCrewMember pcm);

        private AddCrewMemberToRosterDelegate AddCrewMemberToRoster;

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
                        ProtoCrewMember.Gender newKerbalGender = GetKerbalGender(currentKerbalName);
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
                        CreateKerbalIfMissing(currentKerbalName, protovesselID);
                        HighLogic.CurrentGame.CrewRoster[currentKerbalName].rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                        HighLogic.CurrentGame.CrewRoster[currentKerbalName].seatIdx = crewIndex;
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

        public void CreateKerbalIfMissing(string kerbalName, Guid vesselID)
        {
            if (!HighLogic.CurrentGame.CrewRoster.Exists(kerbalName))
            {
                if (AddCrewMemberToRoster == null)
                {
                    MethodInfo addMemberToCrewRosterMethod = typeof(KerbalRoster).GetMethod("AddCrewMember", BindingFlags.Public | BindingFlags.Instance);
                    AddCrewMemberToRoster = (AddCrewMemberToRosterDelegate)Delegate.CreateDelegate(typeof(AddCrewMemberToRosterDelegate), HighLogic.CurrentGame.CrewRoster, addMemberToCrewRosterMethod);
                    if (AddCrewMemberToRoster == null)
                    {
                        throw new Exception("Failed to load AddCrewMember delegate!");
                    }
                }
                ProtoCrewMember pcm = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Crew);
                pcm.ChangeName(kerbalName);
                pcm.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                AddCrewMemberToRoster(pcm);
                DarkLog.Debug("Created kerbal " + pcm.name + " for vessel " + vesselID + ", Kerbal was missing");
            }
        }

        //Better not use a bool for this and enforce the gender binary on xir!
        public static ProtoCrewMember.Gender GetKerbalGender(string kerbalName)
        {
            string trimmedName = kerbalName;
            if (kerbalName.Contains(" Kerman"))
            {
                trimmedName = kerbalName.Substring(0, kerbalName.IndexOf(" Kerman"));
                DarkLog.Debug("Trimming to '" + trimmedName + "'");
            }
            try
            {
                string[] femaleNames = (string[])typeof(CrewGenerator).GetField("\u0004", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                string[] femaleNamesPrefix = (string[])typeof(CrewGenerator).GetField("\u0005", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                string[] femaleNamesPostfix = (string[])typeof(CrewGenerator).GetField("\u0006", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                //Not part of the generator
                if (trimmedName == "Valentina")
                {
                    return ProtoCrewMember.Gender.Female;
                }
                foreach (string name in femaleNames)
                {
                    if (name == trimmedName)
                    {
                        return ProtoCrewMember.Gender.Female;
                    }
                }
                foreach (string prefixName in femaleNamesPrefix)
                {
                    if (trimmedName.StartsWith(prefixName))
                    {
                        foreach (string postfixName in femaleNamesPostfix)
                        {
                            if (trimmedName == prefixName + postfixName)
                            {
                                return ProtoCrewMember.Gender.Female;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("DarkMultiPlayer name identifier exception: " + e);
            }
            return ProtoCrewMember.Gender.Male;
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

