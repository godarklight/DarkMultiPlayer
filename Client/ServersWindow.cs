using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;
using System.Text;

namespace DarkMultiPlayer
{
    public class ServersWindow
    {
        public bool display = false;
        private bool safeDisplay = false;
        private bool initialized = false;
        private bool isWindowLocked = false;
        //private parts
        private float lastUpdateTime = 0;
        private ServerListConnection.ServerListEntry[] servers = new ServerListConnection.ServerListEntry[0];
        private string[] playerCache = new string[0];
        private int addEvent = -1;
        private int connectEvent = -1;
        private int displayedServers = 0;
        string bottomText = "";
        //GUI Layout
        private Rect windowRect;
        private Rect moveRect;
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;
        private GUIStyle greenStyle;
        private GUIStyle addStyle;
        private GUIStyle connectStyle;
        private GUIStyle darkColor;
        private GUIStyle medColor;
        private GUIStyle lightColor;
        //const
        private const float MIN_WINDOW_HEIGHT = 0.2f;
        private const float MAX_WINDOW_HEIGHT = 0.9f;
        private const float WINDOW_WIDTH = 0.9f;
        private const float DISPLAY_UPDATE_INTERVAL = .2f;
        private const float GROUP_HEIGHT_PERCENT = 0.02f;
        private const float GROUP_HEIGHT_SPACING_PERCENT = 0.022f;
        //Services
        private Settings dmpSettings;
        private OptionsWindow optionsWindow;
        private ServerListConnection serverListConnection;
        private StringBuilder stringBuilder = new StringBuilder();
        private Dictionary<int, string> gameModeCache;

        public ServersWindow(Settings dmpSettings, OptionsWindow optionsWindow, ServerListConnection serverListConnection)
        {
            this.dmpSettings = dmpSettings;
            this.optionsWindow = optionsWindow;
            this.serverListConnection = serverListConnection;

        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width * 0.05f, Screen.height * 0.1f, WINDOW_WIDTH * Screen.width, MIN_WINDOW_HEIGHT * Screen.height);
            moveRect = new Rect(0, 0, 10000, 10);
            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);
            labelStyle = new GUIStyle(GUI.skin.label);
            greenStyle = new GUIStyle(GUI.skin.label);
            greenStyle.normal.textColor = Color.green;
            addStyle = new GUIStyle(GUI.skin.button);
            addStyle.stretchWidth = false;
            connectStyle = new GUIStyle(GUI.skin.button);
            connectStyle.stretchWidth = false;

            darkColor = new GUIStyle();
            Texture2D darkTexture = new Texture2D(1, 1);
            darkTexture.SetPixel(0, 0, new Color(0.2f, 0.2f, 0.2f, 0.9f));
            darkTexture.Apply();
            /*
            windowStyle.normal.background = darkTexture;
            windowStyle.active.background = darkTexture;
            windowStyle.hover.background = darkTexture;
            windowStyle.focused.background = darkTexture;
            */           
            darkColor.normal.background = darkTexture;
            darkColor.normal.textColor = Color.white;
            darkColor.padding = new RectOffset(4, 4, 2, 2);
            darkColor.alignment = TextAnchor.MiddleCenter;
            darkColor.fontStyle = FontStyle.Bold;

            medColor = new GUIStyle();
            Texture2D medTexture = new Texture2D(1, 1);
            medTexture.SetPixel(0, 0, new Color(0.25f, 0.25f, 0.25f, 0.9f));
            medTexture.Apply();
            medColor.normal.background = medTexture;
            medColor.normal.textColor = Color.white;
            medColor.padding = new RectOffset(4, 4, 2, 2);
            medColor.alignment = TextAnchor.MiddleCenter;
            medColor.fontStyle = FontStyle.Bold;

            lightColor = new GUIStyle();
            Texture2D lightTexture = new Texture2D(1, 1);
            lightTexture.SetPixel(0, 0, new Color(0.3f, 0.3f, 0.3f, 0.9f));
            lightTexture.Apply();
            lightColor.normal.background = lightTexture;
            lightColor.normal.textColor = Color.white;
            lightColor.padding = new RectOffset(4, 4, 2, 2);
            lightColor.alignment = TextAnchor.MiddleCenter;
            lightColor.fontStyle = FontStyle.Bold;

            gameModeCache = new Dictionary<int, string>();
            gameModeCache.Add(0, "Sandbox");
            gameModeCache.Add(1, "Science");
            gameModeCache.Add(2, "Career");
        }

        public void Draw()
        {
            if (safeDisplay)
            {
                if (!initialized)
                {
                    initialized = true;
                    InitGUI();
                }
                windowRect.width = WINDOW_WIDTH * Screen.width;
                if (servers != null)
                {
                    float groupSpacing = Screen.height * GROUP_HEIGHT_SPACING_PERCENT;
                    if (groupSpacing < 22f)
                    {
                        groupSpacing = 22f;
                    }
                    windowRect.height = (3 + displayedServers) * groupSpacing;
                }
                if (windowRect.height < MIN_WINDOW_HEIGHT * Screen.height)
                {
                    windowRect.height = MIN_WINDOW_HEIGHT * Screen.height;
                }
                if (windowRect.height > MAX_WINDOW_HEIGHT * Screen.height)
                {
                    windowRect.height = MAX_WINDOW_HEIGHT * Screen.height;
                }
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUI.Window(6715 + Client.WINDOW_OFFSET, windowRect, DrawContent, string.Empty, windowStyle));
            }
            CheckWindowLock();
        }

        private void DrawContent(int windowID)
        {
            float windowWidth = windowRect.width;
            float windowHeight = windowRect.height;
            float groupHeight = Screen.height * GROUP_HEIGHT_PERCENT;
            if (groupHeight < 20f)
            {
                groupHeight = 20f;
            }
            float groupSpacing = Screen.height * GROUP_HEIGHT_SPACING_PERCENT;
            if (groupSpacing < 22f)
            {
                groupSpacing = 22f;
            }
            float groupY = windowStyle.border.top;
            GUI.DragWindow(moveRect);
            GUI.Box(new Rect(2f, groupY, windowWidth - 4f, groupSpacing + groupY), string.Empty, darkColor);
            if (GUI.Button(new Rect(windowWidth * 0.97f, 10f, windowWidth * 0.02f, Screen.height * 0.02f), "X"))
            {
                display = false;
            }
            GUI.Label(new Rect(0.085f * windowWidth, groupY, 0.05f * windowWidth, groupHeight), "Country");
            GUI.Label(new Rect(0.12f * windowWidth, groupY, 0.05f * windowWidth, groupHeight), "Players");
            GUI.Label(new Rect(0.165f * windowWidth, groupY, 0.05f * windowWidth, groupHeight), "Mode");
            GUI.Label(new Rect(0.20f * windowWidth, groupY, 0.05f * windowWidth, groupHeight), "Version");
            GUI.Label(new Rect(0.5f * windowWidth, groupY, 0.05f * windowWidth, groupHeight), "Server Name");
            displayedServers = 0;
            for (int i = 0; i < servers.Length; i++)
            {
                ServerListConnection.ServerListEntry sle = servers[i];
                if (sle == null)
                {
                    continue;
                }
                if (sle.protocolVersion != Common.PROTOCOL_VERSION)
                {
                    continue;
                }
                groupY += groupSpacing;
                if (displayedServers % 2 == 0)
                {
                    GUI.Box(new Rect(2f, groupY, windowWidth - 4f, groupSpacing), string.Empty, medColor);
                }
                else
                {
                    GUI.Box(new Rect(2f, groupY, windowWidth - 4f, groupSpacing), string.Empty, lightColor);
                }
                if (GUI.Button(new Rect(0.01f * windowWidth, groupY, 0.025f * windowWidth, groupHeight), "Add"))
                {
                    addEvent = i;
                }
                if (GUI.Button(new Rect(0.04f * windowWidth, groupY, 0.04f * windowWidth, groupHeight), "Connect"))
                {
                    connectEvent = i;
                }
                GUI.Label(new Rect(0.09f * windowWidth, groupY, 0.025f * windowWidth, groupHeight), sle.location);
                if (sle.players != null && sle.players.Length > 0)
                {
                    GUI.contentColor = Color.green;
                }
                GUI.Label(new Rect(0.13f * windowWidth, groupY, 0.025f * windowWidth, groupHeight), playerCache[i]);
                GUI.contentColor = Color.white;
                if (gameModeCache.ContainsKey(sle.gameMode))
                {
                    GUI.Label(new Rect(0.16f * windowWidth, groupY, 0.05f * windowWidth, groupHeight), gameModeCache[sle.gameMode]);
                }
                GUI.Label(new Rect(0.20f * windowWidth, groupY, 0.05f * windowWidth, groupHeight), sle.programVersion);
                GUI.Label(new Rect(0.24f * windowWidth, groupY, 0.71f * windowWidth, groupHeight), sle.serverName);
                displayedServers++;
            }
            GUI.Label(new Rect(0.01f * windowWidth, (windowHeight * 0.99f) - groupHeight, windowWidth * 0.95f, groupHeight), bottomText);
        }

        public void Update()
        {
            if (addEvent != -1)
            {
                ServerEntry serverEntry = new ServerEntry();
                serverEntry.name = servers[addEvent].serverName;
                if (serverEntry.name.Length > 23)
                {
                    serverEntry.name = serverEntry.name.Substring(0, 20) + "...";
                }
                serverEntry.address = servers[addEvent].gameAddress;
                serverEntry.port = servers[addEvent].gamePort;
                dmpSettings.servers.Add(serverEntry);
                dmpSettings.SaveSettings();
                display = false;
                addEvent = -1;
            }
            if (connectEvent != -1)
            {
                Client.dmpClient.ConnectToServer(servers[connectEvent].gameAddress, servers[connectEvent].gamePort);
                connectEvent = -1;
                display = false;
            }
            safeDisplay = display;
            if (safeDisplay)
            {
                if ((Client.realtimeSinceStartup - lastUpdateTime) > DISPLAY_UPDATE_INTERVAL)
                {
                    lastUpdateTime = Client.realtimeSinceStartup;
                    Dictionary<int, ServerListConnection.ServerListEntry> updateServers = serverListConnection.GetServers();
                    int players = 0;
                    int newer = 0;
                    int older = 0;
                    displayedServers = 0;
                    lock (updateServers)
                    {
                        bool isNew = false;
                        if (servers == null || servers.Length != updateServers.Count)
                        {
                            servers = new ServerListConnection.ServerListEntry[updateServers.Count];
                            playerCache = new string[updateServers.Count];
                            isNew = true;
                        }
                        int freeID = 0;
                        foreach (ServerListConnection.ServerListEntry sle in updateServers.Values)
                        {
                            servers[freeID] = sle;
                            if (isNew)
                            {
                                if (sle.location != null)
                                {
                                    sle.location = sle.location.ToUpper();
                                }
                                if (sle.programVersion != null && sle.programVersion.Length > 9)
                                {
                                    sle.programVersion = sle.programVersion.Substring(0, 9);
                                }
                            }
                            if (sle.protocolVersion > Common.PROTOCOL_VERSION)
                            {
                                newer++;
                            }
                            if (sle.protocolVersion < Common.PROTOCOL_VERSION)
                            {
                                older++;
                            }
                            if (sle.protocolVersion == Common.PROTOCOL_VERSION)
                            {
                                displayedServers++;
                            }
                            freeID++;
                        }
                        Array.Sort(servers, ServerSorter);
                        for (int i = 0; i < servers.Length; i++)
                        {
                            ServerListConnection.ServerListEntry sle = servers[i];
                            playerCache[i] = "0";
                            if (sle.players != null)
                            {
                                playerCache[i] = sle.players.Length.ToString();
                                players += sle.players.Length;
                            }
                        }
                    }
                    stringBuilder.Clear();
                    stringBuilder.Append(players);
                    stringBuilder.Append(" players on ");
                    stringBuilder.Append(servers.Length);
                    stringBuilder.Append(" servers.");
                    if (newer > 0)
                    {
                        stringBuilder.Append(" ");
                        stringBuilder.Append(newer);
                        stringBuilder.Append(" incompatible newer servers hidden.");
                    }
                    if (older > 0)
                    {
                        stringBuilder.Append(" ");
                        stringBuilder.Append(older);
                        stringBuilder.Append(" incompatible older servers hidden.");
                    }
                    bottomText = stringBuilder.ToString();
                }
            }
        }

        private void CheckWindowLock()
        {
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
                    InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, "DMP_ServersLock");
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
                InputLockManager.RemoveControlLock("DMP_ServersLock");
            }
        }

        private int ServerSorter(ServerListConnection.ServerListEntry x, ServerListConnection.ServerListEntry y)
        {
            if (x == null && y == null)
            {
                return 0;
            }
            if (x != null && y == null)
            {
                return -1;
            }
            if (x == null && y != null)
            {
                return 1;
            }
            if (x.players != null && x.players.Length > 0 && y.players == null)
            {
                return -1;
            }
            if (x.players == null && y.players != null && y.players.Length > 0)
            {
                return 1;
            }
            if (x.players != null && y.players != null)
            {
                if (x.players.Length != y.players.Length)
                {
                    if (x.players.Length > y.players.Length)
                    {
                        return -1;
                    }
                    else
                    {
                        return 1;
                    }
                }
            }
            if (x.programVersion != y.programVersion)
            {
                return String.Compare(y.programVersion, x.programVersion);
            }
            if (x.serverName != y.serverName)
            {
                return String.Compare(x.serverName, y.serverName);
            }
            return 0;
        }
    }
}

