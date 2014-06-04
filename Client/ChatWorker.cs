using System;
using System.Collections.Generic;
using UnityEngine;
using DarkMultiPlayerCommon;
using MessageStream;

namespace DarkMultiPlayer
{
    public class ChatWorker
    {
        private static ChatWorker singleton;
        public bool display = false;
        public bool workerEnabled = false;
        private bool safeDisplay = false;
        private bool initialized = false;
        //State tracking
        private Queue<string> disconnectingPlayers = new Queue<string>();
        private Queue<JoinLeaveMessage> newJoinMessages = new Queue<JoinLeaveMessage>();
        private Queue<JoinLeaveMessage> newLeaveMessages = new Queue<JoinLeaveMessage>();
        private Queue<ChannelEntry> newChannelMessages = new Queue<ChannelEntry>();
        private Queue<PrivateEntry> newPrivateMessages = new Queue<PrivateEntry>();
        private Dictionary<string, List<string>> channelMessages = new Dictionary<string, List<string>>();
        private Dictionary<string, List<string>> privateMessages = new Dictionary<string, List<string>>();
        private Dictionary<string, List<string>> playerChannels = new Dictionary<string, List<string>>();
        private List<string> joinedChannels = new List<string>();
        private List<string> joinedPMChannels = new List<string>();
        private List<string> highlightChannel = new List<string>();
        private List<string> highlightPM = new List<string>();
        public bool chatButtonHighlighted = false;
        private string selectedChannel = null;
        private string selectedPMChannel = null;
        private bool chatLocked = false;
        private bool ignoreChatInput = false;
        private bool selectTextBox = false;
        private string sendText = "";
        //event handling
        private bool leaveEventHandled = true;
        private bool sendEventHandled = true;
        //GUILayout stuff
        private Rect windowRect;
        private Rect moveRect;
        private GUILayoutOption[] windowLayoutOptions;
        private GUILayoutOption[] smallSizeOption;
        private GUIStyle windowStyle;
        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;
        private GUIStyle highlightStyle;
        private GUIStyle textAreaStyle;
        private GUIStyle scrollStyle;
        private Vector2 chatScrollPos;
        private Vector2 playerScrollPos;
        //const
        private const float WINDOW_HEIGHT = 300;
        private const float WINDOW_WIDTH = 400;
        private const string DMP_CHAT_LOCK = "DMP_ChatLock";
        public const ControlTypes BLOCK_ALL_CONTROLS = ControlTypes.ALL_SHIP_CONTROLS | ControlTypes.ACTIONS_ALL | ControlTypes.EVA_INPUT | ControlTypes.TIMEWARP | ControlTypes.MISC | ControlTypes.GROUPS_ALL | ControlTypes.CUSTOM_ACTION_GROUPS;

        public static ChatWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width / 10, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);

            windowLayoutOptions = new GUILayoutOption[4];
            windowLayoutOptions[0] = GUILayout.MinWidth(WINDOW_WIDTH);
            windowLayoutOptions[1] = GUILayout.MaxWidth(WINDOW_WIDTH);
            windowLayoutOptions[2] = GUILayout.MinHeight(WINDOW_HEIGHT);
            windowLayoutOptions[3] = GUILayout.MaxHeight(WINDOW_HEIGHT);

            smallSizeOption = new GUILayoutOption[1];
            smallSizeOption[0] = GUILayout.Width(WINDOW_WIDTH * .25f);

            windowStyle = new GUIStyle(GUI.skin.window);
            scrollStyle = new GUIStyle(GUI.skin.scrollView);

            chatScrollPos = new Vector2(0, 0);
            labelStyle = new GUIStyle(GUI.skin.label);
            buttonStyle = new GUIStyle(GUI.skin.button);
            highlightStyle = new GUIStyle(GUI.skin.button);
            highlightStyle.normal.textColor = Color.red;
            highlightStyle.active.textColor = Color.red;
            highlightStyle.hover.textColor = Color.red;
            textAreaStyle = new GUIStyle(GUI.skin.textArea);
        }

        public void QueueChatJoin(string playerName, string channelName)
        {
            JoinLeaveMessage jlm = new JoinLeaveMessage();
            jlm.fromPlayer = playerName;
            jlm.channel = channelName;
            newJoinMessages.Enqueue(jlm);
        }

        public void QueueChatLeave(string playerName, string channelName)
        {
            JoinLeaveMessage jlm = new JoinLeaveMessage();
            jlm.fromPlayer = playerName;
            jlm.channel = channelName;
            newLeaveMessages.Enqueue(jlm);
        }

        public void QueueChannelMessage(string fromPlayer, string channelName, string channelMessage)
        {
            ChannelEntry ce = new ChannelEntry();
            ce.fromPlayer = fromPlayer;
            ce.channel = channelName;
            ce.message = channelMessage;
            newChannelMessages.Enqueue(ce);
            if (!display)
            {
                chatButtonHighlighted = true;
                if (ce.channel != "")
                {
                    ScreenMessages.PostScreenMessage(ce.fromPlayer + " -> #" + ce.channel + ": " + ce.message, 5f, ScreenMessageStyle.UPPER_LEFT);
                }
                else
                {
                    ScreenMessages.PostScreenMessage(ce.fromPlayer + " -> #Global : " + ce.message, 5f, ScreenMessageStyle.UPPER_LEFT);
                }
            }
        }

        public void QueuePrivateMessage(string fromPlayer, string toPlayer, string privateMessage)
        {
            PrivateEntry pe = new PrivateEntry();
            pe.fromPlayer = fromPlayer;
            pe.toPlayer = toPlayer;
            pe.message = privateMessage;
            newPrivateMessages.Enqueue(pe);
            if (!display)
            {
                chatButtonHighlighted = true;
                if (pe.fromPlayer != Settings.fetch.playerName)
                {
                    ScreenMessages.PostScreenMessage(pe.fromPlayer + " -> @" + pe.toPlayer + ": " + pe.message, 5f, ScreenMessageStyle.UPPER_LEFT);
                }
            }
        }

        public void QueueRemovePlayer(string playerName)
        {
            disconnectingPlayers.Enqueue(playerName);
        }

        private void Update()
        {
            safeDisplay = display;
            ignoreChatInput = false;
            if (chatButtonHighlighted && display)
            {
                chatButtonHighlighted = false;
            }
            if (chatLocked && !display)
            {
                chatLocked = false;
                InputLockManager.RemoveControlLock(DMP_CHAT_LOCK);
            }
            if (workerEnabled)
            {
                //Handle leave event
                if (!leaveEventHandled)
                {
                    if (selectedChannel != null)
                    {
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<int>((int)ChatMessageType.LEAVE);
                            mw.Write<string>(Settings.fetch.playerName);
                            mw.Write<string>(selectedChannel);
                            NetworkWorker.fetch.SendChatMessage(mw.GetMessageBytes());
                        }
                        if (joinedChannels.Contains(selectedChannel))
                        {
                            joinedChannels.Remove(selectedChannel);
                        }
                        selectedChannel = null;
                        selectedPMChannel = null;
                    }
                    if (selectedPMChannel != null)
                    {
                        if (joinedPMChannels.Contains(selectedPMChannel))
                        {
                            joinedPMChannels.Remove(selectedPMChannel);
                        }
                        selectedChannel = null;
                        selectedPMChannel = null;
                    }
                    leaveEventHandled = true;
                }
                //Handle send event
                if (!sendEventHandled)
                {
                    if (sendText != "")
                    {
                        if (!sendText.StartsWith("/") || sendText.StartsWith("//"))
                        {
                            if (sendText.StartsWith("//"))
                            {
                                sendText = sendText.Substring(1);
                            }
                            if (selectedChannel == null && selectedPMChannel == null)
                            {
                                //Sending a global chat message
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                                    mw.Write<string>(Settings.fetch.playerName);
                                    //Global channel name is empty string.
                                    mw.Write<string>("");
                                    mw.Write<string>(sendText);
                                    NetworkWorker.fetch.SendChatMessage(mw.GetMessageBytes());
                                }
                            }
                            if (selectedChannel != null)
                            {
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                                    mw.Write<string>(Settings.fetch.playerName);
                                    mw.Write<string>(selectedChannel);
                                    mw.Write<string>(sendText);
                                    NetworkWorker.fetch.SendChatMessage(mw.GetMessageBytes());
                                }
                            }
                            if (selectedPMChannel != null)
                            {
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write<int>((int)ChatMessageType.PRIVATE_MESSAGE);
                                    mw.Write<string>(Settings.fetch.playerName);
                                    mw.Write<string>(selectedPMChannel);
                                    mw.Write<string>(sendText);
                                    NetworkWorker.fetch.SendChatMessage(mw.GetMessageBytes());
                                }
                            }
                        }
                        else
                        {
                            //Handle command
                            if (sendText.StartsWith("/join ") && sendText.Length > 7)
                            {
                                string channelName = sendText.Substring(6);
                                if (channelName != "" || channelName != "Global")
                                {
                                    DarkLog.Debug("Joining channel " + channelName);
                                    joinedChannels.Add(channelName);
                                    selectedChannel = channelName;
                                    selectedPMChannel = null;
                                    using (MessageWriter mw = new MessageWriter())
                                    {
                                        mw.Write<int>((int)ChatMessageType.JOIN);
                                        mw.Write<string>(Settings.fetch.playerName);
                                        mw.Write<string>(channelName);
                                        NetworkWorker.fetch.SendChatMessage(mw.GetMessageBytes());
                                    }
                                }
                                else
                                {
                                    ScreenMessages.PostScreenMessage("Couln't join '" + channelName + "', channel name not valid!", 5f, ScreenMessageStyle.UPPER_CENTER);
                                }
                            }
                            if (sendText.StartsWith("/query ") && sendText.Length > 8)
                            {
                                string playerName = sendText.Substring(7);
                                bool playerFound = false;
                                if (playerName != "Server")
                                {
                                    foreach (PlayerStatus ps in PlayerStatusWorker.fetch.playerStatusList)
                                    {
                                        if (ps.playerName == playerName)
                                        {
                                            playerFound = true;
                                        }
                                    }
                                }
                                else
                                {
                                    //Make sure we can always query the server.
                                    playerFound = true;
                                }
                                if (playerFound)
                                {
                                    DarkLog.Debug("Starting query with " + playerName);
                                    joinedPMChannels.Add(playerName);
                                    selectedChannel = null;
                                    selectedPMChannel = playerName;
                                }
                                else
                                {
                                    DarkLog.Debug("Couln't start query with '" + playerName + "', player not found!");
                                    ScreenMessages.PostScreenMessage("Couln't start query with '" + playerName + "', player not found!", 5f, ScreenMessageStyle.UPPER_CENTER);
                                }
                               
                            }
                            if (sendText == "/part" || sendText == "/leave")
                            {
                                leaveEventHandled = false;
                            }
                            if (sendText == "/ping")
                            {
                                NetworkWorker.fetch.SendPingRequest();
                            }
                        }
                    }
                    sendText = "";
                    sendEventHandled = true;
                }
                //Handle join messages
                while (newJoinMessages.Count > 0)
                {
                    JoinLeaveMessage jlm = newJoinMessages.Dequeue();
                    if (!playerChannels.ContainsKey(jlm.fromPlayer))
                    {
                        playerChannels.Add(jlm.fromPlayer, new List<string>());
                    }
                    if (!playerChannels[jlm.fromPlayer].Contains(jlm.channel))
                    {
                        playerChannels[jlm.fromPlayer].Add(jlm.channel);
                    }
                }
                //Handle leave messages
                while (newLeaveMessages.Count > 0)
                {
                    JoinLeaveMessage jlm = newLeaveMessages.Dequeue();
                    if (playerChannels.ContainsKey(jlm.fromPlayer))
                    {
                        if (playerChannels[jlm.fromPlayer].Contains(jlm.channel))
                        {
                            playerChannels[jlm.fromPlayer].Remove(jlm.channel);
                        }
                        if (playerChannels[jlm.fromPlayer].Count == 0)
                        {
                            playerChannels.Remove(jlm.fromPlayer);
                        }
                    }
                }
                //Handle channel messages
                while (newChannelMessages.Count > 0)
                {
                    ChannelEntry ce = newChannelMessages.Dequeue();
                    if (!channelMessages.ContainsKey(ce.channel))
                    {
                        channelMessages.Add(ce.channel, new List<string>());
                    }
                    //Highlight if the channel isn't selected.
                    if (selectedChannel != null && ce.channel == "")
                    {
                        if (!highlightChannel.Contains(ce.channel))
                        {
                            highlightChannel.Add(ce.channel);
                        }
                    }
                    if (ce.channel != selectedChannel && ce.channel != "")
                    {
                        if (!highlightChannel.Contains(ce.channel))
                        {
                            highlightChannel.Add(ce.channel);
                        }
                    }
                    //Move the bar to the bottom on a new message
                    if (selectedChannel == null && selectedPMChannel == null && ce.channel == "")
                    {
                        chatScrollPos.y = float.PositiveInfinity;
                    }
                    if (selectedChannel != null && selectedPMChannel == null && ce.channel == selectedChannel)
                    {
                        chatScrollPos.y = float.PositiveInfinity;
                    }
                    channelMessages[ce.channel].Add(ce.fromPlayer + ": " + ce.message);
                }
                //Handle private messages
                while (newPrivateMessages.Count > 0)
                {
                    PrivateEntry pe = newPrivateMessages.Dequeue();
                    if (pe.fromPlayer != Settings.fetch.playerName)
                    {
                        if (!privateMessages.ContainsKey(pe.fromPlayer))
                        {
                            privateMessages.Add(pe.fromPlayer, new List<string>());
                        }
                        //Highlight if the player isn't selected
                        if (!joinedPMChannels.Contains(pe.fromPlayer))
                        {
                            joinedPMChannels.Add(pe.fromPlayer);
                        }
                        if (selectedPMChannel != pe.fromPlayer)
                        {
                            if (!highlightPM.Contains(pe.fromPlayer))
                            {
                                highlightPM.Add(pe.fromPlayer);
                            }
                        }
                    }
                    //Move the bar to the bottom on a new message
                    if (selectedPMChannel != null && selectedChannel == null && (pe.fromPlayer == selectedPMChannel || pe.fromPlayer == Settings.fetch.playerName))
                    {
                        chatScrollPos.y = float.PositiveInfinity;
                    }
                    if (pe.fromPlayer != Settings.fetch.playerName)
                    {
                        privateMessages[pe.fromPlayer].Add(pe.fromPlayer + ": " + pe.message);
                    }
                    else
                    {
                        privateMessages[pe.toPlayer].Add(pe.fromPlayer + ": " + pe.message);
                    }
                }
                while (disconnectingPlayers.Count > 0)
                {
                    string disconnectingPlayer = disconnectingPlayers.Dequeue();
                    if (playerChannels.ContainsKey(disconnectingPlayer))
                    {
                        playerChannels.Remove(disconnectingPlayer);
                    }
                    if (joinedPMChannels.Contains(disconnectingPlayer))
                    {
                        joinedPMChannels.Remove(disconnectingPlayer);
                    }
                    if (highlightPM.Contains(disconnectingPlayer))
                    {
                        highlightPM.Remove(disconnectingPlayer);
                    }
                    if (privateMessages.ContainsKey(disconnectingPlayer))
                    {
                        privateMessages.Remove(disconnectingPlayer);
                    }
                }
            }
        }

        public void Draw()
        {
            if (!initialized)
            {
                InitGUI();
                initialized = true;
            }
            if (safeDisplay)
            {
                bool pressedChatShortcutKey = (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.BackQuote);
                if (pressedChatShortcutKey)
                {
                    ignoreChatInput = true;
                    selectTextBox = true;
                }
                windowRect = GUILayout.Window(GUIUtility.GetControlID(6704, FocusType.Passive), windowRect, DrawContent, "DarkMultiPlayer Chat", windowStyle, windowLayoutOptions);
            }
        }

        private void DrawContent(int windowID)
        {
            bool pressedEnter = (Event.current.type == EventType.KeyDown && !Event.current.shift && Event.current.character == '\n');
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            GUILayout.BeginHorizontal();
            DrawRooms();
            GUILayout.FlexibleSpace();
            if (selectedChannel != null || selectedPMChannel != null)
            {
                if (GUILayout.Button("Leave", buttonStyle))
                {
                    leaveEventHandled = false;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            chatScrollPos = GUILayout.BeginScrollView(chatScrollPos, scrollStyle);
            if (selectedChannel == null && selectedPMChannel == null)
            {
                if (!channelMessages.ContainsKey(""))
                {
                    channelMessages.Add("", new List<string>());
                }
                foreach (string channelMessage in channelMessages[""])
                {
                    GUILayout.Label(channelMessage, labelStyle);
                }
            }
            if (selectedChannel != null)
            {
                if (!channelMessages.ContainsKey(selectedChannel))
                {
                    channelMessages.Add(selectedChannel, new List<string>());
                }
                foreach (string channelMessage in channelMessages[selectedChannel])
                {
                    GUILayout.Label(channelMessage, labelStyle);
                }
            }
            if (selectedPMChannel != null)
            {
                if (!privateMessages.ContainsKey(selectedPMChannel))
                {
                    privateMessages.Add(selectedPMChannel, new List<string>());
                }
                foreach (string privateMessage in privateMessages[selectedPMChannel])
                {
                    GUILayout.Label(privateMessage, labelStyle);
                }
            }
            GUILayout.EndScrollView();
            playerScrollPos = GUILayout.BeginScrollView(playerScrollPos, scrollStyle, smallSizeOption);
            GUILayout.BeginVertical();
            GUILayout.Label(Settings.fetch.playerName, labelStyle);
            if (selectedPMChannel != null)
            {
                GUILayout.Label(selectedPMChannel, labelStyle);
            }
            else
            {
                if (selectedChannel == null)
                {
                    //Global chat
                    foreach (PlayerStatus player in PlayerStatusWorker.fetch.playerStatusList)
                    {
                        if (joinedPMChannels.Contains(player.playerName))
                        {
                            GUI.enabled = false;
                        }
                        if (GUILayout.Button(player.playerName, labelStyle))
                        {
                            if (!joinedPMChannels.Contains(player.playerName))
                            {
                                joinedPMChannels.Add(player.playerName);
                            }
                        }
                        GUI.enabled = true;
                    }
                }
                else
                {
                    foreach (KeyValuePair<string, List<string>> playerEntry in playerChannels)
                    {
                        if (playerEntry.Key != Settings.fetch.playerName)
                        {
                            if (playerEntry.Value.Contains(selectedChannel))
                            {
                                if (joinedPMChannels.Contains(playerEntry.Key))
                                {
                                    GUI.enabled = false;
                                }
                                if (GUILayout.Button(playerEntry.Key, labelStyle))
                                {
                                    if (!joinedPMChannels.Contains(playerEntry.Key))
                                    {
                                        joinedPMChannels.Add(playerEntry.Key);
                                    }
                                }
                                GUI.enabled = true;
                            }
                        }
                    }
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("SendTextArea");
            string tempSendText = GUILayout.TextArea(sendText, textAreaStyle);
            //Don't add the newline to the messages, queue a send
            if (!ignoreChatInput)
            {
                if (pressedEnter)
                {
                    sendEventHandled = false;
                }
                else
                {
                    sendText = tempSendText;
                }
            }
            if (sendText == "")
            {
                GUI.enabled = false;
            }
            if (GUILayout.Button("Send", buttonStyle, smallSizeOption))
            {
                sendEventHandled = false;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            if ((GUI.GetNameOfFocusedControl() == "SendTextArea") && !chatLocked)
            {
                chatLocked = true;
                InputLockManager.SetControlLock(BLOCK_ALL_CONTROLS, DMP_CHAT_LOCK);
            }
            if ((GUI.GetNameOfFocusedControl() != "SendTextArea") && chatLocked)
            {
                chatLocked = false;
                InputLockManager.RemoveControlLock(DMP_CHAT_LOCK);
            }
            if (selectTextBox)
            {
                selectTextBox = false;
                GUI.FocusControl("SendTextArea");
            }
        }

        private void DrawRooms()
        {
            GUIStyle possibleHighlightButtonStyle = buttonStyle;
            if (selectedChannel == null && selectedPMChannel == null)
            {
                GUI.enabled = false;
            }
            if (highlightChannel.Contains(""))
            {
                possibleHighlightButtonStyle = highlightStyle;
            }
            else
            {
                possibleHighlightButtonStyle = buttonStyle;
            }
            if (GUILayout.Button("#Global", possibleHighlightButtonStyle))
            {
                if (highlightChannel.Contains(""))
                {
                    highlightChannel.Remove("");
                }
                selectedChannel = null;
                selectedPMChannel = null;
                chatScrollPos.y = float.PositiveInfinity;
            }
            GUI.enabled = true;
            foreach (string joinedChannel in joinedChannels)
            {
                if (highlightChannel.Contains(joinedChannel))
                {
                    possibleHighlightButtonStyle = highlightStyle;
                }
                else
                {
                    possibleHighlightButtonStyle = buttonStyle;
                }
                if (selectedChannel == joinedChannel)
                {
                    GUI.enabled = false;
                }
                if (GUILayout.Button("#" + joinedChannel, possibleHighlightButtonStyle))
                {
                    if (highlightChannel.Contains(joinedChannel))
                    {
                        highlightChannel.Remove(joinedChannel);
                    }
                    selectedChannel = joinedChannel;
                    selectedPMChannel = null;
                    chatScrollPos.y = float.PositiveInfinity;
                }
                GUI.enabled = true;
            }

            foreach (string joinedPlayer in joinedPMChannels)
            {
                if (highlightPM.Contains(joinedPlayer))
                {
                    possibleHighlightButtonStyle = highlightStyle;
                }
                else
                {
                    possibleHighlightButtonStyle = buttonStyle;
                }
                if (selectedPMChannel == joinedPlayer)
                {
                    GUI.enabled = false;
                }
                if (GUILayout.Button("@" + joinedPlayer, possibleHighlightButtonStyle))
                {
                    if (highlightPM.Contains(joinedPlayer))
                    {
                        highlightPM.Remove(joinedPlayer);
                    }
                    selectedChannel = null;
                    selectedPMChannel = joinedPlayer;
                    chatScrollPos.y = float.PositiveInfinity;
                }
                GUI.enabled = true;
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
                    Client.drawEvent.Remove(singleton.Draw);
                }
                singleton = new ChatWorker();
                Client.updateEvent.Add(singleton.Update);
                Client.drawEvent.Add(singleton.Draw);
            }
        }
    }

    public class ChannelEntry
    {
        public string fromPlayer;
        public string channel;
        public string message;
    }

    public class PrivateEntry
    {
        public string fromPlayer;
        public string toPlayer;
        public string message;
    }

    public class JoinLeaveMessage
    {
        public string fromPlayer;
        public string channel;
    }
}

