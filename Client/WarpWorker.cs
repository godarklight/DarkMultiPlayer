using System;
using System.Collections.Generic;
using UnityEngine;
using DarkMultiPlayerCommon;
using MessageStream;

namespace DarkMultiPlayer
{
    public class WarpWorker
    {
        public bool enabled;
        public WarpMode warpMode;
        //Private parts
        private Client parent;
        //A list of lowest rates in MCW_LOWEST mode.
        private Dictionary<string, PlayerWarpRate> ClientWarpList;
        //The player that can control warp in MCW_VOTE mode.
        private string warpMaster;
        private string voteMaster;
        private Dictionary<string, bool> voteList;
        private int voteYesCount;
        private int voteNoCount;
        private int voteNeededCount;
        private int voteFailedCount;
        private bool canWarp;
        private bool voteSent;
        private bool registered;
        private double warpMasterOwnerTime;
        private double lastScreenMessageCheck;
        private Queue<byte[]> newWarpMessages;
        private ScreenMessage warpMessage;
        private const float SCREEN_MESSAGE_UPDATE_INTERVAL = 0.2f;
        private const float MAX_WARP_TIME = 120f;
        private const float RELEASE_AFTER_WARP_TIME = 10f;

        public WarpWorker(Client parent)
        {
            this.parent = parent;
            Reset();
        }

        public void Update()
        {
            if (enabled)
            {
                if (!registered)
                {
                    GameEvents.onTimeWarpRateChanged.Add(OnWarpChanged);
                }
                while (newWarpMessages.Count > 0)
                {
                    HandleWarpMessage(newWarpMessages.Dequeue());
                }
                if ((UnityEngine.Time.realtimeSinceStartup - lastScreenMessageCheck) > SCREEN_MESSAGE_UPDATE_INTERVAL)
                {
                    lastScreenMessageCheck = UnityEngine.Time.realtimeSinceStartup;
                    UpdateScreenMessage();
                }
                HandleInput();
                HandleWarpMasterTimeouts();
            }
            else
            {
                if (registered)
                {
                    registered = false;
                    try
                    {
                        GameEvents.onTimeWarpRateChanged.Remove(OnWarpChanged);
                    }
                    catch
                    {
                        DarkLog.Debug("Failed to unregister onTimeWarpRateChanged hook");
                    }
                }
            }
        }

        private void UpdateScreenMessage()
        {
            if (warpMaster != "")
            {
                if (warpMaster != parent.settings.playerName)
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
                    if (voteMaster == parent.settings.playerName)
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

        public void OnWarpChanged()
        {
            if (!canWarp && TimeWarp.CurrentRateIndex > 0)
            {
                DarkLog.Debug("Resetting warp rate back to 0");
                TimeWarp.SetRate(0, true);
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
                    case WarpMode.MCW_VOTE:
                        HandleVoteInput(startWarpKey, stopWarpKey);
                        break;
                }
            }
        }

        private void HandleVoteInput(bool startWarpKey, bool stopWarpKey)
        {
            if (warpMaster == "")
            {
                if (voteMaster == "")
                {
                    if (startWarpKey)
                    {
                        if (parent.playerStatusWorker.playerStatusList.Count > 0)
                        {
                            //Start a warp vote
                            using (MessageWriter mw = new MessageWriter(0, false))
                            {
                                mw.Write<int>((int)WarpMessageType.REQUEST_VOTE);
                                mw.Write<string>(parent.settings.playerName);
                                parent.networkWorker.SendWarpMessage(mw.GetMessageBytes());
                            }
                            voteMaster = parent.settings.playerName;
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
                            voteNeededCount = (parent.playerStatusWorker.playerStatusList.Count + 1) / 2;
                            voteFailedCount = voteNeededCount + (1 - (voteNeededCount % 2));
                            DarkLog.Debug("Started warp vote");
                        }
                        else
                        {
                            //Nobody else is online, Let's just take the warp master.
                            warpMasterOwnerTime = UnityEngine.Time.realtimeSinceStartup;
                            warpMaster = parent.settings.playerName;
                            canWarp = true;
                            using (MessageWriter mw = new MessageWriter(0, false))
                            {
                                mw.Write<int>((int)WarpMessageType.SET_CONTROLLER);
                                mw.Write<string>(parent.settings.playerName);
                                mw.Write<string>(parent.settings.playerName);
                                parent.networkWorker.SendWarpMessage(mw.GetMessageBytes());
                            }
                        }
                    }
                }
                else
                {
                    if (voteMaster != parent.settings.playerName)
                    {
                        //Send a vote if we haven't voted yet
                        if (!voteSent)
                        {
                            using (MessageWriter mw = new MessageWriter(0, false))
                            {
                                mw.Write<int>((int)WarpMessageType.REPLY_VOTE);
                                mw.Write<string>(parent.settings.playerName);
                                mw.Write<bool>(startWarpKey);
                                parent.networkWorker.SendWarpMessage(mw.GetMessageBytes());
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
                if (warpMaster == parent.settings.playerName)
                {
                    if (stopWarpKey && (TimeWarp.CurrentRate == 1f))
                    {
                        //Release control of the warp master instead of waiting for the timeout
                        ReleaseWarpMaster();
                    }
                }
            }
        }

        private void HandleWarpMasterTimeouts()
        {
            if (warpMaster == parent.settings.playerName)
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
            canWarp = false;
            warpMaster = "";
            warpMasterOwnerTime = 0f;
            CancelVote();
            using (MessageWriter mw = new MessageWriter(0, false))
            {
                mw.Write<int>((int)WarpMessageType.SET_CONTROLLER);
                mw.Write<string>(parent.settings.playerName);
                mw.Write<string>("");
                parent.networkWorker.SendWarpMessage(mw.GetMessageBytes());
            }
            if (!canWarp && TimeWarp.CurrentRateIndex > 0)
            {
                DarkLog.Debug("Resetting warp rate back to 0");
                TimeWarp.SetRate(0, true);
            }
        }

        private void HandleWarpMessage(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                WarpMessageType messageType = (WarpMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                DarkLog.Debug("Handling " + messageType + " from " + fromPlayer);
                switch (messageType)
                {
                    case WarpMessageType.REQUEST_VOTE:
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
                        break;
                    case WarpMessageType.REPLY_VOTE:
                        if (warpMode == WarpMode.MCW_VOTE)
                        {
                            if (voteMaster == parent.settings.playerName && warpMaster == "")
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
                                    warpMaster = parent.settings.playerName;
                                    canWarp = true;
                                    using (MessageWriter mw = new MessageWriter(0, false))
                                    {
                                        mw.Write<int>((int)WarpMessageType.SET_CONTROLLER);
                                        mw.Write<string>(parent.settings.playerName);
                                        mw.Write<string>(parent.settings.playerName);
                                        parent.networkWorker.SendWarpMessage(mw.GetMessageBytes());
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
                        break;
                    case WarpMessageType.SET_CONTROLLER:
                        if (warpMode == WarpMode.MCW_FORCE || warpMode == WarpMode.MCW_VOTE || warpMode == WarpMode.MCW_LOWEST)
                        {
                            string newController = mr.Read<string>();
                            warpMaster = newController;
                            if (warpMode == WarpMode.MCW_VOTE && newController == "")
                            {
                                CancelVote();
                            }
                        }
                        break;
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

        public void Reset()
        {
            warpMaster = "";
            voteMaster = "";
            voteYesCount = 0;
            voteNoCount = 0;
            warpMode = WarpMode.NONE;
            canWarp = false;
            voteSent = false;
            lastScreenMessageCheck = 0f;
            ClientWarpList = new Dictionary<string, PlayerWarpRate>();
            voteList = new Dictionary<string, bool>();
            newWarpMessages = new Queue<byte[]>();
            if (ClientWarpList != null)
            {
                //Shutup compiler
            }
        }
    }

    public class PlayerWarpRate
    {
        public bool isPhysWarp;
        public int rateIndex;
    }
}

