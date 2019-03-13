using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class PlayerStatusWorker
    {
        public bool workerEnabled;
        private Queue<PlayerStatus> addStatusQueue = new Queue<PlayerStatus>();
        private Queue<string> removeStatusQueue = new Queue<string>();
        public PlayerStatus myPlayerStatus;
        private PlayerStatus lastPlayerStatus = new PlayerStatus();
        public List<PlayerStatus> playerStatusList = new List<PlayerStatus>();
        private const float PLAYER_STATUS_CHECK_INTERVAL = .2f;
        private const float PLAYER_STATUS_SEND_THROTTLE = 1f;
        private float lastPlayerStatusSend;
        private float lastPlayerStatusCheck;
        //Services
        private DMPGame dmpGame;
        private VesselWorker vesselWorker;
        private LockSystem lockSystem;
        private NetworkWorker networkWorker;

        public PlayerStatusWorker(DMPGame dmpGame, Settings dmpSettings, VesselWorker vesselWorker, LockSystem lockSystem, NetworkWorker networkWorker)
        {
            this.dmpGame = dmpGame;
            this.vesselWorker = vesselWorker;
            this.lockSystem = lockSystem;
            this.networkWorker = networkWorker;
            myPlayerStatus = new PlayerStatus();
            myPlayerStatus.playerName = dmpSettings.playerName;
            myPlayerStatus.statusText = "Syncing";
            dmpGame.updateEvent.Add(Update);
        }

        private void Update()
        {
            if (workerEnabled)
            {
                if ((Client.realtimeSinceStartup - lastPlayerStatusCheck) > PLAYER_STATUS_CHECK_INTERVAL)
                {
                    lastPlayerStatusCheck = Client.realtimeSinceStartup;
                    myPlayerStatus.vesselText = "";
                    myPlayerStatus.statusText = "";
                    if (HighLogic.LoadedSceneIsFlight)
                    {
                        //Send vessel+status update
                        if (FlightGlobals.ActiveVessel != null)
                        {
                            if (!vesselWorker.isSpectating)
                            {
                                myPlayerStatus.vesselText = FlightGlobals.ActiveVessel.vesselName;
                                string bodyName = FlightGlobals.ActiveVessel.mainBody.bodyName;
                                switch (FlightGlobals.ActiveVessel.situation)
                                {
                                    case (Vessel.Situations.DOCKED):
                                        myPlayerStatus.statusText = "Docked above " + bodyName;
                                        break;
                                    case (Vessel.Situations.ESCAPING):
                                        if (FlightGlobals.ActiveVessel.orbit.timeToPe < 0)
                                        {
                                            myPlayerStatus.statusText = "Escaping " + bodyName;
                                        }
                                        else
                                        {
                                            myPlayerStatus.statusText = "Encountering " + bodyName;
                                        }
                                        break;
                                    case (Vessel.Situations.FLYING):
                                        if (!SafetyBubble.isInSafetyBubble(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), FlightGlobals.fetch.activeVessel.mainBody, vesselWorker.safetyBubbleDistance))
                                        {
                                            myPlayerStatus.statusText = "Flying above " + bodyName;
                                        }
                                        else
                                        {
                                            myPlayerStatus.statusText = "Flying in safety bubble";
                                        }
                                        break;
                                    case (Vessel.Situations.LANDED):
                                        if (!SafetyBubble.isInSafetyBubble(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), FlightGlobals.fetch.activeVessel.mainBody, vesselWorker.safetyBubbleDistance))
                                        {
                                            myPlayerStatus.statusText = "Landed on " + bodyName;
                                        }
                                        else
                                        {
                                            myPlayerStatus.statusText = "Landed in safety bubble";
                                        }
                                        break;
                                    case (Vessel.Situations.ORBITING):
                                        myPlayerStatus.statusText = "Orbiting " + bodyName;
                                        break;
                                    case (Vessel.Situations.PRELAUNCH):
                                        if (!SafetyBubble.isInSafetyBubble(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), FlightGlobals.fetch.activeVessel.mainBody, vesselWorker.safetyBubbleDistance))
                                        {
                                            myPlayerStatus.statusText = "Launching from " + bodyName;
                                        }
                                        else
                                        {
                                            myPlayerStatus.statusText = "Launching from safety bubble";
                                        }
                                        break;
                                    case (Vessel.Situations.SPLASHED):
                                        myPlayerStatus.statusText = "Splashed on " + bodyName;
                                        break;
                                    case (Vessel.Situations.SUB_ORBITAL):
                                        if (FlightGlobals.ActiveVessel.verticalSpeed > 0)
                                        {
                                            myPlayerStatus.statusText = "Ascending from " + bodyName;
                                        }
                                        else
                                        {
                                            myPlayerStatus.statusText = "Descending to " + bodyName;
                                        }
                                        break;
                                    default:
                                        break;
                                }
                            }
                            else
                            {
                                if (lockSystem.LockExists("control-" + FlightGlobals.ActiveVessel.id.ToString()))
                                {
                                    if (lockSystem.LockIsOurs("control-" + FlightGlobals.ActiveVessel.id.ToString()))
                                    {
                                        myPlayerStatus.statusText = "Waiting for vessel control";
                                    }
                                    else 
                                    {
                                        myPlayerStatus.statusText = "Spectating " + lockSystem.LockOwner("control-" + FlightGlobals.ActiveVessel.id.ToString());
                                    }
                                }
                                else
                                {
                                    myPlayerStatus.statusText = "Spectating future updates";
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
                                myPlayerStatus.statusText = "Building";
                                if (EditorDriver.editorFacility == EditorFacility.VAB)
                                {
                                    myPlayerStatus.statusText = "Building in VAB";
                                }
                                if (EditorDriver.editorFacility == EditorFacility.SPH)
                                {
                                    myPlayerStatus.statusText = "Building in SPH";
                                }
                                break;
                            case (GameScenes.SPACECENTER):
                                myPlayerStatus.statusText = "At Space Center";
                                break;
                            case (GameScenes.TRACKSTATION):
                                myPlayerStatus.statusText = "At Tracking Station";
                                break;
                            case (GameScenes.LOADING):
                                myPlayerStatus.statusText = "Loading";
                                break;
                            default:
                                break;
                        }
                    }
                }

                bool statusDifferent = false;
                statusDifferent = statusDifferent || (myPlayerStatus.vesselText != lastPlayerStatus.vesselText);
                statusDifferent = statusDifferent || (myPlayerStatus.statusText != lastPlayerStatus.statusText);
                if (statusDifferent && ((Client.realtimeSinceStartup - lastPlayerStatusSend) > PLAYER_STATUS_SEND_THROTTLE))
                {
                    lastPlayerStatusSend = Client.realtimeSinceStartup;
                    lastPlayerStatus.vesselText = myPlayerStatus.vesselText;
                    lastPlayerStatus.statusText = myPlayerStatus.statusText;
                    networkWorker.SendPlayerStatus(myPlayerStatus);
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
        }

        public void AddPlayerStatus(PlayerStatus playerStatus)
        {
            addStatusQueue.Enqueue(playerStatus);
        }

        public void RemovePlayerStatus(string playerName)
        {
            removeStatusQueue.Enqueue(playerName);
        }

        public int GetPlayerCount()
        {
            return playerStatusList.Count;
        }

        public PlayerStatus GetPlayerStatus(string playerName)
        {
            PlayerStatus returnStatus = null;
            foreach (PlayerStatus ps in playerStatusList)
            {
                if (ps.playerName == playerName)
                {
                    returnStatus = ps;
                    break;
                }
            }
            return returnStatus;
        }

        public void Stop()
        {
                    workerEnabled = false;
                    dmpGame.updateEvent.Remove(Update);
        }
    }
}

