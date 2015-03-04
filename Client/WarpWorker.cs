using System;
using System.Collections.Generic;
using UnityEngine;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayer
{
    public class WarpWorker
    {
        public bool workerEnabled = false;
        public WarpMode warpMode = WarpMode.SUBSPACE;
        //Private parts
        private static WarpWorker singleton;
        //The current warp state of warping players
        private Dictionary<string, PlayerWarpRate> clientWarpList = new Dictionary<string, PlayerWarpRate>();
        //Read from DebugWindow
        public Dictionary<string, float> clientSkewList = new Dictionary<string, float>();
        //A list of the subspaces that all the clients belong to.
        private Dictionary<string, int> clientSubspaceList = new Dictionary<string, int>();
        //MCW states
        private string warpMaster = "";
        private string voteMaster = "";
        private double controllerExpireTime = double.NegativeInfinity;
        private double voteExpireTime = double.NegativeInfinity;
        private int voteYesCount;
        private int voteNoCount;
        private bool voteSent;
        private double lastScreenMessageCheck;
        //MCW_Lowest state
        private bool requestPhysWarp = false;
        private int requestIndex = 0;
        //Subspace simple tracking
        private bool canSubspaceSimpleWarp;
        //Report tracking
        private double lastWarpSet;
        private double lastReportRate;
        private Queue<byte[]> newWarpMessages = new Queue<byte[]>();
        private ScreenMessage warpMessage;
        private const float SCREEN_MESSAGE_UPDATE_INTERVAL = 0.2f;
        private const float WARP_SET_THROTTLE = 1f;
        private const float REPORT_SKEW_RATE_INTERVAL = 10f;
        //MCW Succeed/Fail counts.
        private int voteNeededCount
        {
            get
            {
                return (PlayerStatusWorker.fetch.GetPlayerCount() + 1) / 2;
            }
        }

        private int voteFailedCount
        {
            get
            {

                return voteNeededCount + (1 - (voteNeededCount % 2));
            }
        }

        public static WarpWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        private void Update()
        {
            if (!workerEnabled)
            {
                return;
            }

            //Reset warp if we need to
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
            if ((warpMode == WarpMode.MCW_FORCE) || (warpMode == WarpMode.MCW_VOTE) || (warpMode == WarpMode.SUBSPACE) || warpMode == WarpMode.SUBSPACE_SIMPLE)
            {
                if (!clientWarpList.ContainsKey(Settings.fetch.playerName))
                {
                    clientWarpList[Settings.fetch.playerName] = new PlayerWarpRate();
                }
                PlayerWarpRate ourRate = clientWarpList[Settings.fetch.playerName];
                if ((ourRate.rateIndex != TimeWarp.CurrentRateIndex) || (ourRate.isPhysWarp != (TimeWarp.WarpMode == TimeWarp.Modes.LOW)))
                {
                    ourRate.isPhysWarp = (TimeWarp.WarpMode == TimeWarp.Modes.LOW);
                    ourRate.rateIndex = TimeWarp.CurrentRateIndex;
                    ourRate.serverClock = TimeSyncer.fetch.GetServerClock();
                    ourRate.planetTime = Planetarium.GetUniversalTime();
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)WarpMessageType.CHANGE_WARP);
                        mw.Write<bool>(ourRate.isPhysWarp);
                        mw.Write<int>(ourRate.rateIndex);
                        mw.Write<long>(ourRate.serverClock);
                        mw.Write<double>(ourRate.planetTime);
                        NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                    }
                }
            }

            if ((UnityEngine.Time.realtimeSinceStartup - lastWarpSet) > WARP_SET_THROTTLE)
            {
                //Follow the warp master into warp if needed (MCW_FORCE/MCW_VOTE)
                if (warpMode == WarpMode.MCW_FORCE || warpMode == WarpMode.MCW_VOTE)
                {
                    if ((warpMaster != "") && (warpMaster != Settings.fetch.playerName))
                    {
                        if (clientWarpList.ContainsKey(warpMaster))
                        {
                            //Get master warp rate
                            PlayerWarpRate masterWarpRate = clientWarpList[warpMaster];
                            SetTimeFromWarpEntry(masterWarpRate);
                            lastWarpSet = UnityEngine.Time.realtimeSinceStartup;
                        }
                        else
                        {
                            TimeWarp.SetRate(0, true);
                        }
                    }
                }

                if (warpMode == WarpMode.MCW_LOWEST)
                {
                    if ((warpMaster != "") && clientWarpList.ContainsKey(warpMaster))
                    {
                        //Get master warp rate
                        PlayerWarpRate masterWarpRate = clientWarpList[warpMaster];
                        SetTimeFromWarpEntry(masterWarpRate);
                        lastWarpSet = UnityEngine.Time.realtimeSinceStartup;
                    }
                }
            }

            //Report our timeSyncer skew
            if ((UnityEngine.Time.realtimeSinceStartup - lastReportRate) > REPORT_SKEW_RATE_INTERVAL && TimeSyncer.fetch.locked)
            {
                lastReportRate = UnityEngine.Time.realtimeSinceStartup;
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)WarpMessageType.REPORT_RATE);
                    mw.Write<float>(TimeSyncer.fetch.requestedRate);
                    NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                }
            }

            //Handle warp keys
            HandleInput();
        }

        private void SetTimeFromWarpEntry(PlayerWarpRate masterWarpRate)
        {
            float[] warpRates = null;
            if (masterWarpRate.isPhysWarp)
            {
                TimeWarp.fetch.Mode = TimeWarp.Modes.LOW;
                warpRates = TimeWarp.fetch.physicsWarpRates;
            }
            else
            {
                TimeWarp.fetch.Mode = TimeWarp.Modes.HIGH;
                warpRates = TimeWarp.fetch.warpRates;
            }

            if (TimeWarp.WarpMode != TimeWarp.Modes.LOW && masterWarpRate.isPhysWarp)
            {
                TimeWarp.fetch.Mode = TimeWarp.Modes.LOW;
            }

            if (TimeWarp.WarpMode != TimeWarp.Modes.HIGH && !masterWarpRate.isPhysWarp)
            {
                TimeWarp.fetch.Mode = TimeWarp.Modes.HIGH;
            }

            //Set warp
            if (TimeWarp.CurrentRateIndex != masterWarpRate.rateIndex)
            {
                TimeWarp.SetRate(masterWarpRate.rateIndex, true);
            }

            //Set clock
            long serverClockDiff = TimeSyncer.fetch.GetServerClock() - masterWarpRate.serverClock;
            double secondsDiff = serverClockDiff / 10000000d;
            double newTime = masterWarpRate.planetTime + (warpRates[masterWarpRate.rateIndex] * secondsDiff);
            Planetarium.SetUniversalTime(newTime);
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
            if (warpMode == WarpMode.MCW_FORCE || warpMode == WarpMode.MCW_VOTE)
            {
                if (warpMaster != "")
                {
                    int timeLeft = (int)(controllerExpireTime - UnityEngine.Time.realtimeSinceStartup);
                    if (warpMaster != Settings.fetch.playerName)
                    {
                        DisplayMessage(warpMaster + " currently has warp control (timeout " + timeLeft + "s)", 1f);
                    }
                    else
                    {
                        DisplayMessage("You have warp control, press '<' while not in warp to release (timeout " + timeLeft + "s)", 1f);
                    }
                }
                else
                {
                    if (voteMaster != "")
                    {
                        int timeLeft = (int)(voteExpireTime - UnityEngine.Time.realtimeSinceStartup);
                        if (voteMaster == Settings.fetch.playerName)
                        {
                            DisplayMessage("Waiting for vote replies... Yes: " + voteYesCount + ", No: " + voteNoCount + ", Needed: " + voteNeededCount + " (" + timeLeft + "s left)", 1f);
                        }
                        else
                        {
                            if (voteSent)
                            {
                                DisplayMessage("Voted! (Yes: " + voteYesCount + ", No: " + voteNoCount + ", Timeout: " + timeLeft + "s)", 1f);
                            }
                            else
                            {
                                DisplayMessage(voteMaster + " has started a warp vote, reply with '<' for no or '>' for yes (Yes: " + voteYesCount + ", No: " + voteNoCount + ", Timeout: " + timeLeft + "s)", 1f);
                            }
                        }
                    }
                }
            }
            if (warpMode == WarpMode.MCW_LOWEST)
            {
                string fastestPlayer = GetFastestWarpingPlayer();
                float ourRate = GetRateAtIndex(requestIndex, requestPhysWarp);
                string displayMessage = String.Empty;

                if (fastestPlayer != null && fastestPlayer != Settings.fetch.playerName && clientWarpList.ContainsKey(fastestPlayer))
                {
                    PlayerWarpRate fastestRate = clientWarpList[fastestPlayer];
                    displayMessage += "\n" + fastestPlayer + " is requesting rate " + GetRateAtIndex(fastestRate.rateIndex, fastestRate.isPhysWarp) + "x";
                }

                if (ourRate > 1f)
                {
                    displayMessage += "\nWe are requesting rate: " + ourRate + "x";
                }

                if (warpMaster != null && clientWarpList.ContainsKey(warpMaster))
                {
                    PlayerWarpRate currentWarpRate = clientWarpList[warpMaster];
                    float currentRate = GetRateAtIndex(currentWarpRate.rateIndex, currentWarpRate.isPhysWarp);
                    displayMessage += "\nCurrent warp rate: " + currentRate + "x";
                }

                if (displayMessage != String.Empty)
                {
                    DisplayMessage(displayMessage, 1f);
                }
            }
            if (warpMode == WarpMode.SUBSPACE_SIMPLE)
            {
                int mostAdvancedSubspace = TimeSyncer.fetch.GetMostAdvancedSubspace();
                int ourSubspace = TimeSyncer.fetch.currentSubspace;
                canSubspaceSimpleWarp = true;
                if ((ourSubspace != -1) && (mostAdvancedSubspace != ourSubspace))
                {
                    canSubspaceSimpleWarp = false;
                    double deltaSeconds = TimeSyncer.fetch.GetUniverseTime(mostAdvancedSubspace) - TimeSyncer.fetch.GetUniverseTime(ourSubspace);
                    DisplayMessage("Press '>' to warp " + Math.Round(deltaSeconds) + "s into the future", 1f);
                }
            }
        }

        //Returns a player name of the fastest warping player, or null if nobody is warping
        private string GetFastestWarpingPlayer()
        {
            string fastestPlayer = null;
            float fastestRate = 1f;
            foreach (KeyValuePair<string, PlayerWarpRate> kvp in clientWarpList)
            {
                float currentRate = GetRateAtIndex(kvp.Value.rateIndex, kvp.Value.isPhysWarp);
                if (currentRate > fastestRate)
                {
                    fastestPlayer = kvp.Key;
                    fastestRate = currentRate;
                }
            }
            return fastestPlayer;
        }

        private float GetRateAtIndex(int index, bool physWarp)
        {
            if (TimeWarp.fetch == null)
            {
                return 1f;
            }
            if (physWarp)
            {
                if (index < TimeWarp.fetch.physicsWarpRates.Length)
                {
                    return TimeWarp.fetch.physicsWarpRates[index];
                }
            }
            else
            {
                if (index < TimeWarp.fetch.warpRates.Length)
                {
                    return TimeWarp.fetch.warpRates[index];
                }
            }
            return 1f;
        }

        private void CheckWarp()
        {
            bool resetWarp = true;
            if ((warpMode == WarpMode.MCW_FORCE) || (warpMode == WarpMode.MCW_VOTE) || (warpMode == WarpMode.MCW_LOWEST))
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
            if (warpMode == WarpMode.SUBSPACE_SIMPLE && canSubspaceSimpleWarp)
            {
                resetWarp = false;
            }
            if ((TimeWarp.CurrentRateIndex > 0) && resetWarp)
            {
                //DarkLog.Debug("Resetting warp rate back to 0");
                TimeWarp.SetRate(0, true);
            }
            if ((TimeWarp.CurrentRateIndex > 0) && (TimeWarp.CurrentRate > 1.1f) && !resetWarp && TimeSyncer.fetch.locked)
            {
                DarkLog.Debug("Unlocking from subspace");
                TimeSyncer.fetch.UnlockSubspace();
            }
            if ((TimeWarp.CurrentRateIndex == 0) && (TimeWarp.CurrentRate < 1.1f) && !TimeSyncer.fetch.locked && ((warpMode == WarpMode.SUBSPACE) || (warpMode == WarpMode.SUBSPACE_SIMPLE)) && (TimeSyncer.fetch.currentSubspace == -1))
            {
                SendNewSubspace();
            }
        }

        public static void SendNewSubspace()
        {
            long serverClock = TimeSyncer.fetch.GetServerClock();
            double planetClock = Planetarium.GetUniversalTime();
            float requestedRate = TimeSyncer.fetch.requestedRate;
            TimeSyncer.fetch.LockTemporarySubspace(serverClock, planetClock, requestedRate);
            SendNewSubspace(serverClock, planetClock, requestedRate);

        }

        public static void SendNewSubspace(long serverClock, double planetTime, float subspaceRate)
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)WarpMessageType.NEW_SUBSPACE);
                mw.Write<long>(serverClock);
                mw.Write<double>(planetTime);
                mw.Write<float>(subspaceRate);
                NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
            }
        }

        public void HandleSetSubspace(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                int subspaceID = mr.Read<int>();
                TimeSyncer.fetch.LockSubspace(subspaceID);
            }
        }

        private void HandleInput()
        {
            bool startWarpKey = GameSettings.TIME_WARP_INCREASE.GetKeyDown();
            bool stopWarpKey = GameSettings.TIME_WARP_DECREASE.GetKeyDown();
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
                    case WarpMode.MCW_LOWEST:
                        HandleMCWLowestInput(startWarpKey, stopWarpKey);
                        break;
                    case WarpMode.SUBSPACE_SIMPLE:
                        HandleSubspaceSimpleInput(startWarpKey, stopWarpKey);
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
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)WarpMessageType.REQUEST_CONTROLLER);
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
                        //Start a warp vote
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<int>((int)WarpMessageType.REQUEST_CONTROLLER);
                            NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
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
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write<int>((int)WarpMessageType.REPLY_VOTE);
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

        private void HandleMCWLowestInput(bool startWarpKey, bool stopWarpKey)
        {
            int newRequestIndex = requestIndex;
            bool newRequestPhysWarp = requestPhysWarp;
            if (startWarpKey)
            {
                if (requestIndex == 0)
                {
                    newRequestPhysWarp = GameSettings.MODIFIER_KEY.GetKey();
                }
                float[] physRates = TimeWarp.fetch.physicsWarpRates;
                float[] warpRates = TimeWarp.fetch.warpRates;
                newRequestIndex++;
                if (newRequestPhysWarp && newRequestIndex >= physRates.Length)
                {
                    newRequestIndex = physRates.Length - 1;
                }
                if (!newRequestPhysWarp && newRequestIndex >= warpRates.Length)
                {
                    newRequestIndex = warpRates.Length - 1;
                }
            }
            if (stopWarpKey)
            {
                newRequestIndex--;
                if (newRequestIndex < 0)
                {
                    newRequestIndex = 0;
                }
                if (newRequestIndex == 0)
                {
                    newRequestPhysWarp = false;
                }
            }
            if ((newRequestIndex != requestIndex) || (newRequestPhysWarp != requestPhysWarp))
            {
                requestIndex = newRequestIndex;
                requestPhysWarp = newRequestPhysWarp;
                PlayerWarpRate newWarpRate = new PlayerWarpRate();
                newWarpRate.isPhysWarp = requestPhysWarp;
                newWarpRate.rateIndex = requestIndex;
                newWarpRate.planetTime = Planetarium.GetUniversalTime();
                newWarpRate.serverClock = TimeSyncer.fetch.GetServerClock();
                clientWarpList[Settings.fetch.playerName] = newWarpRate;
                DarkLog.Debug("Warp request change: " + requestIndex + ", physwarp: " + requestPhysWarp);
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)WarpMessageType.CHANGE_WARP);
                    mw.Write<bool>(requestPhysWarp);
                    mw.Write<int>(requestIndex);
                    mw.Write<long>(TimeSyncer.fetch.GetServerClock());
                    mw.Write<double>(Planetarium.GetUniversalTime());
                    NetworkWorker.fetch.SendWarpMessage(mw.GetMessageBytes());
                }
            }
        }

        private void HandleSubspaceSimpleInput(bool startWarpKey, bool stopWarpKey)
        {
            if (startWarpKey && !canSubspaceSimpleWarp)
            {
                TimeSyncer.fetch.LockSubspace(TimeSyncer.fetch.GetMostAdvancedSubspace());
            }
        }

        private void ReleaseWarpMaster()
        {
            if (warpMaster == Settings.fetch.playerName)
            {
                SendNewSubspace();
            }
            warpMaster = "";
            voteSent = false;
            voteMaster = "";
            voteYesCount = 0;
            voteNoCount = 0;
            controllerExpireTime = double.NegativeInfinity;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)WarpMessageType.RELEASE_CONTROLLER);
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
            using (MessageReader mr = new MessageReader(messageData))
            {
                WarpMessageType messageType = (WarpMessageType)mr.Read<int>();
                switch (messageType)
                {
                    case WarpMessageType.REQUEST_VOTE:
                        {
                            voteMaster = mr.Read<string>();
                            long expireTime = mr.Read<long>();
                            voteExpireTime = Time.realtimeSinceStartup + ((expireTime - TimeSyncer.fetch.GetServerClock()) / 10000000d);
                        }
                        break;
                    case WarpMessageType.REPLY_VOTE:
                        {
                            int voteYesCount = mr.Read<int>();
                            int voteNoCount = mr.Read<int>();
                            HandleReplyVote(voteYesCount, voteNoCount);
                        }
                        break;
                    case WarpMessageType.SET_CONTROLLER:
                        {
                            string newController = mr.Read<string>();
                            long expireTime = mr.Read<long>();
                            HandleSetController(newController, expireTime);
                        }
                        break;
                    case WarpMessageType.CHANGE_WARP:
                        {
                            string fromPlayer = mr.Read<string>();
                            bool isPhysWarp = mr.Read<bool>();
                            int rateIndex = mr.Read<int>();
                            long serverClock = mr.Read<long>();
                            double planetTime = mr.Read<double>();
                            HandleChangeWarp(fromPlayer, isPhysWarp, rateIndex, serverClock, planetTime);
                        }
                        break;
                    case WarpMessageType.NEW_SUBSPACE:
                        {
                            int newSubspaceID = mr.Read<int>();
                            long serverTime = mr.Read<long>();
                            double planetariumTime = mr.Read<double>();
                            float gameSpeed = mr.Read<float>();
                            TimeSyncer.fetch.AddNewSubspace(newSubspaceID, serverTime, planetariumTime, gameSpeed);
                        }
                        break;
                    case WarpMessageType.CHANGE_SUBSPACE:
                        {
                            string fromPlayer = mr.Read<string>();
                            if (fromPlayer != Settings.fetch.playerName)
                            {
                                int changeSubspaceID = mr.Read<int>();
                                clientSubspaceList[fromPlayer] = changeSubspaceID;
                                if (changeSubspaceID != -1)
                                {
                                    if (clientWarpList.ContainsKey(fromPlayer))
                                    {
                                        clientWarpList.Remove(fromPlayer);
                                    }
                                }
                            }
                        }
                        break;
                    case WarpMessageType.RELOCK_SUBSPACE:
                        {
                            int subspaceID = mr.Read<int>();
                            long serverTime = mr.Read<long>();
                            double planetariumTime = mr.Read<double>();
                            float gameSpeed = mr.Read<float>();
                            TimeSyncer.fetch.RelockSubspace(subspaceID, serverTime, planetariumTime, gameSpeed);
                        }
                        break;
                    case WarpMessageType.REPORT_RATE:
                        {
                            string fromPlayer = mr.Read<string>();
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

        private void HandleReplyVote(int voteYesCount, int voteNoCount)
        {
            if (warpMode == WarpMode.MCW_VOTE)
            {
                this.voteYesCount = voteYesCount;
                this.voteNoCount = voteNoCount;
                if (voteMaster == Settings.fetch.playerName && voteNoCount >= voteFailedCount)
                {
                    DisplayMessage("Vote failed!", 3f);
                }
            }
        }

        private void HandleSetController(string newController, long expireTime)
        {
            if (warpMode == WarpMode.MCW_FORCE || warpMode == WarpMode.MCW_VOTE || warpMode == WarpMode.MCW_LOWEST)
            {
                warpMaster = newController;
                if (warpMaster == "")
                {
                    voteMaster = "";
                    voteYesCount = 0;
                    voteNoCount = 0;
                    voteSent = false;
                    controllerExpireTime = double.NegativeInfinity;
                    TimeWarp.SetRate(0, true);
                }
                else
                {
                    if (warpMode != WarpMode.MCW_LOWEST)
                    {
                        long expireTimeDelta = expireTime - TimeSyncer.fetch.GetServerClock();
                        controllerExpireTime = Time.realtimeSinceStartup + (expireTimeDelta / 10000000d);
                    }
                }
            }
        }

        private void HandleChangeWarp(string fromPlayer, bool isPhysWarp, int newRate, long serverClock, double planetTime)
        {
            if (warpMode != WarpMode.NONE)
            {
                if (clientWarpList.ContainsKey(fromPlayer))
                {
                    clientWarpList[fromPlayer].isPhysWarp = isPhysWarp;
                    clientWarpList[fromPlayer].rateIndex = newRate;
                    clientWarpList[fromPlayer].serverClock = serverClock;
                    clientWarpList[fromPlayer].planetTime = planetTime;
                }
                else
                {
                    PlayerWarpRate newPlayerWarpRate = new PlayerWarpRate();
                    newPlayerWarpRate.isPhysWarp = isPhysWarp;
                    newPlayerWarpRate.rateIndex = newRate;
                    newPlayerWarpRate.serverClock = serverClock;
                    newPlayerWarpRate.planetTime = planetTime;
                    clientWarpList.Add(fromPlayer, newPlayerWarpRate);
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

        public SubspaceDisplayEntry[] GetSubspaceDisplayEntries()
        {
            if (warpMode == WarpMode.SUBSPACE || warpMode == WarpMode.SUBSPACE_SIMPLE)
            {
                return GetSubspaceDisplayEntriesSubspace();
            }
            return GetSubspaceDisplayEntriesMCWNone();
        }

        private SubspaceDisplayEntry[] GetSubspaceDisplayEntriesMCWNone()
        {
            int currentSubspace = TimeSyncer.fetch.currentSubspace;
            List<string> allPlayers = new List<string>();
            allPlayers.Add(Settings.fetch.playerName);
            allPlayers.AddRange(clientSubspaceList.Keys);
            allPlayers.Sort(PlayerSorter);
            SubspaceDisplayEntry sde = new SubspaceDisplayEntry();
            sde.players = allPlayers.ToArray();
            sde.isUs = true;
            if (currentSubspace != -1)
            {
                sde.subspaceID = TimeSyncer.fetch.currentSubspace;
                sde.subspaceEntry = TimeSyncer.fetch.GetSubspace(currentSubspace);
            }
            else
            {
                sde.isWarping = true;
                if (clientWarpList.ContainsKey(Settings.fetch.playerName))
                {
                    sde.warpingEntry = clientWarpList[Settings.fetch.playerName];
                }
            }
            return new SubspaceDisplayEntry[] { sde };
        }

        private SubspaceDisplayEntry[] GetSubspaceDisplayEntriesSubspace()
        {
            //Subspace/subspace simple mode
            Dictionary<int, List<string>> nonWarpCache = new Dictionary<int, List<string>>();
            List<string> warpCache = new List<string>();

            //Add us
            if (TimeSyncer.fetch.currentSubspace != -1)
            {
                List<string> newList = new List<string>();
                newList.Add(Settings.fetch.playerName);
                nonWarpCache.Add(TimeSyncer.fetch.currentSubspace, newList);
            }
            else
            {
                warpCache.Add(Settings.fetch.playerName);
            }

            //Add other players
            foreach (KeyValuePair<string, int> clientEntry in clientSubspaceList)
            {
                if (clientEntry.Value != -1)
                {
                    if (!nonWarpCache.ContainsKey(clientEntry.Value))
                    {
                        nonWarpCache.Add(clientEntry.Value, new List<string>());
                    }
                    if (!nonWarpCache[clientEntry.Value].Contains(clientEntry.Key))
                    {
                        nonWarpCache[clientEntry.Value].Add(clientEntry.Key);
                    }
                }
                else
                {
                    if (!warpCache.Contains(clientEntry.Key))
                    {
                        warpCache.Add(clientEntry.Key);
                    }
                }
            }
            //Players missing subspace or warp entries
            List<string> unknownPlayers = new List<string>();
            //Return list
            List<SubspaceDisplayEntry> returnList = new List<SubspaceDisplayEntry>();
            //Process locked players
            foreach (KeyValuePair<int, List<string>> subspaceEntry in nonWarpCache)
            {
                if (TimeSyncer.fetch.SubspaceExists(subspaceEntry.Key))
                {
                    SubspaceDisplayEntry sde = new SubspaceDisplayEntry();
                    sde.subspaceID = subspaceEntry.Key;
                    sde.subspaceEntry = TimeSyncer.fetch.GetSubspace(subspaceEntry.Key);
                    subspaceEntry.Value.Sort(PlayerSorter);
                    sde.isUs = subspaceEntry.Value.Contains(Settings.fetch.playerName);
                    sde.players = subspaceEntry.Value.ToArray();
                    returnList.Add(sde);
                }
                else
                {
                    foreach (string unknownPlayer in subspaceEntry.Value)
                    {
                        unknownPlayers.Add(unknownPlayer);
                    }
                }
            }
            //Process warping players
            foreach (string warpingPlayer in warpCache)
            {
                if (clientWarpList.ContainsKey(warpingPlayer))
                {
                    SubspaceDisplayEntry sde = new SubspaceDisplayEntry();
                    sde.warpingEntry = clientWarpList[warpingPlayer];
                    sde.players = new string[] { warpingPlayer };
                    sde.isUs = (warpingPlayer == Settings.fetch.playerName);
                    sde.isWarping = true;
                    returnList.Add(sde);
                }
                else
                {
                    unknownPlayers.Add(warpingPlayer);
                }
            }
            returnList.Sort(SubpaceDisplayEntrySorter);
            if (unknownPlayers.Count > 0)
            {
                SubspaceDisplayEntry sde = new SubspaceDisplayEntry();
                sde.players = unknownPlayers.ToArray();
                sde.isUs = unknownPlayers.Contains(Settings.fetch.playerName);
                returnList.Add(sde);
            }
            return returnList.ToArray();
        }

        private int PlayerSorter(string lhs, string rhs)
        {
            string ourName = Settings.fetch.playerName;
            if (lhs == ourName)
            {
                return -1;
            }
            if (rhs == ourName)
            {
                return 1;
            }
            return String.Compare(lhs, rhs);
        }

        private int SubpaceDisplayEntrySorter(SubspaceDisplayEntry lhs, SubspaceDisplayEntry rhs)
        {
            long serverClock = TimeSyncer.fetch.GetServerClock();
            double subspace1Time = double.MinValue;
            double subspace2Time = double.MinValue;
            //LHS time
            if (lhs.isWarping)
            {
                if (lhs.warpingEntry != null)
                {
                    float[] warpRates = TimeWarp.fetch.warpRates;
                    if (lhs.warpingEntry.isPhysWarp)
                    {
                        warpRates = TimeWarp.fetch.physicsWarpRates;
                    }
                    long serverClockDiff = serverClock - lhs.warpingEntry.serverClock;
                    double secondsDiff = serverClockDiff / 10000000d;
                    subspace1Time = lhs.warpingEntry.planetTime + (warpRates[lhs.warpingEntry.rateIndex] * secondsDiff);
                }
            }
            else
            {
                if (lhs.subspaceEntry != null)
                {
                    long serverClockDiff = serverClock - lhs.subspaceEntry.serverClock;
                    double secondsDiff = serverClockDiff / 10000000d;
                    subspace1Time = lhs.subspaceEntry.planetTime + (lhs.subspaceEntry.subspaceSpeed * secondsDiff);
                }
            }
            //RHS time
            if (rhs.isWarping)
            {
                if (rhs.warpingEntry != null)
                {
                    float[] warpRates = TimeWarp.fetch.warpRates;
                    if (rhs.warpingEntry.isPhysWarp)
                    {
                        warpRates = TimeWarp.fetch.physicsWarpRates;
                    }
                    long serverClockDiff = serverClock - rhs.warpingEntry.serverClock;
                    double secondsDiff = serverClockDiff / 10000000d;
                    subspace2Time = rhs.warpingEntry.planetTime + (warpRates[rhs.warpingEntry.rateIndex] * secondsDiff);
                }
            }
            else
            {
                if (rhs.subspaceEntry != null)
                {
                    long serverClockDiff = serverClock - rhs.subspaceEntry.serverClock;
                    double secondsDiff = serverClockDiff / 10000000d;
                    subspace2Time = rhs.subspaceEntry.planetTime + (rhs.subspaceEntry.subspaceSpeed * secondsDiff);
                }
            }

            if (subspace1Time > subspace2Time)
            {
                return -1;
            }
            if (subspace1Time == subspace2Time)
            {
                return 0;
            }
            return 1;
        }

        public List<int> GetActiveSubspaces()
        {
            List<int> returnList = new List<int>();
            returnList.Add(TimeSyncer.fetch.currentSubspace);
            foreach (KeyValuePair<string, int> clientSubspace in clientSubspaceList)
            {
                if (!returnList.Contains(clientSubspace.Value))
                {
                    returnList.Add(clientSubspace.Value);
                }
            }
            returnList.Sort(SubspaceComparer);
            return returnList;
        }

        private int SubspaceComparer(int lhs, int rhs)
        {
            double subspace1Time = TimeSyncer.fetch.GetUniverseTime(lhs);
            double subspace2Time = TimeSyncer.fetch.GetUniverseTime(rhs);
            //x<y -1, x==y 0, x>y 1
            if (subspace1Time < subspace2Time)
            {
                return -1;
            }
            if (subspace1Time == subspace2Time)
            {
                return 0;
            }
            return 1;
        }

        public List<string> GetClientsInSubspace(int subspace)
        {
            List<string> returnList = new List<string>();
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

    public class SubspaceDisplayEntry
    {
        public bool isWarping;
        public bool isUs;
        public PlayerWarpRate warpingEntry;
        public int subspaceID;
        public Subspace subspaceEntry;
        public string[] players;
    }
}

