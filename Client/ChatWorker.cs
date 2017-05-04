using System;
using System.Collections.Generic;
using UnityEngine;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayer
{
    public class ChatWorker
    {
        public bool display = false;
        public bool workerEnabled = false;
        private bool isWindowLocked = false;
        private bool safeDisplay = false;
        private bool initialized = false;
        //State tracking
        private Queue<string> disconnectingPlayers = new Queue<string>();
        private Queue<JoinLeaveMessage> newJoinMessages = new Queue<JoinLeaveMessage>();
        private Queue<JoinLeaveMessage> newLeaveMessages = new Queue<JoinLeaveMessage>();
        private Queue<ChannelEntry> newChannelMessages = new Queue<ChannelEntry>();
        private Queue<PrivateEntry> newPrivateMessages = new Queue<PrivateEntry>();
        private Queue<ConsoleEntry> newConsoleMessages = new Queue<ConsoleEntry>();
        private Dictionary<string, List<string>> channelMessages = new Dictionary<string, List<string>>();
        private Dictionary<string, List<string>> privateMessages = new Dictionary<string, List<string>>();
        private List<string> consoleMessages = new List<string>();
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
        private int previousTextID = 0;
        private string sendText = "";
        public string consoleIdentifier = "";
        //chat command register
        private Dictionary<string, ChatCommand> registeredChatCommands = new Dictionary<string, ChatCommand>();
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
        //window size
        private float WINDOW_HEIGHT = 300;
        private float WINDOW_WIDTH = 400;
        //const
        private const string DMP_CHAT_LOCK = "DMP_ChatLock";
        private const string DMP_CHAT_WINDOW_LOCK = "DMP_Chat_Window_Lock";
        //Services
        private DMPGame dmpGame;
        private Settings dmpSettings;
        private NetworkWorker networkWorker;
        private AdminSystem adminSystem;
        private PlayerStatusWorker playerStatusWorker;

        public ChatWorker(DMPGame dmpGame, Settings dmpSettings, NetworkWorker networkWorker, AdminSystem adminSystem, PlayerStatusWorker playerStatusWorker)
        {
            this.dmpGame = dmpGame;
            this.dmpSettings = dmpSettings;
            this.networkWorker = networkWorker;
            this.adminSystem = adminSystem;
            this.playerStatusWorker = playerStatusWorker;
            RegisterChatCommand("help", DisplayHelp, "Displays this help");
            RegisterChatCommand("join", JoinChannel, "Joins a new chat channel");
            RegisterChatCommand("query", StartQuery, "Starts a query");
            RegisterChatCommand("leave", LeaveChannel, "Leaves the current channel");
            RegisterChatCommand("part", LeaveChannel, "Leaves the current channel");
            RegisterChatCommand("ping", ServerPing, "Pings the server");
            RegisterChatCommand("motd", ServerMOTD, "Gets the current Message of the Day");
            RegisterChatCommand("resize", ResizeChat, "Resized the chat window");
            RegisterChatCommand("version", DisplayVersion, "Displays the current version of DMP");
            dmpGame.updateEvent.Add(Update);
            dmpGame.drawEvent.Add(Draw);
        }

        private void PrintToSelectedChannel(string text)
        {
            if (selectedChannel == null && selectedPMChannel == null)
            {
                QueueChannelMessage(dmpSettings.playerName, "", text);
            }
            if (selectedChannel != null && selectedChannel != consoleIdentifier)
            {
                QueueChannelMessage(dmpSettings.playerName, selectedChannel, text);
            }
            if (selectedChannel == consoleIdentifier)
            {
                QueueSystemMessage(text);
            }
            if (selectedPMChannel != null)
            {
                QueuePrivateMessage(dmpSettings.playerName, selectedPMChannel, text);
            }
        }

        private void DisplayHelp(string commandArgs)
        {
            List<ChatCommand> commands = new List<ChatCommand>();
            int longestName = 0;
            foreach (ChatCommand cmd in registeredChatCommands.Values)
            {
                commands.Add(cmd);
                if (cmd.name.Length > longestName)
                {
                    longestName = cmd.name.Length;
                }
            }
            commands.Sort();
            foreach (ChatCommand cmd in commands)
            {
                string helpText = cmd.name.PadRight(longestName) + " - " + cmd.description;
                PrintToSelectedChannel(helpText);
            }
        }

        private void DisplayVersion(string commandArgs)
        {
            string versionMessage = (Common.PROGRAM_VERSION.Length == 40) ? "DarkMultiPlayer development build " + Common.PROGRAM_VERSION.Substring(0, 7) : "DarkMultiPlayer " + Common.PROGRAM_VERSION;
            PrintToSelectedChannel(versionMessage);
        }

        private void JoinChannel(string commandArgs)
        {
            if (commandArgs != "" && commandArgs != "Global" && commandArgs != consoleIdentifier && commandArgs != "#Global" && commandArgs != "#" + consoleIdentifier)
            {
                if (commandArgs.StartsWith("#"))
                {
                    commandArgs = commandArgs.Substring(1);
                }
                if (!joinedChannels.Contains(commandArgs))
                {
                    DarkLog.Debug("Joining channel " + commandArgs);
                    joinedChannels.Add(commandArgs);
                    selectedChannel = commandArgs;
                    selectedPMChannel = null;
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)ChatMessageType.JOIN);
                        mw.Write<string>(dmpSettings.playerName);
                        mw.Write<string>(commandArgs);
                        networkWorker.SendChatMessage(mw.GetMessageBytes());
                    }
                }
            }
            else
            {
                ScreenMessages.PostScreenMessage("Couldn't join '" + commandArgs + "', channel name not valid!");
            }
        }

        private void LeaveChannel(string commandArgs)
        {
            leaveEventHandled = false;
        }

        private void StartQuery(string commandArgs)
        {
            bool playerFound = false;
            if (commandArgs != consoleIdentifier)
            {
                foreach (PlayerStatus ps in playerStatusWorker.playerStatusList)
                {
                    if (ps.playerName == commandArgs)
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
                if (!joinedPMChannels.Contains(commandArgs))
                {
                    DarkLog.Debug("Starting query with " + commandArgs);
                    joinedPMChannels.Add(commandArgs);
                    selectedChannel = null;
                    selectedPMChannel = commandArgs;
                }
            }
            else
            {
                ScreenMessages.PostScreenMessage("Couldn't start query with '" + commandArgs + "', player not found!");
            }
        }

        private void ServerPing(string commandArgs)
        {
            networkWorker.SendPingRequest();
        }

        private void ServerMOTD(string commandArgs)
        {
            networkWorker.SendMotdRequest();
        }

        private void ResizeChat(string commandArgs)
        {
            string func = "";
            float size = 0;

            func = commandArgs;
            if (commandArgs.Contains(" "))
            {
                func = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                if (commandArgs.Substring(func.Length).Contains(" "))
                {
                    try
                    {
                        size = Convert.ToSingle(commandArgs.Substring(func.Length + 1));
                    }
                    catch (FormatException)
                    {
                        PrintToSelectedChannel("Error: " + size + " is not a valid number");
                        size = 400f;
                    }
                }
            }

            switch (func)
            {
                default:
                    PrintToSelectedChannel("Undefined function. Usage: /resize [default|medium|large], /resize [x|y] size, or /resize show");
                    PrintToSelectedChannel("Chat window size is currently: " + WINDOW_WIDTH + "x" + WINDOW_HEIGHT);
                    break;
                case "x":
                    if (size <= 800 && size >= 300)
                    {
                        WINDOW_WIDTH = size;
                        initialized = false;

                        PrintToSelectedChannel("New window size is: " + WINDOW_WIDTH + "x" + WINDOW_HEIGHT);
                    }
                    else
                    {
                        PrintToSelectedChannel("Size is out of range.");
                    }
                    break;
                case "y":
                    if (size <= 800 && size >= 300)
                    {
                        WINDOW_HEIGHT = size;
                        initialized = false;

                        PrintToSelectedChannel("New window size is: " + WINDOW_WIDTH + "x" + WINDOW_HEIGHT);
                    }
                    else
                    {
                        PrintToSelectedChannel("Size is out of range.");
                    }
                    break;
                case "default":
                    WINDOW_HEIGHT = 300;
                    WINDOW_WIDTH = 400;
                    initialized = false;
                    PrintToSelectedChannel("New window size is: " + WINDOW_WIDTH + "x" + WINDOW_HEIGHT);
                    break;
                case "medium":
                    WINDOW_HEIGHT = 600;
                    WINDOW_WIDTH = 600;
                    initialized = false;
                    PrintToSelectedChannel("New window size is: " + WINDOW_WIDTH + "x" + WINDOW_HEIGHT);
                    break;
                case "large":
                    WINDOW_HEIGHT = 800;
                    WINDOW_WIDTH = 800;
                    initialized = false;
                    PrintToSelectedChannel("New window size is: " + WINDOW_WIDTH + "x" + WINDOW_HEIGHT);
                    break;
                case "show":
                    PrintToSelectedChannel("Chat window size is currently: " + WINDOW_WIDTH + "x" + WINDOW_HEIGHT);
                    break;
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
            // Check if any of these is null before doing anything else
            if (!string.IsNullOrEmpty(fromPlayer) && !string.IsNullOrEmpty(channelMessage))
            {
                ChannelEntry ce = new ChannelEntry();
                ce.fromPlayer = fromPlayer;
                ce.channel = channelName;
                ce.message = channelMessage;
                newChannelMessages.Enqueue(ce);
            }
        }

        public void QueuePrivateMessage(string fromPlayer, string toPlayer, string privateMessage)
        {
            PrivateEntry pe = new PrivateEntry();
            pe.fromPlayer = fromPlayer;
            pe.toPlayer = toPlayer;
            pe.message = privateMessage;
            newPrivateMessages.Enqueue(pe);
        }

        public void QueueRemovePlayer(string playerName)
        {
            disconnectingPlayers.Enqueue(playerName);
        }

        public void PMMessageServer(string message)
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.PRIVATE_MESSAGE);
                mw.Write<string>(dmpSettings.playerName);
                mw.Write<string>(consoleIdentifier);
                mw.Write<string>(message);
                networkWorker.SendChatMessage(mw.GetMessageBytes());
            }
        }

        public void QueueSystemMessage(string message)
        {
            ConsoleEntry ce = new ConsoleEntry();
            ce.message = message;
            newConsoleMessages.Enqueue(ce);
        }

        public void RegisterChatCommand(string command, Action<string> func, string description)
        {
            ChatCommand cmd = new ChatCommand(command, func, description);
            if (!registeredChatCommands.ContainsKey(command))
            {
                registeredChatCommands.Add(command, cmd);
            }
        }



        public void HandleChatInput(string input)
        {
            if (!input.StartsWith("/") || input.StartsWith("//"))
            {
                //Handle chat messages
                if (input.StartsWith("//"))
                {
                    input = input.Substring(1);
                }

                if (selectedChannel == null && selectedPMChannel == null)
                {
                    //Sending a global chat message
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                        mw.Write<string>(dmpSettings.playerName);
                        //Global channel name is empty string.
                        mw.Write<string>("");
                        mw.Write<string>(input);
                        networkWorker.SendChatMessage(mw.GetMessageBytes());
                    }
                }
                if (selectedChannel != null && selectedChannel != consoleIdentifier)
                {
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                        mw.Write<string>(dmpSettings.playerName);
                        mw.Write<string>(selectedChannel);
                        mw.Write<string>(input);
                        networkWorker.SendChatMessage(mw.GetMessageBytes());
                    }
                }
                if (selectedChannel == consoleIdentifier)
                {
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)ChatMessageType.CONSOLE_MESSAGE);
                        mw.Write<string>(dmpSettings.playerName);
                        mw.Write<string>(input);
                        networkWorker.SendChatMessage(mw.GetMessageBytes());
                        DarkLog.Debug("Server Command: " + input);
                    }
                }
                if (selectedPMChannel != null)
                {
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)ChatMessageType.PRIVATE_MESSAGE);
                        mw.Write<string>(dmpSettings.playerName);
                        mw.Write<string>(selectedPMChannel);
                        mw.Write<string>(input);
                        networkWorker.SendChatMessage(mw.GetMessageBytes());
                    }
                }
            }
            else
            {
                string commandPart = input.Substring(1);
                string argumentPart = "";
                if (commandPart.Contains(" "))
                {
                    if (commandPart.Length > commandPart.IndexOf(' ') + 1)
                    {
                        argumentPart = commandPart.Substring(commandPart.IndexOf(' ') + 1);
                    }
                    commandPart = commandPart.Substring(0, commandPart.IndexOf(' '));
                }
                if (commandPart.Length > 0)
                {
                    if (registeredChatCommands.ContainsKey(commandPart))
                    {
                        try
                        {
                            DarkLog.Debug("Chat Command: " + input.Substring(1));
                            registeredChatCommands[commandPart].func(argumentPart);
                        }
                        catch (Exception e)
                        {
                            DarkLog.Debug("Error handling chat command " + commandPart + ", Exception " + e);
                            PrintToSelectedChannel("Error handling chat command: " + commandPart);
                        }
                    }
                    else
                    {
                        PrintToSelectedChannel("Unknown chat command: " + commandPart);
                    }
                }
            }
        }

        private void HandleChatEvents()
        {
            //Handle leave event
            if (!leaveEventHandled)
            {
                if (!string.IsNullOrEmpty(selectedChannel) && selectedChannel != consoleIdentifier)
                {
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)ChatMessageType.LEAVE);
                        mw.Write<string>(dmpSettings.playerName);
                        mw.Write<string>(selectedChannel);
                        networkWorker.SendChatMessage(mw.GetMessageBytes());
                    }

                    if (joinedChannels.Contains(selectedChannel))
                    {
                        joinedChannels.Remove(selectedChannel);
                    }

                    selectedChannel = null;
                    selectedPMChannel = null;
                }
                if (!string.IsNullOrEmpty(selectedPMChannel))
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
                    HandleChatInput(sendText);
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

                // Write message to screen if chat window is disabled
                if (!display)
                {
                    chatButtonHighlighted = ce.fromPlayer != consoleIdentifier;

                    if (!string.IsNullOrEmpty(ce.channel))
                        ScreenMessages.PostScreenMessage(ce.fromPlayer + " -> #" + ce.channel + ": " + ce.message, 5f, ScreenMessageStyle.UPPER_LEFT);
                    else
                        ScreenMessages.PostScreenMessage(ce.fromPlayer + " -> #Global : " + ce.message, 5f, ScreenMessageStyle.UPPER_LEFT);
                }

                //Highlight if the channel isn't selected.
                if (!string.IsNullOrEmpty(selectedChannel) && string.IsNullOrEmpty(ce.channel) && ce.fromPlayer != consoleIdentifier)
                {
                    if (!highlightChannel.Contains(ce.channel))
                        highlightChannel.Add(ce.channel);
                }

                if (!string.IsNullOrEmpty(ce.channel) && ce.channel != selectedChannel)
                {
                    if (!highlightChannel.Contains(ce.channel))
                        highlightChannel.Add(ce.channel);
                }

                //Move the bar to the bottom on a new message
                if (string.IsNullOrEmpty(selectedChannel) && string.IsNullOrEmpty(selectedPMChannel) && string.IsNullOrEmpty(ce.channel))
                {
                    chatScrollPos.y = float.PositiveInfinity;
                    selectTextBox = chatLocked;
                }

                if (!string.IsNullOrEmpty(selectedChannel) && string.IsNullOrEmpty(selectedPMChannel) && ce.channel == selectedChannel)
                {
                    chatScrollPos.y = float.PositiveInfinity;
                    selectTextBox = chatLocked;
                }

                channelMessages[ce.channel].Add(ce.fromPlayer + ": " + ce.message);
            }
            //Handle private messages
            while (newPrivateMessages.Count > 0)
            {
                PrivateEntry pe = newPrivateMessages.Dequeue();
                if (pe.fromPlayer != dmpSettings.playerName)
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

                if (!privateMessages.ContainsKey(pe.toPlayer))
                {
                    privateMessages.Add(pe.toPlayer, new List<string>());
                }

                //Move the bar to the bottom on a new message
                if (!string.IsNullOrEmpty(selectedPMChannel) && string.IsNullOrEmpty(selectedChannel) && (pe.fromPlayer == selectedPMChannel || pe.fromPlayer == dmpSettings.playerName))
                {
                    chatScrollPos.y = float.PositiveInfinity;
                    selectTextBox = chatLocked;
                }

                if (pe.fromPlayer != dmpSettings.playerName)
                {
                    privateMessages[pe.fromPlayer].Add(pe.fromPlayer + ": " + pe.message);

                    if (!display)
                    {
                        chatButtonHighlighted = true;
                        ScreenMessages.PostScreenMessage(pe.fromPlayer + " -> @" + pe.toPlayer + ": " + pe.message, 5f, ScreenMessageStyle.UPPER_LEFT);
                    }
                }
                else
                {
                    privateMessages[pe.toPlayer].Add(pe.fromPlayer + ": " + pe.message);
                }
            }
            //Handle console messages
            while (newConsoleMessages.Count > 0)
            {
                ConsoleEntry ce = newConsoleMessages.Dequeue();
                //Highlight if the channel isn't selected.
                if (selectedChannel != consoleIdentifier)
                {
                    if (!highlightChannel.Contains(consoleIdentifier))
                    {
                        highlightChannel.Add(consoleIdentifier);
                    }
                }

                //Move the bar to the bottom on a new message
                if (!string.IsNullOrEmpty(selectedChannel) && string.IsNullOrEmpty(selectedPMChannel) && consoleIdentifier == selectedChannel)
                {
                    chatScrollPos.y = float.PositiveInfinity;
                    selectTextBox = chatLocked;
                }

                consoleMessages.Add(ce.message);
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
                HandleChatEvents();
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
                bool pressedChatShortcutKey = (Event.current.type == EventType.KeyDown && Event.current.keyCode == dmpSettings.chatKey);
                if (pressedChatShortcutKey)
                {
                    ignoreChatInput = true;
                    selectTextBox = true;
                }
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6704 + Client.WINDOW_OFFSET, windowRect, DrawContent, "DarkMultiPlayer Chat", windowStyle, windowLayoutOptions));
            }
            CheckWindowLock();
        }

        private void DrawContent(int windowID)
        {
            bool pressedEnter = (Event.current.type == EventType.KeyDown && !Event.current.shift && Event.current.character == '\n');
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            GUILayout.BeginHorizontal();
            DrawRooms();
            GUILayout.FlexibleSpace();
            if (selectedChannel != null && selectedChannel != consoleIdentifier || selectedPMChannel != null)
            {
                if (GUILayout.Button("Leave", buttonStyle))
                {
                    leaveEventHandled = false;
                }
            }
            DrawConsole();
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
            if (selectedChannel != null && selectedChannel != consoleIdentifier)
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
            if (selectedChannel == consoleIdentifier)
            {
                foreach (string consoleMessage in consoleMessages)
                {
                    GUILayout.Label(consoleMessage, labelStyle);
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
            GUILayout.Label(dmpSettings.playerName, labelStyle);
            if (selectedPMChannel != null)
            {
                GUILayout.Label(selectedPMChannel, labelStyle);
            }
            else
            {
                if (selectedChannel == null)
                {
                    //Global chat
                    foreach (PlayerStatus player in playerStatusWorker.playerStatusList)
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
                        if (playerEntry.Key != dmpSettings.playerName)
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
            //When a control is inserted or removed from the GUI, Unity's focusing starts tripping balls. This is a horrible workaround for unity that shouldn't exist...
            int newTextID = GUIUtility.GetControlID(FocusType.Keyboard);
            if (previousTextID != newTextID)
            {
                previousTextID = newTextID;
                if (chatLocked)
                {
                    selectTextBox = true;
                }
            }
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
            if (!selectTextBox)
            {
                if ((GUI.GetNameOfFocusedControl() == "SendTextArea") && !chatLocked)
                {
                    chatLocked = true;
                    InputLockManager.SetControlLock(DMPGuiUtil.BLOCK_ALL_CONTROLS, DMP_CHAT_LOCK);
                }
                if ((GUI.GetNameOfFocusedControl() != "SendTextArea") && chatLocked)
                {
                    chatLocked = false;
                    InputLockManager.RemoveControlLock(DMP_CHAT_LOCK);
                }
            }
            else
            {
                selectTextBox = false;
                GUI.FocusControl("SendTextArea");
            }
        }

        private void DrawConsole()
        {
            GUIStyle possibleHighlightButtonStyle = buttonStyle;
            if (selectedChannel == consoleIdentifier)
            {
                GUI.enabled = false;
            }
            if (highlightChannel.Contains(consoleIdentifier))
            {
                possibleHighlightButtonStyle = highlightStyle;
            }
            else
            {
                possibleHighlightButtonStyle = buttonStyle;
            }
            if (adminSystem.IsAdmin(dmpSettings.playerName))
            {
                if (GUILayout.Button("#" + consoleIdentifier, possibleHighlightButtonStyle))
                {
                    if (highlightChannel.Contains(consoleIdentifier))
                    {
                        highlightChannel.Remove(consoleIdentifier);
                    }
                    selectedChannel = consoleIdentifier;
                    selectedPMChannel = null;
                    chatScrollPos.y = float.PositiveInfinity;
                }
            }
            GUI.enabled = true;
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

        public void Stop()
        {
            workerEnabled = false;
            RemoveWindowLock();
            dmpGame.updateEvent.Remove(Update);
            dmpGame.drawEvent.Remove(Draw);
            if (chatLocked)
            {
                chatLocked = false;
                InputLockManager.RemoveControlLock(DMP_CHAT_LOCK);
            }
        }


        private void CheckWindowLock()
        {
            if (!dmpGame.running)
            {
                RemoveWindowLock();
                return;
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                RemoveWindowLock();
                return;
            }

            if (safeDisplay)
            {
                Vector2 mousePos = Input.mousePosition;
                mousePos.y = Screen.height - mousePos.y;

                bool shouldLock = windowRect.Contains(mousePos);

                if (shouldLock && !isWindowLocked)
                {
                    InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, DMP_CHAT_WINDOW_LOCK);
                    isWindowLocked = true;
                }
                if (!shouldLock && isWindowLocked)
                {
                    RemoveWindowLock();
                }
            }

            if (!safeDisplay && isWindowLocked)
            {
                RemoveWindowLock();
            }
        }

        private void RemoveWindowLock()
        {
            if (isWindowLocked)
            {
                isWindowLocked = false;
                InputLockManager.RemoveControlLock(DMP_CHAT_WINDOW_LOCK);
            }
        }

        private class ChatCommand : IComparable
        {
            public string name;
            public Action<string> func;
            public string description;

            public ChatCommand(string name, Action<string> func, string description)
            {
                this.name = name;
                this.func = func;
                this.description = description;
            }

            public int CompareTo(object obj)
            {
                var cmd = obj as ChatCommand;
                return this.name.CompareTo(cmd.name);
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

    public class ConsoleEntry
    {
        public string message;
    }
}

