using System.Collections.Generic;

namespace DarkMultiPlayer
{
    /// <summary>
    /// This system attempts to fix a reputation related bug. Now, when a kerbal dies, only players that has been in control of a ship that contained that kerbal will lose reputation.
    /// Reputation can also only be lost once per kerbal, before it could be lost many times for vessels that the client did not control.
    /// </summary>
    public class ReputationSystem
    {
        private readonly HashSet<string> usedKerbals = new HashSet<string>();
        private readonly HashSet<string> deadKerbals = new HashSet<string>();
        private static ReputationSystem instance;

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (instance != null)
                {
                    instance.DeregisterEvents();
                }

                instance = new ReputationSystem();
                instance.RegisterEvents();
            }
        }

        private void RegisterEvents()
        {
            GameEvents.OnReputationChanged.Add(OnReputationChanged);
            GameEvents.onCrewKilled.Add(OnCrewKilled);
            GameEvents.onVesselChange.Add(OnVesselChanged);
            GameEvents.OnVesselRecoveryRequested.Add(OnVesselRecovered);
        }

        private void DeregisterEvents()
        {
            GameEvents.OnReputationChanged.Remove(OnReputationChanged);
            GameEvents.onCrewKilled.Remove(OnCrewKilled);
            GameEvents.onVesselChange.Remove(OnVesselChanged);
            GameEvents.OnVesselRecoveryRequested.Remove(OnVesselRecovered);
        }

        private void OnVesselRecovered(Vessel vessel)
        {
            // When a vessel is recovered ensure that all kerbals are removed from the used/dead lists.
            foreach (var crewMember in vessel.GetVesselCrew())
            {
                usedKerbals.Remove(crewMember.name);
                deadKerbals.Remove(crewMember.name);
            }
        }

        private void OnVesselChanged(Vessel vessel)
        {
            if (FlightGlobals.ActiveVessel != vessel)
                return;

            // Keep track of which kerbals has been used, to know if a penalty is appropriate or not when a kerbal dies.
            foreach (var crewMember in vessel.GetVesselCrew())
            {
                usedKerbals.Add(crewMember.name);
                deadKerbals.Remove(crewMember.name);
            }
        }

        private void OnCrewKilled(EventReport data)
        {
            var kerbalName = data.sender;

            // If this kerbal was used by the player at any point, add the reputation penalty.
            if (usedKerbals.Contains(kerbalName) && !deadKerbals.Contains(kerbalName))
            {
                Reputation.Instance.AddReputation(GameVariables.Instance.reputationKerbalDeath * HighLogic.CurrentGame.Parameters.Career.RepLossMultiplier, TransactionReasons.Any);
                deadKerbals.Add(kerbalName);
            }
        }

        private void OnReputationChanged(float reputation, TransactionReasons reason)
        {
            // We only care about vessel loss (= crew killed).
            if (reason != TransactionReasons.VesselLoss)
                return;

            // Reimburse reputation lost when crew is killed.
            Reputation.Instance.AddReputation(-GameVariables.Instance.reputationKerbalDeath * HighLogic.CurrentGame.Parameters.Career.RepLossMultiplier, TransactionReasons.Any);
        }
    }
}
