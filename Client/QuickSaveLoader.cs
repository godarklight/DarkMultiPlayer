using System;
using DarkMultiPlayerCommon;
using MessageStream;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class QuickSaveLoader
    {
        Client parent;
        ConfigNode savedVessel;
        Subspace savedSubspace;

        public QuickSaveLoader(Client parent) {
            this.parent = parent;
            Reset();
        }

        public void Save() {
            if ((HighLogic.LoadedScene == GameScenes.FLIGHT) && (FlightGlobals.fetch.activeVessel != null))
            {
                if (FlightGlobals.fetch.activeVessel.loaded && !FlightGlobals.fetch.activeVessel.packed)
                {
                    if (FlightGlobals.fetch.activeVessel.situation != Vessel.Situations.FLYING)
                    {
                        savedVessel = new ConfigNode();
                        ProtoVessel tempVessel = new ProtoVessel(FlightGlobals.fetch.activeVessel);
                        tempVessel.Save(savedVessel);
                        savedSubspace = new Subspace();
                        savedSubspace.planetTime = Planetarium.GetUniversalTime();
                        savedSubspace.serverClock = parent.timeSyncer.GetServerClock();
                        savedSubspace.subspaceSpeed = 1f;
                        ScreenMessages.PostScreenMessage("Quicksaved!", 3f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    else
                    {
                        ScreenMessages.PostScreenMessage("Cannot quicksave - Active vessel is in flight!", 3f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Cannot quicksave - Active vessel is not loaded!", 3f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            else
            {
                ScreenMessages.PostScreenMessage("Cannot quicksave - Not in flight!", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        public void Load() {
            if (savedVessel != null && savedSubspace != null)
            {
                parent.timeSyncer.UnlockSubspace();
                long serverClock = parent.timeSyncer.GetServerClock();
                int newSubspace = parent.timeSyncer.LockNewSubspace(serverClock, savedSubspace.planetTime, savedSubspace.subspaceSpeed);
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)WarpMessageType.NEW_SUBSPACE);
                    mw.Write<string>(parent.settings.playerName);
                    mw.Write<int>(newSubspace);
                    mw.Write<long>(serverClock);
                    mw.Write<double>(savedSubspace.planetTime);
                    mw.Write<float>(savedSubspace.subspaceSpeed);
                    parent.networkWorker.SendWarpMessage(mw.GetMessageBytes());
                }
                parent.timeSyncer.LockSubspace(newSubspace);
                parent.vesselWorker.LoadVessel(savedVessel);
                ScreenMessages.PostScreenMessage("Quickloaded!", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
            else
            {
                ScreenMessages.PostScreenMessage("No current quicksave to load from!", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        public void Update() {
            if (Input.GetKey(KeyCode.F5))
            {
                Save();
            }
            if (Input.GetKey(KeyCode.F9))
            {
                Load();
            }
        }

        public void Reset() {
            savedVessel = null;
            savedSubspace = null;
        }
    }
}

