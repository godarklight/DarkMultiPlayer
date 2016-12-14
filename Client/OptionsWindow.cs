using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    enum OptionsTab : int
    {
        PLAYER,
        CACHE,
        CONTROLS,
        ADVANCED
    }

    public class OptionsWindow
    {
        private static OptionsWindow singleton = new OptionsWindow();
        public bool loadEventHandled = true;
        public bool display;
        private bool isWindowLocked = false;
        private bool safeDisplay;
        private bool initialized;
        //GUI Layout
        private Rect windowRect;
        private Rect moveRect;
        private GUILayoutOption[] layoutOptions;
        private GUILayoutOption[] smallOption;
        //Styles
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        //const
        private const float WINDOW_HEIGHT = 300;
        private const float WINDOW_WIDTH = 300;
        private const int descWidth = 75;
        private const int sepWidth = 5;
        //TempColour
        private Color tempColor = new Color(1f, 1f, 1f, 1f);
        private GUIStyle tempColorLabelStyle;
        //Cache size
        private string newCacheSize = "";
        //Keybindings
        private bool settingChat;
        private bool settingScreenshot;
        private string settingKeyMessage = "cancel";
        private string toolbarMode;
        // Toolbar
        private GUIStyle toolbarBtnStyle;
        private OptionsTab selectedTab = OptionsTab.PLAYER;
        private string[] optionsTabs = { "Player", "Cache", "Advanced" };
        // New style
        private GUIStyle descriptorStyle;
        private GUIStyle plrNameStyle;
        private GUIStyle textFieldStyle;
        private GUIStyle noteStyle;
        private GUIStyle sectionHeaderStyle;

        public OptionsWindow()
        {
            Client.updateEvent.Add(this.Update);
            Client.drawEvent.Add(this.Draw);
        }

        public static OptionsWindow fetch
        {
            get
            {
                return singleton;
            }
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width / 2f + WINDOW_WIDTH / 2f, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);

            windowStyle = new GUIStyle(GUI.skin.window);

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.Width(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.Height(WINDOW_HEIGHT);
            layoutOptions[2] = GUILayout.ExpandWidth(true);
            layoutOptions[3] = GUILayout.ExpandHeight(true);

            smallOption = new GUILayoutOption[2];
            smallOption[0] = GUILayout.Width(100);
            smallOption[1] = GUILayout.ExpandWidth(false);

            toolbarBtnStyle = new GUIStyle();
            toolbarBtnStyle.alignment = TextAnchor.MiddleCenter;
            toolbarBtnStyle.normal.background = new Texture2D(1, 1);
            toolbarBtnStyle.normal.background.SetPixel(0, 0, Color.black);
            toolbarBtnStyle.normal.background.Apply();
            toolbarBtnStyle.normal.textColor = Color.white;
            toolbarBtnStyle.hover.background = new Texture2D(1, 1);
            toolbarBtnStyle.hover.background.SetPixel(0, 0, Color.grey);
            toolbarBtnStyle.hover.background.Apply();
            toolbarBtnStyle.hover.textColor = Color.white;
            toolbarBtnStyle.padding = new RectOffset(4, 4, 2, 2);

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.padding = new RectOffset(4, 4, 2, 2);

            descriptorStyle = new GUIStyle();
            descriptorStyle.normal.textColor = Color.white;
            descriptorStyle.padding = new RectOffset(4, 4, 2, 2);
            descriptorStyle.alignment = TextAnchor.MiddleRight;

            plrNameStyle = new GUIStyle();
            plrNameStyle.normal.background = new Texture2D(1, 1);
            plrNameStyle.normal.background.SetPixel(0, 0, new Color(0, 0, 0, .54f));
            plrNameStyle.normal.background.Apply();
            plrNameStyle.normal.textColor = Settings.fetch.playerColor;
            plrNameStyle.padding = new RectOffset(4, 4, 2, 2);
            plrNameStyle.alignment = TextAnchor.MiddleLeft;
            plrNameStyle.fontStyle = FontStyle.Bold;

            textFieldStyle = new GUIStyle();
            textFieldStyle.normal.background = new Texture2D(1, 1);
            textFieldStyle.normal.background.SetPixel(0, 0, new Color(0, 0, 0, .54f));
            textFieldStyle.normal.background.Apply();
            textFieldStyle.padding = new RectOffset(4, 4, 2, 2);
            textFieldStyle.normal.textColor = Color.white;

            noteStyle = new GUIStyle();
            noteStyle.normal.textColor = new Color(1, 1, 1, 0.75f);
            noteStyle.fontSize = 12;
            noteStyle.padding = new RectOffset(4, 4, 2, 2);
            noteStyle.alignment = TextAnchor.UpperCenter;
            noteStyle.wordWrap = true;

            sectionHeaderStyle = new GUIStyle();
            Texture2D sectionHeader = new Texture2D(1, 1);
            sectionHeader.SetPixel(0, 0, new Color(0, 0, 0, 0.87f));
            sectionHeader.Apply();
            sectionHeaderStyle.normal.background = sectionHeader;
            sectionHeaderStyle.normal.textColor = Color.white;
            sectionHeaderStyle.padding = new RectOffset(4, 4, 2, 2);
            sectionHeaderStyle.alignment = TextAnchor.MiddleCenter;
            sectionHeaderStyle.fontStyle = FontStyle.Bold;

            tempColor = new Color();
            tempColorLabelStyle = new GUIStyle(GUI.skin.label);
            UpdateToolbarString();
        }

        private void UpdateToolbarString()
        {
            switch (Settings.fetch.toolbarType)
            {
                case DMPToolbarType.DISABLED:
                    toolbarMode = "Toolbar: Disabled";
                    break;
                case DMPToolbarType.FORCE_STOCK:
                    toolbarMode = "Toolbar: Stock";
                    break;
                case DMPToolbarType.BLIZZY_IF_INSTALLED:
                    toolbarMode = "Toolbar: Blizzy's Toolbar";
                    break;
                case DMPToolbarType.BOTH_IF_INSTALLED:
                    toolbarMode = "Toolbar: Both";
                    break;
            }
        }

        private void Update()
        {
            safeDisplay = display;
        }

        private void Draw()
        {
            if (!initialized)
            {
                initialized = true;
                InitGUI();
            }
            if (safeDisplay)
            {
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6711 + Client.WINDOW_OFFSET, windowRect, DrawContent, "DarkMultiPlayer - Options", windowStyle, layoutOptions));
            }
            CheckWindowLock();
        }

        private void DrawContent(int windowID)
        {
            if (!loadEventHandled)
            {
                loadEventHandled = true;
                tempColor = Settings.fetch.playerColor;
                newCacheSize = Settings.fetch.cacheSize.ToString();
            }

            if (GUI.Button(new Rect(windowRect.width - 24, 0, 19, 19), "X"))
            {
                display = false;
            }
            //Player color
            GUI.DragWindow(moveRect);
            GUI.Box(new Rect(2, 20, windowRect.width - 4, 20), string.Empty, sectionHeaderStyle);
            selectedTab = (OptionsTab)GUILayout.Toolbar((int)selectedTab, GetOptionsTabStrings(), toolbarBtnStyle);

            int windowY = 17;
            windowY += 20 + 2;
            int groupY = 0;

            if (selectedTab == OptionsTab.PLAYER)
            {
                GUI.BeginGroup(new Rect(10, windowY, windowRect.width - 20, 106));
                groupY = 0;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Name:", descriptorStyle);
                plrNameStyle.normal.textColor = Settings.fetch.playerColor;
                if (NetworkWorker.fetch.state == DarkMultiPlayerCommon.ClientState.RUNNING)
                    GUI.Label(new Rect(descWidth + sepWidth, groupY,
                        windowRect.width - (descWidth + sepWidth) - 20, 20),
                        Settings.fetch.playerName, plrNameStyle);
                else
                {
                    string newName = GUI.TextField(new Rect(
                        descWidth + sepWidth, 
                        0, 
                        windowRect.width - (descWidth + sepWidth) - 20, 
                        20), Settings.fetch.playerName, plrNameStyle);

                    if (!newName.Equals(Settings.fetch.playerName))
                    {
                        Settings.fetch.playerName = newName;
                        Settings.fetch.SaveSettings();
                    }
                }
                groupY += 20 + 4;


                Color playerColor = Settings.fetch.playerColor;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Red:", descriptorStyle);
                playerColor.r = GUI.HorizontalSlider(new Rect(
                    descWidth + sepWidth, 
                    groupY + 5,
                    windowRect.width - (descWidth + sepWidth) - 20,
                    12
                    ), Settings.fetch.playerColor.r, 0, 1);
                groupY += 20;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Green:", descriptorStyle);
                playerColor.g = GUI.HorizontalSlider(new Rect(
                    descWidth + sepWidth,
                    groupY + 5,
                    windowRect.width - (descWidth + sepWidth) - 20,
                    12
                    ), Settings.fetch.playerColor.g, 0, 1);
                groupY += 20;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Blue:", descriptorStyle);
                playerColor.b = GUI.HorizontalSlider(new Rect(
                    descWidth + sepWidth,
                    groupY + 5,
                    windowRect.width - (descWidth + sepWidth) - 20,
                    12
                    ), Settings.fetch.playerColor.b, 0, 1);
                groupY += 22;

                if (GUI.Button(new Rect(0, groupY, windowRect.width - 20, 20), "Random Color", buttonStyle))
                    playerColor = PlayerColorWorker.GenerateRandomColor();

                if (!playerColor.Equals(Settings.fetch.playerColor))
                {
                    Settings.fetch.playerColor = playerColor;
                    Settings.fetch.SaveSettings();

                    if (NetworkWorker.fetch.state == DarkMultiPlayerCommon.ClientState.RUNNING)
                        PlayerColorWorker.fetch.SendPlayerColorToServer();
                }

                GUI.EndGroup();
               // windowY += 106 + 5;
            }
            if (selectedTab == OptionsTab.CACHE)
            {
                GUI.BeginGroup(new Rect(10, windowY, windowRect.width - 20, 84));
                groupY = 0;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Current:", descriptorStyle);
                GUI.Label(
                    new Rect(descWidth + sepWidth, groupY, windowRect.width - (descWidth + sepWidth) - 102, 20), 
                    Mathf.Round(UniverseSyncCache.fetch.currentCacheSize / 1024).ToString() + " KB");

                groupY += 20;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Maximum:", descriptorStyle);
                string newSizeStr = GUI.TextField(new Rect(descWidth + sepWidth, groupY, windowRect.width - (descWidth + sepWidth) - 152, 20), (Settings.fetch.cacheSize / 1024).ToString(), textFieldStyle);
                GUI.Label(new Rect(descWidth + sepWidth + 80, groupY, 100, 20), "kilobytes (KB)");
                int newSize;
                if (newSizeStr == string.Empty) newSize = 1;
                else
                {
                    if (int.TryParse(newSizeStr, out newSize))
                    {
                        if (newSize < 1) newSize = 1;
                        else if (newSize > 1000000) newSize = 1000000;
                    }
                    else newSize = 100000;
                }

                if (newSize != Settings.fetch.cacheSize)
                {
                    Settings.fetch.cacheSize = newSize * 1024;
                    Settings.fetch.SaveSettings();
                }
                groupY += 22;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Manage:", descriptorStyle);
                if (GUI.Button(new Rect(descWidth + sepWidth, groupY, windowRect.width - (descWidth + sepWidth) - 20, 20), "Expire"))
                    UniverseSyncCache.fetch.ExpireCache();

                groupY += 22;

                if (GUI.Button(new Rect(descWidth + sepWidth, groupY, windowRect.width - (descWidth + sepWidth) - 20, 20), "Delete"))
                    UniverseSyncCache.fetch.DeleteCache();
                GUI.EndGroup();
            }
            //Key bindings
            if (selectedTab == OptionsTab.CONTROLS)
            {
                GUI.BeginGroup(new Rect(10, windowY, windowRect.width - 20, 92));
                groupY = 0;

                GUI.Label(new Rect(0, groupY, windowRect.width - 20, 48),
                    "Click a button below to select the action you want to change. Then press a key to set the binding. To cancel, click the button again or press Escape.",
                    noteStyle);
                groupY += 48;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Chat:", descriptorStyle);
                string chatKey = Settings.fetch.chatKey.ToString();
                if (settingChat)
                {
                    chatKey = settingKeyMessage;
                    if (Event.current.isKey)
                    {
                        if (Event.current.keyCode != KeyCode.Escape)
                        {
                            Settings.fetch.chatKey = Event.current.keyCode;
                            Settings.fetch.SaveSettings();
                        }
                        settingChat = false;
                    }
                }

                if (GUI.Button(new Rect(descWidth + sepWidth, groupY, windowRect.width - (descWidth + sepWidth) - 20, 20), chatKey, buttonStyle))
                {
                    settingScreenshot = false;
                    settingChat = !settingChat;
                }
                groupY += 22;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Screenshot:", descriptorStyle);
                string screenshotKey = Settings.fetch.screenshotKey.ToString();
                if (settingScreenshot)
                {
                    screenshotKey = settingKeyMessage;
                    if (Event.current.isKey)
                    {
                        if (Event.current.keyCode != KeyCode.Escape)
                        {
                            Settings.fetch.screenshotKey = Event.current.keyCode;
                            Settings.fetch.SaveSettings();
                        }
                        settingScreenshot = false;
                    }
                }

                if (GUI.Button(new Rect(descWidth + sepWidth, groupY, windowRect.width - (descWidth + sepWidth) - 20, 20), screenshotKey, buttonStyle))
                {
                    settingChat = false;
                    settingScreenshot = !settingScreenshot;
                }
                GUI.EndGroup();
            }
            if (selectedTab == OptionsTab.ADVANCED)
            {
                GUI.Box(new Rect(2, windowY, windowRect.width - 4, 20), "Mod Control", sectionHeaderStyle);
                windowY += 22;

                GUI.BeginGroup(new Rect(10, windowY, windowRect.width - 20, 42));
                groupY = 0;

                GUI.Label(new Rect(0, groupY, descWidth, 20), "Generate:", descriptorStyle);
                if (GUI.Button(new Rect(descWidth + sepWidth, groupY, windowRect.width - (descWidth + sepWidth) - 20, 20), "Whitelist", buttonStyle))
                    ModWorker.fetch.GenerateModControlFile(true);

                groupY += 22;

                if (GUI.Button(new Rect(descWidth + sepWidth, groupY, windowRect.width - (descWidth + sepWidth) - 20, 20), "Blacklist", buttonStyle))
                    ModWorker.fetch.GenerateModControlFile(false);

                GUI.EndGroup();
                windowY += 47;

                GUI.Box(new Rect(2, windowY, windowRect.width - 4, 20), "Other", sectionHeaderStyle);
                windowY += 22;

                GUI.BeginGroup(new Rect(10, windowY, windowRect.width - 20, 148));
                groupY = 0;

                bool toggleCompression = GUI.Toggle(new Rect(0, groupY, windowRect.width - 20, 20), Settings.fetch.compressionEnabled, "Compress Network Traffic");
                if (toggleCompression != Settings.fetch.compressionEnabled)
                {
                    Settings.fetch.compressionEnabled = toggleCompression;
                    Settings.fetch.SaveSettings();
                }
                groupY += 22;

                bool toggleRevert = GUI.Toggle(new Rect(0, groupY, windowRect.width - 20, 20), Settings.fetch.revertEnabled, "Enable Revert");
                if (toggleRevert != Settings.fetch.revertEnabled)
                {
                    Settings.fetch.revertEnabled = toggleRevert;
                    Settings.fetch.SaveSettings();
                }
                groupY += 22;

                UniverseConverterWindow.fetch.display = GUI.Toggle(new Rect(0, groupY, windowRect.width - 20, 20), UniverseConverterWindow.fetch.display, "Generate DMP universe from saved game...", buttonStyle);
                groupY += 22;

                if (GUI.Button(new Rect(0, groupY, windowRect.width - 20, 20), "Reset Disclaimer", buttonStyle))
                {
                    Settings.fetch.disclaimerAccepted = 0;
                    Settings.fetch.SaveSettings();
                }
                groupY += 22;

                if (GUI.Button(new Rect(0, groupY, windowRect.width - 20, 20), toolbarMode, buttonStyle))
                {
                    int newSetting = (int)Settings.fetch.toolbarType + 1;
                    //Overflow to 0
                    if (!Enum.IsDefined(typeof(DMPToolbarType), newSetting))
                    {
                        newSetting = 0;
                    }
                    Settings.fetch.toolbarType = (DMPToolbarType)newSetting;
                    Settings.fetch.SaveSettings();
                    UpdateToolbarString();
                    ToolbarSupport.fetch.DetectSettingsChange();
                }
                groupY += 22;

#if DEBUG
                if (GUI.Button(new Rect(0, groupY, windowRect.width - 20, 20), "Check missing parts", buttonStyle))
                {
                    ModWorker.fetch.CheckCommonStockParts();
                }
#endif

                GUI.EndGroup();
            }
        }

        private void CheckWindowLock()
        {
            if (!Client.fetch.gameRunning)
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
                    InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, "DMP_OptionsLock");
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
                InputLockManager.RemoveControlLock("DMP_OptionsLock");
            }
        }

        private string[] GetOptionsTabStrings()
        {
            System.Collections.Generic.List<string> stringList = new System.Collections.Generic.List<string>();
            foreach (OptionsTab enumVal in Enum.GetValues(typeof(OptionsTab)))
            {
                if (enumVal == OptionsTab.PLAYER) stringList.Add("Player");
                if (enumVal == OptionsTab.CACHE) stringList.Add("Cache");
                if (enumVal == OptionsTab.CONTROLS) stringList.Add("Keys");
                if (enumVal == OptionsTab.ADVANCED) stringList.Add("Advanced");
            }
            return stringList.ToArray();
        }
    }
}

