using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class PlayerStatusWorker
    {
        public bool enabled;
        private Client parent;
        private Queue<PlayerStatus> addStatusQueue;
        private Queue<string> removeStatusQueue;
        //Read from PlayerStatusWindow.
        public PlayerStatus myPlayerStatus;
        public PlayerStatus lastPlayerStatus;
        public List<PlayerStatus> playerStatusList;
        private const float PLAYER_STATUS_CHECK_INTERVAL = .2f;
        private const float PLAYER_STATUS_SEND_THROTTLE = 1f;
        private float lastPlayerStatusSend;
        private float lastPlayerStatusCheck;

        public PlayerStatusWorker(Client parent)
        {
            this.parent = parent;
            Reset();
        }

        public void Update()
        {
            if ((UnityEngine.Time.realtimeSinceStartup - lastPlayerStatusCheck) > PLAYER_STATUS_CHECK_INTERVAL)
            {
                lastPlayerStatusCheck = UnityEngine.Time.realtimeSinceStartup;
                myPlayerStatus.vesselText = "";
                myPlayerStatus.statusText = "";
                if (HighLogic.LoadedSceneIsFlight)
                {
                    //Send vessel+status update
                    if (FlightGlobals.ActiveVessel != null)
                    {
                        if (!parent.vesselWorker.isSpectating)
                        {
                            myPlayerStatus.vesselText = FlightGlobals.ActiveVessel.vesselName;
                            string bodyName = FlightGlobals.ActiveVessel.mainBody.bodyName;
                            switch (FlightGlobals.ActiveVessel.situation)
                            {
                                case (Vessel.Situations.DOCKED):
                                    myPlayerStatus.statusText = "Docked above " + bodyName;
                                    break;
                                case (Vessel.Situations.ESCAPING):
                                    if (FlightGlobals.ActiveVessel.orbit.timeToPe > 0)
                                    {
                                        myPlayerStatus.statusText = "Escaping " + bodyName;
                                    }
                                    else
                                    {
                                        myPlayerStatus.statusText = "Encountering " + bodyName;
                                    }
                                    break;
                                case (Vessel.Situations.FLYING):
                                    myPlayerStatus.statusText = "Flying above " + bodyName;
                                    break;
                                case (Vessel.Situations.LANDED):
                                    myPlayerStatus.statusText = "Landed on " + bodyName;
                                    break;
                                case (Vessel.Situations.ORBITING):
                                    myPlayerStatus.statusText = "Orbiting " + bodyName;
                                    break;
                                case (Vessel.Situations.PRELAUNCH):
                                    myPlayerStatus.statusText = "Launching from " + bodyName;
                                    break;
                                case (Vessel.Situations.SPLASHED):
                                    myPlayerStatus.statusText = "Splashed on " + bodyName;
                                    break;
                                case (Vessel.Situations.SUB_ORBITAL):
                                    if (FlightGlobals.ActiveVessel.orbit.timeToPe > 0)
                                    {
                                        myPlayerStatus.statusText = "Ascending from " + bodyName;
                                    }
                                    else
                                    {
                                        myPlayerStatus.statusText = "Descending to " + bodyName;
                                    }
                                    break;
                            }
                        }
                        else
                        {
                            if (parent.vesselWorker.inUse.ContainsKey(FlightGlobals.ActiveVessel.id.ToString())) {
                                myPlayerStatus.statusText = "Spectating " + parent.vesselWorker.inUse[FlightGlobals.ActiveVessel.id.ToString()];
                            }
                            else
                            {
                                myPlayerStatus.statusText = "Spectating Unknown";
                            }
                        }
                    }
                    else
                    {
                        myPlayerStatus.statusText = "Loading";
                    }
                }
                else
                {
                    //Send status update
                    switch (HighLogic.LoadedScene)
                    {
                        case (GameScenes.EDITOR):
                            myPlayerStatus.statusText = "Building in VAB";
                            break;
                        case (GameScenes.SPACECENTER):
                            myPlayerStatus.statusText = "At Space Center";
                            break;
                        case (GameScenes.SPH):
                            myPlayerStatus.statusText = "Building in SPH";
                            break;
                        case (GameScenes.TRACKSTATION):
                            myPlayerStatus.statusText = "At Tracking Station";
                            break;
                        case (GameScenes.LOADING):
                            myPlayerStatus.statusText = "Loading";
                            break;

                    }
                }
            }

            bool statusDifferent = false;
            statusDifferent = statusDifferent || (myPlayerStatus.vesselText != lastPlayerStatus.vesselText);
            statusDifferent = statusDifferent || (myPlayerStatus.statusText != lastPlayerStatus.statusText);
            if (statusDifferent && ((UnityEngine.Time.realtimeSinceStartup - lastPlayerStatusSend) > PLAYER_STATUS_SEND_THROTTLE))
            {
                lastPlayerStatusSend = UnityEngine.Time.realtimeSinceStartup;
                lastPlayerStatus.vesselText = myPlayerStatus.vesselText;
                lastPlayerStatus.statusText = myPlayerStatus.statusText;
                parent.networkWorker.SendPlayerStatus(myPlayerStatus);
            }

            while (addStatusQueue.Count > 0)
            {
                PlayerStatus newStatusEntry = addStatusQueue.Dequeue();
                bool found = false;
                foreach (PlayerStatus playerStatusEntry in playerStatusList)
                {
                    if (playerStatusEntry.playerName == newStatusEntry.playerName)
                    {
                        found = true;
                        playerStatusEntry.vesselText = newStatusEntry.vesselText;
                        playerStatusEntry.statusText = newStatusEntry.statusText;
                    }
                }
                if (!found)
                {
                    playerStatusList.Add(newStatusEntry);
                    DarkLog.Debug("Added " + newStatusEntry.playerName + " to status list");
                }
            }

            while (removeStatusQueue.Count > 0)
            {
                string removeStatusString = removeStatusQueue.Dequeue();
                PlayerStatus removeStatus = null;
                foreach (PlayerStatus currentStatus in playerStatusList)
                {
                    if (currentStatus.playerName == removeStatusString)
                    {
                        removeStatus = currentStatus;
                    }
                }
                if (removeStatus != null)
                {
                    playerStatusList.Remove(removeStatus);
                    DarkLog.Debug("Removed " + removeStatusString + " from status list");
                }
                else
                {
                    DarkLog.Debug("Cannot remove non-existant player " + removeStatusString);
                }
            }
        }

        public void AddPlayerStatus(PlayerStatus playerStatus)
        {
            addStatusQueue.Enqueue(playerStatus);
        }

        public void RemovePlayerStatus(string playerName)
        {
            removeStatusQueue.Enqueue(playerName);
        }

        public void Reset()
        {
            lastPlayerStatusSend = 0;
            addStatusQueue = new Queue<PlayerStatus>();
            removeStatusQueue = new Queue<string>();
            myPlayerStatus = new PlayerStatus();
            lastPlayerStatus = new PlayerStatus();
            playerStatusList = new List<PlayerStatus>();
            myPlayerStatus.playerName = parent.settings.playerName;
        }
    }
}

