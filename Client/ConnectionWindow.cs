using System;
using DarkMultiPlayerCommon;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class ConnectionWindow
    {
        public bool display = false;
        public bool connectEventHandled = true;
        public bool disconnectEventHandled = true;
        public bool addEventHandled = true;
        public bool editEventHandled = true;
        public bool removeEventHandled = true;
        public bool renameEventHandled = true;
        public bool addingServer = false;
        public bool addingServerSafe = false;
        public int selected = -1;
        private int selectedSafe = -1;
        private string status = "";
        public ServerEntry addEntry = null;
        public ServerEntry editEntry = null;
        //private parts
        private static ConnectionWindow singleton = new ConnectionWindow();
        private bool initialized;
        //Add window
        private string serverName = "Local";
        private string serverAddress = "127.0.0.1";
        private string serverPort = "6702";
        //GUI Layout
        private Rect windowRect;
        private Rect moveRect;
        private GUILayoutOption[] labelOptions;
        private GUILayoutOption[] layoutOptions;
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle textAreaStyle;
        private GUIStyle statusStyle;
        private Vector2 scrollPos;
        //const
        private const float WINDOW_HEIGHT = 400;
        private const float WINDOW_WIDTH = 400;
        //version
        private string version()
        {
            if (Common.PROGRAM_VERSION.Length == 40)
            {
                return "build " + Common.PROGRAM_VERSION.Substring(0, 7);
            }
            return Common.PROGRAM_VERSION;
        }

        public ConnectionWindow()
        {
            lock (Client.eventLock)
            {
                Client.updateEvent.Add(this.Update);
                Client.drawEvent.Add(this.Draw);
            }
        }

        public static ConnectionWindow fetch
        {
            get
            {
                return singleton;
            }
        }

        private void Update()
        {
            status = Client.fetch.status;
            selectedSafe = selected;
            addingServerSafe = addingServer;
            display = (HighLogic.LoadedScene == GameScenes.MAINMENU);
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width * 0.9f - WINDOW_WIDTH, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);

            windowStyle = new GUIStyle(GUI.skin.window);
            textAreaStyle = new GUIStyle(GUI.skin.textArea);
            buttonStyle = new GUIStyle(GUI.skin.button);
            //buttonStyle.fontSize = 10;
            statusStyle = new GUIStyle(GUI.skin.label);
            //statusStyle.fontSize = 10;
            statusStyle.normal.textColor = Color.yellow;

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.MinWidth(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.MaxWidth(WINDOW_WIDTH);
            layoutOptions[2] = GUILayout.MinHeight(WINDOW_HEIGHT);
            layoutOptions[3] = GUILayout.MaxHeight(WINDOW_HEIGHT);

            labelOptions = new GUILayoutOption[1];
            labelOptions[0] = GUILayout.Width(100);
        }

        private void Draw()
        {
            if (!initialized)
            {
                initialized = true;
                InitGUI();
            }
            if (display)
            {
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6702 + Client.WINDOW_OFFSET, windowRect, DrawContent, "DarkMultiPlayer " + version(), windowStyle, layoutOptions));
            }
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            GUILayout.Space(20);
            GUILayout.BeginHorizontal();
            GUILayout.Label(LanguageWorker.fetch.GetString("playerNameLabel"), labelOptions);
            string oldPlayerName = Settings.fetch.playerName;
            Settings.fetch.playerName = GUILayout.TextArea(Settings.fetch.playerName, 32, textAreaStyle); // Max 32 characters
            if (oldPlayerName != Settings.fetch.playerName)
            {
                renameEventHandled = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            //Draw add button
            string addMode = selectedSafe == -1 ? LanguageWorker.fetch.GetString("addServer") : LanguageWorker.fetch.GetString("editServer");
            string buttonAddMode = addMode;
            if (addingServer)
            {
                buttonAddMode = LanguageWorker.fetch.GetString("cancelBtn");
            }
            addingServer = GUILayout.Toggle(addingServer, buttonAddMode, buttonStyle);
            if (addingServer && !addingServerSafe)
            {
                if (selected != -1)
                {
                    //Load the existing server settings
                    serverName = Settings.fetch.servers[selected].name;
                    serverAddress = Settings.fetch.servers[selected].address;
                    serverPort = Settings.fetch.servers[selected].port.ToString();
                }
            }

            //Draw connect button
            if (NetworkWorker.fetch.state == DarkMultiPlayerCommon.ClientState.DISCONNECTED)
            {
                GUI.enabled = (selectedSafe != -1);
                if (GUILayout.Button(LanguageWorker.fetch.GetString("connectBtn"), buttonStyle))
                {
                    connectEventHandled = false;
                }
            }
            else
            {
                if (GUILayout.Button(LanguageWorker.fetch.GetString("disconnectBtn"), buttonStyle))
                {
                    disconnectEventHandled = false;
                }
            }
            //Draw remove button
            if (GUILayout.Button(LanguageWorker.fetch.GetString("removeBtn"), buttonStyle))
            {
                if (removeEventHandled == true)
                {
                    removeEventHandled = false;
                }
            }
            GUI.enabled = true;
            OptionsWindow.fetch.display = GUILayout.Toggle(OptionsWindow.fetch.display, LanguageWorker.fetch.GetString("options"), buttonStyle);
            GUILayout.EndHorizontal();
            if (addingServerSafe)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(LanguageWorker.fetch.GetString("serverNameLabel"), labelOptions);
                serverName = GUILayout.TextArea(serverName, textAreaStyle);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(LanguageWorker.fetch.GetString("serverAddressLabel"), labelOptions);
                serverAddress = GUILayout.TextArea(serverAddress, textAreaStyle).Trim();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(LanguageWorker.fetch.GetString("serverPortLabel"), labelOptions);
                serverPort = GUILayout.TextArea(serverPort, textAreaStyle).Trim();
                GUILayout.EndHorizontal();
                if (GUILayout.Button(addMode + " " + LanguageWorker.fetch.GetString("serverAddEdit"), buttonStyle))
                {
                    if (addEventHandled == true)
                    {
                        if (selected == -1)
                        {
                            addEntry = new ServerEntry();
                            addEntry.name = serverName;
                            addEntry.address = serverAddress;
                            addEntry.port = 6702;
                            Int32.TryParse(serverPort, out addEntry.port);
                            addEventHandled = false;
                        }
                        else
                        {
                            editEntry = new ServerEntry();
                            editEntry.name = serverName;
                            editEntry.address = serverAddress;
                            editEntry.port = 6702;
                            Int32.TryParse(serverPort, out editEntry.port);
                            editEventHandled = false;
                        }
                    }
                }
            }

            GUILayout.Label(LanguageWorker.fetch.GetString("serversLabel"));
            if (Settings.fetch.servers.Count == 0)
            {
                GUILayout.Label(LanguageWorker.fetch.GetString("noServers"));
            }

            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Width(WINDOW_WIDTH - 5), GUILayout.Height(WINDOW_HEIGHT - 100));

            for (int serverPos = 0; serverPos < Settings.fetch.servers.Count; serverPos++)
            {
                bool thisSelected = GUILayout.Toggle(serverPos == selectedSafe, Settings.fetch.servers[serverPos].name, buttonStyle);
                if (selected == selectedSafe)
                {
                    if (thisSelected)
                    {
                        if (selected != serverPos)
                        {
                            selected = serverPos;
                            addingServer = false;
                        }
                    }
                    else if (selected == serverPos)
                    {
                        selected = -1;
                        addingServer = false;
                    }
                }
            }
            GUILayout.EndScrollView();

            //Draw status message
            GUILayout.Label(status, statusStyle);
            GUILayout.EndVertical();
        }
    }
}

