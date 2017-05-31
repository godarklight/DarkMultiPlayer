using System;
using DarkMultiPlayerCommon;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class ConnectionWindow
    {
        public bool display = false;
        public bool networkWorkerDisconnected = true;
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
        public string status = "";
        public ServerEntry addEntry = null;
        public ServerEntry editEntry = null;
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
        //Services
        Settings dmpSettings;
        OptionsWindow optionsWindow;

        public ConnectionWindow(Settings dmpSettings, OptionsWindow optionsWindow)
        {
            this.dmpSettings = dmpSettings;
            this.optionsWindow = optionsWindow;
        }

        public void Update()
        {
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

        public void Draw()
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
            GUILayout.Label("Player name:", labelOptions);
            string oldPlayerName = dmpSettings.playerName;
            dmpSettings.playerName = GUILayout.TextArea(dmpSettings.playerName, 32, textAreaStyle); // Max 32 characters
            if (oldPlayerName != dmpSettings.playerName)
            {
                dmpSettings.playerName = dmpSettings.playerName.Replace("\n", "");
                renameEventHandled = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            //Draw add button
            string addMode = selectedSafe == -1 ? "Add" : "Edit";
            string buttonAddMode = addMode;
            if (addingServer)
            {
                buttonAddMode = "Cancel";
            }
            addingServer = GUILayout.Toggle(addingServer, buttonAddMode, buttonStyle);
            if (addingServer && !addingServerSafe)
            {
                if (selected != -1)
                {
                    //Load the existing server settings
                    serverName = dmpSettings.servers[selected].name;
                    serverAddress = dmpSettings.servers[selected].address;
                    serverPort = dmpSettings.servers[selected].port.ToString();
                }
            }
            //Draw connect button
            if (networkWorkerDisconnected)
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
            optionsWindow.display = GUILayout.Toggle(optionsWindow.display, "Options", buttonStyle);
            GUILayout.EndHorizontal();
            if (addingServerSafe)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", labelOptions);
                serverName = GUILayout.TextArea(serverName, textAreaStyle);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Address:", labelOptions);
                serverAddress = GUILayout.TextArea(serverAddress, textAreaStyle).Trim();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Port:", labelOptions);
                serverPort = GUILayout.TextArea(serverPort, textAreaStyle).Trim();
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
            if (dmpSettings.servers.Count == 0)
            {
                GUILayout.Label("(None - Add a server first)");
            }

            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Width(WINDOW_WIDTH - 5), GUILayout.Height(WINDOW_HEIGHT - 100));

            for (int serverPos = 0; serverPos < dmpSettings.servers.Count; serverPos++)
            {
                bool thisSelected = GUILayout.Toggle(serverPos == selectedSafe, dmpSettings.servers[serverPos].name, buttonStyle);
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

