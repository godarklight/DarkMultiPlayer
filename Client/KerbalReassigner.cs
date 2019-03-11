using System;
using System.Collections.Generic;
using System.Reflection;

namespace DarkMultiPlayer
{
    public class KerbalReassigner
    {
        private bool registered = false;
        private static bool inited = false;
        private static bool broken = false;
        private static HashSet<string> femaleNames = new HashSet<string>();
        private static HashSet<string> femaleNamesSpecial = new HashSet<string>();
        private Dictionary<Guid, List<string>> vesselToKerbal = new Dictionary<Guid, List<string>>();
        private Dictionary<string, Guid> kerbalToVessel = new Dictionary<string, Guid>();

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
                            if (kerbalOk && newExperienceTrait != null && possibleKerbal.experienceTrait != null && newExperienceTrait != possibleKerbal.experienceTrait.TypeName)
                            {
                                kerbalOk = false;
                            }
                            if (kerbalOk)
                            {
                                newKerbal = possibleKerbal;
                                break;
                            }
                        }
                        int kerbalTries = 0;
                        while (newKerbal == null)
                        {
                            bool kerbalOk = true;
                            ProtoCrewMember.KerbalType kerbalType = ProtoCrewMember.KerbalType.Crew;
                            if (newExperienceTrait == "Tourist")
                            {
                                kerbalType = ProtoCrewMember.KerbalType.Tourist;
                            }
                            if (newExperienceTrait == "Unowned")
                            {
                                kerbalType = ProtoCrewMember.KerbalType.Unowned;
                            }
                            ProtoCrewMember possibleKerbal = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(kerbalType);
                            if (kerbalTries < 200 && possibleKerbal.gender != newKerbalGender)
                            {
                                kerbalOk = false;
                            }
                            if (kerbalTries < 100 && newExperienceTrait != null && newExperienceTrait != possibleKerbal.experienceTrait.TypeName)
                            {
                                kerbalOk = false;
                            }
                            if (kerbalOk)
                            {
                                newKerbal = possibleKerbal;
                            }
                            kerbalTries++;
                        }
                        DarkLog.Debug("Generated dodged kerbal with " + kerbalTries + " tries");
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
                ProtoCrewMember pcm = CrewGenerator.RandomCrewMemberPrototype(ProtoCrewMember.KerbalType.Crew);
                HighLogic.CurrentGame.CrewRoster.AddCrewMember(pcm);
                pcm.ChangeName(kerbalName);
                pcm.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                DarkLog.Debug("Created kerbal " + pcm.name + " for vessel " + vesselID + ", Kerbal was missing");
            }
        }

        //Better not use a bool for this and enforce the gender binary on xir!
        public static ProtoCrewMember.Gender GetKerbalGender(string kerbalName)
        {
            if (broken)
            {
                return ProtoCrewMember.Gender.Male;
            }

            if (!inited)
            {
                string[] femaleNamesArray = null;
                string[] femaleNamesSpecialArray = null;
                FieldInfo femaleNamesArrayFI = typeof(CrewGenerator).GetField("kerbalNamesFemale", BindingFlags.NonPublic | BindingFlags.Static);
                FieldInfo femaleNamesSpecialFI = typeof(CrewGenerator).GetField("specialNamesFemale", BindingFlags.NonPublic | BindingFlags.Static);
                if (femaleNamesArrayFI != null && femaleNamesSpecialFI != null)
                {
                    femaleNamesArray = (string[])femaleNamesArrayFI.GetValue(null);
                    femaleNamesSpecialArray = (string[])femaleNamesSpecialFI.GetValue(null);
                }
                inited = true;
                if (femaleNamesArray == null || femaleNamesSpecialArray == null)
                {
                    broken = true;
                    DarkLog.Debug("Kerbal Gender Assigner is BROKEN!");
                    return ProtoCrewMember.Gender.Male;
                }
                else
                {
                    foreach (string name in femaleNamesArray)
                    {
                        femaleNames.Add(name);
                    }

                    foreach (string name in femaleNamesSpecialArray)
                    {
                        femaleNamesSpecial.Add(name);
                    }
                }
            }

            string trimmedName = kerbalName;
            if (kerbalName.Contains(" "))
            {
                trimmedName = kerbalName.Substring(0, kerbalName.LastIndexOf(" ", StringComparison.Ordinal));
            }

            if (trimmedName == "Valentina")
            {
                return ProtoCrewMember.Gender.Female;
            }

            if (femaleNames.Contains(trimmedName))
            {
                return ProtoCrewMember.Gender.Female;
            }

            if (femaleNamesSpecial.Contains(trimmedName))
            {
                return ProtoCrewMember.Gender.Female;
            }

            return ProtoCrewMember.Gender.Male;
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

