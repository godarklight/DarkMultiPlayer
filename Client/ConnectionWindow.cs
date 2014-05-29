using System;
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
        private GUILayoutOption[] layoutOptions;
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle textAreaStyle;
        private GUIStyle statusStyle;
        //const
        private const float WINDOW_HEIGHT = 200;
        private const float WINDOW_WIDTH = 400;

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
                GUILayout.Window(GUIUtility.GetControlID(6702, FocusType.Passive), windowRect, DrawContent, "DarkMultiPlayer", windowStyle, layoutOptions);
            }
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Player name:");
            string oldPlayerName = Settings.fetch.playerName;
            Settings.fetch.playerName = GUILayout.TextArea(Settings.fetch.playerName, textAreaStyle);
            if (Settings.fetch.playerName.Length > 32)
            {
                Settings.fetch.playerName = Settings.fetch.playerName.Substring(0, 32);
            }
            if (oldPlayerName != Settings.fetch.playerName)
            {
                renameEventHandled = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            //Draw add button
            string addMode = selectedSafe == -1 ? "Add" : "Edit";
            addingServer = GUILayout.Toggle(addingServer, addMode, buttonStyle);
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
                if (GUILayout.Button("Connect", buttonStyle))
                {
                    connectEventHandled = false;
                }
            }
            else
            {
                if (GUILayout.Button("Disconnect", buttonStyle))
                {
                    disconnectEventHandled = false;
                }
            }
            //Draw remove button
            if (GUILayout.Button("Remove", buttonStyle))
            {
                if (removeEventHandled == true)
                {
                    removeEventHandled = false;
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (addingServerSafe)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:");
                serverName = GUILayout.TextArea(serverName, textAreaStyle);
                GUILayout.Label("Address:");
                serverAddress = GUILayout.TextArea(serverAddress, textAreaStyle);
                GUILayout.Label("Port:");
                serverPort = GUILayout.TextArea(serverPort, textAreaStyle);
                GUILayout.EndHorizontal();
                if (GUILayout.Button(addMode + " server", buttonStyle))
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

            GUILayout.Label("Servers:");
            if (Settings.fetch.servers.Count == 0)
            {
                GUILayout.Label("(None - Add a server first)");
            }

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
            GUILayout.FlexibleSpace();

            //Draw status message
            GUILayout.Label(status, statusStyle);
            GUILayout.EndVertical();
        }
    }
}

