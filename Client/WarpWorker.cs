using System;
using System.Collections.Generic;
using UnityEngine;
using DarkMultiPlayerCommon;
using MessageStream;

namespace DarkMultiPlayer
{
    public class WarpWorker
    {
        public bool workerEnabled = false;
        public WarpMode warpMode = WarpMode.SUBSPACE;
        //Private parts
        private static WarpWorker singleton;
        //A list of lowest rates in MCW_LOWEST mode.
        private Dictionary<string, PlayerWarpRate> clientWarpList = new Dictionary<string, PlayerWarpRate>();
        //Read from DebugWindow
        public Dictionary<string, float> clientSkewList = new Dictionary<string, float>();
        //A list of the subspaces that all the clients belong to.
        private Dictionary<string, int> clientSubspaceList = new Dictionary<string, int>();
        //The player that can control warp in MCW_VOTE mode.
        private PlayerWarpRate lastSendRate = new PlayerWarpRate();
        private string warpMaster = "";
        private string voteMaster = "";
        private Dictionary<string, bool> voteList = new Dictionary<string, bool>();
        private int voteYesCount;
        private int voteNoCount;
        private int voteNeededCount;
        private int voteFailedCount;
        private bool voteSent;
        private double warpMasterOwnerTime;
        private double lastScreenMessageCheck;
        private double lastWarpSet;
        private double lastReportRate;
        private Queue<byte[]> newWarpMessages = new Queue<byte[]>();
        private ScreenMessage warpMessage;
        private const float SCREEN_MESSAGE_UPDATE_INTERVAL = 0.2f;
        private const float WARP_SET_THROTTLE = 1f;
        private const float REPORT_SKEW_RATE_INTERVAL = 10f;
        private const float MAX_WARP_TIME = 120f;
        private const float RELEASE_AFTER_WARP_TIME = 10f;

        public static WarpWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        private void Update()
        {
            if (workerEnabled)
            {
                CheckWarp();

                //Process new warp messages
                ProcessWarpMessages();

                //Write the screen message if needed
                if ((UnityEngine.Time.realtimeSinceStartup - lastScreenMessageCheck) > SCREEN_MESSAGE_UPDATE_INTERVAL)
                {
                    lastScreenMessageCheck = UnityEngine.Time.realtimeSinceStartup;
                    UpdateScreenMessage();
                }
                //Send a CHANGE_WARP message if needed
                if ((warpMode == WarpMode.MCW_FORCE) || (warpMode == WarpMode.MCW_VOTE) || (warpMode == WarpMode.SUBSPACE))
                {
                    if ((lastSendRate.rateIndex != TimeWarp.CurrentRateIndex) || (lastSendRate.isPhysWarp != (TimeWarp.WarpMode == TimeWarp.Modes.LOW)))
                    {
                        lastSendRate.isPhysWarp = (TimeWarp.WarpMode == TimeWarp.Modes.LOW);
                        lastSendRate.rateIndex = TimeWarp.CurrentRateIndex;
						using (var mw = new MessageWriter())
                        {
                            mw.Write<int>((int)WarpMessageType.CHANGE_WARP);
                            mw.Write<string>(Settings.fetch.playerName);
                            mw.Write<bool>(lastSendRate.isPhysWarp);
                            mw.Write<int>(lastSendRate.rateIndex);
                            NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                        }
                    }
                }

                if (warpMode == WarpMode.MCW_FORCE || warpMode == WarpMode.MCW_VOTE)
                {
                    //Follow the warp master into warp if needed (MCW_FORCE/MCW_VOTE)
                    if ((UnityEngine.Time.realtimeSinceStartup - lastWarpSet) > WARP_SET_THROTTLE)
                    {
                        //The warp master isn't us
                        if ((warpMaster != Settings.fetch.playerName) && (warpMaster != ""))
                        {
                            //We have a entry for the warp master
                            if (clientWarpList.ContainsKey(warpMaster))
                            {
                                //Our warp rate is different
                                if (TimeWarp.CurrentRateIndex != clientWarpList[warpMaster].rateIndex || (TimeWarp.WarpMode == TimeWarp.Modes.LOW) != clientWarpList[warpMaster].isPhysWarp)
                                {
                                    lastWarpSet = UnityEngine.Time.realtimeSinceStartup;
                                    if (clientWarpList[warpMaster].isPhysWarp)
                                    {
                                        TimeWarp.fetch.Mode = TimeWarp.Modes.LOW;
                                        TimeWarp.SetRate(clientWarpList[warpMaster].rateIndex, true);
                                    }
                                    else
                                    {
                                        TimeWarp.fetch.Mode = TimeWarp.Modes.HIGH;
                                        TimeWarp.SetRate(clientWarpList[warpMaster].rateIndex, true);
                                    }
                                }
                            }
                        }
                    }
                    //Release the warp master if we have had it for too long
                    HandleMCWWarpMasterTimeouts();
                }
                //Set the warp rate to the lowest rate if needed (MCW_LOWEST)
                if (warpMode == WarpMode.MCW_LOWEST)
                {
                    int lowestPhysRateIndex = -1;
                    int lowestNormalRateIndex = -1;
                    foreach (KeyValuePair<string, PlayerWarpRate> pwr in clientWarpList)
                    {
                        if (pwr.Value.isPhysWarp)
                        {
                            if (lowestPhysRateIndex == -1)
                            {
                                lowestPhysRateIndex = pwr.Value.rateIndex;
                            }
                            if (lowestPhysRateIndex > pwr.Value.rateIndex)
                            {
                                lowestPhysRateIndex = pwr.Value.rateIndex;
                            }
                        }
                        else
                        {
                            if (lowestNormalRateIndex == -1)
                            {
                                lowestNormalRateIndex = pwr.Value.rateIndex;
                            }
                            if (lowestNormalRateIndex > pwr.Value.rateIndex)
                            {
                                lowestNormalRateIndex = pwr.Value.rateIndex;
                            }
                        }
                    }
                    if (lowestNormalRateIndex > 0 && lowestPhysRateIndex == -1)
                    {
                        TimeWarp.fetch.Mode = TimeWarp.Modes.HIGH;
                        if (TimeWarp.CurrentRateIndex != lowestNormalRateIndex)
                        {
                            TimeWarp.SetRate(lowestNormalRateIndex, true);
                        }
                    }
                    else if (lowestPhysRateIndex > 0 && lowestNormalRateIndex == -1)
                    {
                        TimeWarp.fetch.Mode = TimeWarp.Modes.LOW;
                        if (TimeWarp.CurrentRateIndex != lowestPhysRateIndex)
                        {
                            TimeWarp.SetRate(lowestNormalRateIndex, true);
                        }
                    }
                }
                //Report our timeSyncer skew
                if ((UnityEngine.Time.realtimeSinceStartup - lastReportRate) > REPORT_SKEW_RATE_INTERVAL && TimeSyncer.fetch.locked)
                {
                    lastReportRate = UnityEngine.Time.realtimeSinceStartup;
					using (var mw = new MessageWriter())
                    {
                        mw.Write<int>((int)WarpMessageType.REPORT_RATE);
                        mw.Write<string>(Settings.fetch.playerName);
                        mw.Write<int>(TimeSyncer.fetch.currentSubspace);
                        mw.Write<float>(TimeSyncer.fetch.requestedRate);
                        NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                    }
                }
                //Handle warp keys
                HandleInput();
            }
        }

        public void ProcessWarpMessages()
        {
            while (newWarpMessages.Count > 0)
            {
                HandleWarpMessage(newWarpMessages.Dequeue());
            }
        }

        private void UpdateScreenMessage()
        {
            if (warpMaster != "")
            {
                if (warpMaster != Settings.fetch.playerName)
                {
                    DisplayMessage(warpMaster + " currently has warp control", 1f);
                }
                else
                {
                    int maxTimeout = (int)MAX_WARP_TIME - (int)(UnityEngine.Time.realtimeSinceStartup - warpMasterOwnerTime);
                    DisplayMessage("You have warp control, press '<' while not in warp to release (timeout " + maxTimeout + "s)", 1f);
                }
            }
            else
            {
                if (voteMaster != "")
                {
                    if (voteMaster == Settings.fetch.playerName)
                    {
                        DisplayMessage("Waiting for vote replies... Yes: " + voteYesCount + ", No: " + voteNoCount + ", Needed: " + voteNeededCount, 1f);
                    }
                    else
                    {
                        if (voteSent)
                        {
                            DisplayMessage("Voted!", 1f);
                        }
                        else
                        {
                            DisplayMessage(voteMaster + " has started a warp vote, reply with '<' for no or '>' for yes", 1f);
                        }
                    }
                }
            }
        }

        private void CheckWarp()
        {
            bool resetWarp = true;
            if ((warpMode == WarpMode.MCW_FORCE) || (warpMode == WarpMode.MCW_VOTE))
            {
                if (warpMaster != "")
                {
                    //It could be us or another player. If it's another player it will be controlled from Update() instead.
                    resetWarp = false;
                }
            }
            if (warpMode == WarpMode.SUBSPACE)
            {
                //Never reset warp in SUBSPACE mode.
                resetWarp = false;
            }
            if (warpMode == WarpMode.MCW_LOWEST)
            {
                //Controlled from above in Update()
                resetWarp = false;
            }
            if ((TimeWarp.CurrentRateIndex > 0) && resetWarp)
            {
                DarkLog.Debug("Resetting warp rate back to 0");
                TimeWarp.SetRate(0, true);
            }
            if ((TimeWarp.CurrentRateIndex > 0) && (TimeWarp.CurrentRate > 1.1f) && !resetWarp && TimeSyncer.fetch.locked)
            {
                DarkLog.Debug("Unlocking from subspace");
                TimeSyncer.fetch.UnlockSubspace();
            }
            if ((TimeWarp.CurrentRateIndex == 0) && (TimeWarp.CurrentRate < 1.1f) && !TimeSyncer.fetch.locked && (warpMode == WarpMode.SUBSPACE) && (TimeSyncer.fetch.currentSubspace == -1))
            {
                int newSubspaceID = TimeSyncer.fetch.LockNewSubspace(TimeSyncer.fetch.GetServerClock(), Planetarium.GetUniversalTime(), TimeSyncer.fetch.requestedRate);
                TimeSyncer.fetch.LockSubspace(newSubspaceID);
                Subspace newSubspace = TimeSyncer.fetch.GetSubspace(newSubspaceID);
				using (var mw = new MessageWriter())
                {
                    mw.Write<int>((int)WarpMessageType.NEW_SUBSPACE);
                    mw.Write<string>(Settings.fetch.playerName);
                    mw.Write<int>(newSubspaceID);
                    mw.Write<long>(newSubspace.serverClock);
                    mw.Write<double>(newSubspace.planetTime);
                    mw.Write<float>(newSubspace.subspaceSpeed);
                    NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                }
            }
        }

        private void HandleInput()
        {
            bool startWarpKey = Input.GetKeyDown(KeyCode.Period);
            bool stopWarpKey = Input.GetKeyDown(KeyCode.Comma);
            if (startWarpKey || stopWarpKey)
            {
                switch (warpMode)
                {
                    case WarpMode.NONE:
                        DisplayMessage("Cannot warp, warping is disabled on this server", 5f);
                        break;
                    case WarpMode.MCW_FORCE:
                        HandleMCWForceInput(startWarpKey, stopWarpKey);
                        break;
                    case WarpMode.MCW_VOTE:
                        HandleMCWVoteInput(startWarpKey, stopWarpKey);
                        break;
                }
            }
        }

        private void HandleMCWForceInput(bool startWarpKey, bool stopWarpKey)
        {
            if (warpMaster == "")
            {
                if (startWarpKey)
                {
                    warpMasterOwnerTime = UnityEngine.Time.realtimeSinceStartup;
                    warpMaster = Settings.fetch.playerName;
					using (var mw = new MessageWriter())
                    {
                        mw.Write<int>((int)WarpMessageType.SET_CONTROLLER);
                        mw.Write<string>(Settings.fetch.playerName);
                        mw.Write<string>(Settings.fetch.playerName);
                        NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                    }
                }
            }
            else if (warpMaster == Settings.fetch.playerName)
            {
                if (stopWarpKey && (TimeWarp.CurrentRate < 1.1f))
                {
                    ReleaseWarpMaster();
                }
            }
        }

        private void HandleMCWVoteInput(bool startWarpKey, bool stopWarpKey)
        {
            if (warpMaster == "")
            {
                if (voteMaster == "")
                {
                    if (startWarpKey)
                    {
                        if (PlayerStatusWorker.fetch.playerStatusList.Count > 0)
                        {
                            //Start a warp vote
							using (var mw = new MessageWriter())
                            {
                                mw.Write<int>((int)WarpMessageType.REQUEST_VOTE);
                                mw.Write<string>(Settings.fetch.playerName);
                                NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                            }
                            voteMaster = Settings.fetch.playerName;
                            //To win:
                            //1 other clients = 1 vote needed.
                            //2 other clients = 1 vote needed.
                            //3 other clients = 2 votes neeed.
                            //4 other clients = 2 votes neeed.
                            //5 other clients = 3 votes neeed.
                            //To fail:
                            //1 other clients = 1 vote needed.
                            //2 other clients = 2 vote needed.
                            //3 other clients = 2 votes neeed.
                            //4 other clients = 3 votes neeed.
                            //5 other clients = 3 votes neeed.
                            voteNeededCount = (PlayerStatusWorker.fetch.playerStatusList.Count + 1) / 2;
                            voteFailedCount = voteNeededCount + (1 - (voteNeededCount % 2));
                            DarkLog.Debug("Started warp vote");
                        }
                        else
                        {
                            //Nobody else is online, Let's just take the warp master.
                            warpMasterOwnerTime = UnityEngine.Time.realtimeSinceStartup;
                            warpMaster = Settings.fetch.playerName;
							using (var mw = new MessageWriter())
                            {
                                mw.Write<int>((int)WarpMessageType.SET_CONTROLLER);
                                mw.Write<string>(Settings.fetch.playerName);
                                mw.Write<string>(Settings.fetch.playerName);
                                NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                            }
                        }
                    }
                }
                else
                {
                    if (voteMaster != Settings.fetch.playerName)
                    {
                        //Send a vote if we haven't voted yet
                        if (!voteSent)
                        {
							using (var mw = new MessageWriter())
                            {
                                mw.Write<int>((int)WarpMessageType.REPLY_VOTE);
                                mw.Write<string>(Settings.fetch.playerName);
                                mw.Write<bool>(startWarpKey);
                                NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                                voteSent = true;
                            }
                            DarkLog.Debug("Send warp reply with vote of " + startWarpKey);
                        }
                    }
                    else
                    {
                        if (stopWarpKey)
                        {
                            //Cancel our vote
                            ReleaseWarpMaster();
                            DisplayMessage("Cancelled vote!", 2f);
                            DarkLog.Debug("Cancelled warp vote");
                        }
                    }
                }
            }
            else
            {
                if (warpMaster == Settings.fetch.playerName)
                {
                    if (stopWarpKey && (TimeWarp.CurrentRate < 1.1f))
                    {
                        //Release control of the warp master instead of waiting for the timeout
                        ReleaseWarpMaster();
                    }
                }
            }
        }

        private void HandleMCWWarpMasterTimeouts()
        {
            if (warpMaster == Settings.fetch.playerName)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - warpMasterOwnerTime) > MAX_WARP_TIME)
                {
                    DisplayMessage("Released warp from max timeout.", 5f);
                    ReleaseWarpMaster();
                }
            }
        }

        private void CancelVote()
        {
            voteSent = false;
            voteMaster = "";
            voteYesCount = 0;
            voteNoCount = 0;
            voteList.Clear();
        }

        private void ReleaseWarpMaster()
        {
            if (warpMaster == Settings.fetch.playerName)
            {
				using (var mw = new MessageWriter())
                {
                    long serverClock = TimeSyncer.fetch.GetServerClock();
                    double planetClock = Planetarium.GetUniversalTime();
                    int newSubspaceID = TimeSyncer.fetch.LockNewSubspace(serverClock, planetClock, 1f);
                    TimeSyncer.fetch.LockSubspace(newSubspaceID);
                    mw.Write<int>((int)WarpMessageType.NEW_SUBSPACE);
                    mw.Write<string>(Settings.fetch.playerName);
                    mw.Write<int>(newSubspaceID);
                    mw.Write<long>(serverClock);
                    mw.Write<double>(Planetarium.GetUniversalTime());
                    mw.Write<float>(1f);
                    NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                }
            }
            warpMaster = "";
            warpMasterOwnerTime = 0f;
            CancelVote();
			using (var mw = new MessageWriter())
            {
                mw.Write<int>((int)WarpMessageType.SET_CONTROLLER);
                mw.Write<string>(Settings.fetch.playerName);
                mw.Write<string>("");
                NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
            }
            if (TimeWarp.CurrentRateIndex > 0)
            {
                DarkLog.Debug("Resetting warp rate back to 0");
                TimeWarp.SetRate(0, true);
            }
        }

        private void HandleWarpMessage(byte[] messageData)
        {
			using (var mr = new MessageReader(messageData, false))
            {
                WarpMessageType messageType = (WarpMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                switch (messageType)
                {
                    case WarpMessageType.REQUEST_VOTE:
                        {
                            if (warpMode == WarpMode.MCW_VOTE)
                            {
                                if (voteMaster == "")
                                {
                                    voteMaster = fromPlayer;
                                }
                                else
                                {
                                    //Freak out and tell everyone to reset their warp votes - This can happen if 2 clients start a vote at the exact same time.
                                    ReleaseWarpMaster();
                                }
                            }
                        }
                        break;
                    case WarpMessageType.REPLY_VOTE:
                        {
                            if (warpMode == WarpMode.MCW_VOTE)
                            {
                                if (voteMaster == Settings.fetch.playerName && warpMaster == "")
                                {
                                    if (!voteList.ContainsKey(fromPlayer))
                                    {
                                        bool vote = mr.Read<bool>();
                                        DarkLog.Debug(fromPlayer + " voted " + vote);
                                        voteList.Add(fromPlayer, vote);
                                    }
                                    voteNoCount = 0;
                                    voteYesCount = 0;
                                    foreach (KeyValuePair<string,bool> vote in voteList)
                                    {
                                        if (vote.Value)
                                        {
                                            voteYesCount++;
                                        }
                                        else
                                        {
                                            voteNoCount++;
                                        }
                                    }

                                    //We have enough votes
                                    if (voteYesCount >= voteNeededCount)
                                    {
                                        //Vote has passed.
                                        warpMasterOwnerTime = UnityEngine.Time.realtimeSinceStartup;
                                        warpMaster = Settings.fetch.playerName;
										using (var mw = new MessageWriter())
                                        {
                                            mw.Write<int>((int)WarpMessageType.SET_CONTROLLER);
                                            mw.Write<string>(Settings.fetch.playerName);
                                            mw.Write<string>(Settings.fetch.playerName);
                                            NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                                        }
                                    }
                                    //We have enough votes
                                    if (voteNoCount >= voteFailedCount)
                                    {
                                        //Vote has failed.
                                        ReleaseWarpMaster();
                                        DisplayMessage("Vote failed!", 5f);
                                    }
                                }
                            }
                        }
                        break;
                    case WarpMessageType.SET_CONTROLLER:
                        {
                            if (warpMode == WarpMode.MCW_FORCE || warpMode == WarpMode.MCW_VOTE || warpMode == WarpMode.MCW_LOWEST)
                            {
                                string newController = mr.Read<string>();
                                warpMaster = newController;
                                if (warpMode == WarpMode.MCW_FORCE && newController == "")
                                {
                                    warpMasterOwnerTime = 0f;
                                }
                                if (warpMode == WarpMode.MCW_VOTE && newController == "")
                                {
                                    TimeWarp.SetRate(0, true);
                                    CancelVote();
                                }
                            }
                        }
                        break;
                    case WarpMessageType.CHANGE_WARP:
                        {
                            if (warpMode == WarpMode.MCW_FORCE || warpMode == WarpMode.MCW_VOTE || warpMode == WarpMode.MCW_LOWEST || warpMode == WarpMode.SUBSPACE)
                            {
                                bool newPhysWarp = mr.Read<bool>();
                                int newRateIndex = mr.Read<int>();
                                if (clientWarpList.ContainsKey(fromPlayer))
                                {
                                    clientWarpList[fromPlayer].isPhysWarp = newPhysWarp;
                                    clientWarpList[fromPlayer].rateIndex = newRateIndex;
                                }
                                else
                                {
									var newPlayerWarpRate = new PlayerWarpRate();
                                    newPlayerWarpRate.isPhysWarp = newPhysWarp;
                                    newPlayerWarpRate.rateIndex = newRateIndex;
                                    clientWarpList.Add(fromPlayer, newPlayerWarpRate);
                                }
                                //DarkLog.Debug(fromPlayer + " warp rate changed, Physwarp: " + newPhysWarp + ", Index: " + newRateIndex);
                            }
                        }
                        break;
                    case WarpMessageType.NEW_SUBSPACE:
                        {
                            int newSubspaceID = mr.Read<int>();
                            long serverTime = mr.Read<long>();
                            double planetariumTime = mr.Read<double>();
                            float gameSpeed = mr.Read<float>();
                            TimeSyncer.fetch.LockNewSubspace(newSubspaceID, serverTime, planetariumTime, gameSpeed);
                            if (((warpMode == WarpMode.MCW_VOTE) || (warpMode == WarpMode.MCW_FORCE)) && (warpMaster == fromPlayer))
                            {
                                TimeSyncer.fetch.LockSubspace(newSubspaceID);
                            }
                            if (!TimeSyncer.fetch.locked && TimeSyncer.fetch.currentSubspace == newSubspaceID)
                            {
                                TimeSyncer.fetch.LockSubspace(newSubspaceID);
                            }
                        }
                        break;
                    case WarpMessageType.CHANGE_SUBSPACE:
                        {
                            int changeSubspaceID = mr.Read<int>();
                            clientSubspaceList[fromPlayer] = changeSubspaceID;
                        }
                        break;
                    case WarpMessageType.RELOCK_SUBSPACE:
                        {
                            if (fromPlayer == "Server")
                            {
                                int subspaceID = mr.Read<int>();
                                long serverTime = mr.Read<long>();
                                double planetariumTime = mr.Read<double>();
                                float gameSpeed = mr.Read<float>();
                                TimeSyncer.fetch.RelockSubspace(subspaceID, serverTime, planetariumTime, gameSpeed);
                            }
                        }
                        break;
                    case WarpMessageType.REPORT_RATE:
                        {
                            //Not interested in subspace.
                            mr.Read<int>();
                            clientSkewList[fromPlayer] = mr.Read<float>();
                        }
                        break;
                    default:
                        {
                            DarkLog.Debug("Unhandled WARP_MESSAGE type: " + messageType);
                            break;
                        }
                }
            }
        }

        private void DisplayMessage(string messageText, float messageDuration)
        {
            if (warpMessage != null)
            {
                warpMessage.duration = 0f;
            }
            warpMessage = ScreenMessages.PostScreenMessage(messageText, messageDuration, ScreenMessageStyle.UPPER_CENTER);
        }

        public void QueueWarpMessage(byte[] messageData)
        {
            newWarpMessages.Enqueue(messageData);
        }

        public int GetClientSubspace(string playerName)
        {
            return clientSubspaceList.ContainsKey(playerName) ? clientSubspaceList[playerName] : -1;
        }

        public List<int> GetActiveSubspaces()
        {
			var sortedList = new SortedList<double, int>();
            sortedList.Add(TimeSyncer.fetch.GetUniverseTime(), TimeSyncer.fetch.currentSubspace);
            foreach (KeyValuePair<string, int> clientSubspace in clientSubspaceList)
            {
                if (!sortedList.ContainsValue(clientSubspace.Value))
                {
                    //Normal subspace
                    sortedList.Add(TimeSyncer.fetch.GetUniverseTime(clientSubspace.Value), clientSubspace.Value);
                }
            }
			var returnList = new List<int>();
            foreach (KeyValuePair<double, int> subspaceID in sortedList)
            {
                returnList.Add(subspaceID.Value);
            }
            returnList.Reverse();
            return returnList;
        }

        public List<string> GetClientsInSubspace(int subspace)
        {
			var returnList = new List<string>();
            //Add other players
            foreach (KeyValuePair<string, int> clientSubspace in clientSubspaceList)
            {
                if (clientSubspace.Value == subspace)
                {
                    returnList.Add(clientSubspace.Key);
                }
            }
            returnList.Sort();
            //Add us if we are in the subspace
            if (TimeSyncer.fetch.currentSubspace == subspace)
            {
                //We are on top!
                returnList.Insert(0, Settings.fetch.playerName);
            }
            return returnList;
        }

        public void RemovePlayer(string playerName)
        {
            if (clientSubspaceList.ContainsKey(playerName))
            {
                clientSubspaceList.Remove(playerName);
            }
            if (clientSkewList.ContainsKey(playerName))
            {
                clientSkewList.Remove(playerName);
            }
            if (clientWarpList.ContainsKey(playerName))
            {
                clientWarpList.Remove(playerName);
            }
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    singleton.workerEnabled = false;
                    Client.updateEvent.Remove(singleton.Update);
                }
                singleton = new WarpWorker();
                Client.updateEvent.Add(singleton.Update);
            }
        }
    }

    public class PlayerWarpRate
    {
        public bool isPhysWarp = false;
        public int rateIndex = 0;
    }
}

