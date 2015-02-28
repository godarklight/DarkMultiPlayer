using System;
using UnityEngine;

namespace DarkMultiPlayer
{
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
        private Vector2 scrollPos;
        //Styles
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        //const
        private const float WINDOW_HEIGHT = 400;
        private const float WINDOW_WIDTH = 305;
        //TempColour
        private Color tempColor = new Color(1f, 1f, 1f, 1f);
        private GUIStyle tempColorLabelStyle;
        //Cache size
        private string newCacheSize = "";
        //Keybindings
        private bool settingChat;
        private bool settingScreenshot;
        private string toolbarMode;

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
            windowRect = new Rect(Screen.width / 2f - WINDOW_WIDTH / 2f, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);
            
            GUI.backgroundColor = new Color(GUI.backgroundColor.r, GUI.backgroundColor.g, GUI.backgroundColor.b, 191.25f);

            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.Width(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.Height(WINDOW_HEIGHT);
            layoutOptions[2] = GUILayout.ExpandWidth(true);
            layoutOptions[3] = GUILayout.ExpandHeight(true);

            smallOption = new GUILayoutOption[2];
            smallOption[0] = GUILayout.Width(100);
            smallOption[1] = GUILayout.ExpandWidth(false);

            tempColor = new Color();
            tempColorLabelStyle = new GUIStyle(GUI.skin.label);
            UpdateToolbarString();
        }

        private void UpdateToolbarString()
        {
            switch (Settings.fetch.toolbarType)
            {
                case DMPToolbarType.DISABLED:
                    toolbarMode = LanguageWorker.fetch.GetString("tbDisabled");
                    break;
                case DMPToolbarType.FORCE_STOCK:
                    toolbarMode = LanguageWorker.fetch.GetString("tbStock");
                    break;
                case DMPToolbarType.BLIZZY_IF_INSTALLED:
                    toolbarMode = LanguageWorker.fetch.GetString("tbBlizzy");
                    break;
                case DMPToolbarType.BOTH_IF_INSTALLED:
                    toolbarMode = LanguageWorker.fetch.GetString("tbBoth");
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
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6711 + Client.WINDOW_OFFSET, windowRect, DrawContent, String.Format("DarkMultiPlayer - {0}", LanguageWorker.fetch.GetString("options")), windowStyle, layoutOptions));
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
            //Player color
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Width(WINDOW_WIDTH - 5), GUILayout.Height(WINDOW_HEIGHT - 25));
            GUILayout.BeginHorizontal();
            GUILayout.Label(LanguageWorker.fetch.GetString("playerNameColor"));
            GUILayout.Label(Settings.fetch.playerName, tempColorLabelStyle);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("R: ");
            tempColor.r = GUILayout.HorizontalScrollbar(tempColor.r, 0, 0, 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("G: ");
            tempColor.g = GUILayout.HorizontalScrollbar(tempColor.g, 0, 0, 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("B: ");
            tempColor.b = GUILayout.HorizontalScrollbar(tempColor.b, 0, 0, 1);
            GUILayout.EndHorizontal();
            tempColorLabelStyle.active.textColor = tempColor;
            tempColorLabelStyle.normal.textColor = tempColor;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(LanguageWorker.fetch.GetString("randomBtn"), buttonStyle))
            {
                tempColor = PlayerColorWorker.GenerateRandomColor();
            }
            if (GUILayout.Button(LanguageWorker.fetch.GetString("setBtn"), buttonStyle))
            {
                PlayerStatusWindow.fetch.colorEventHandled = false;
                Settings.fetch.playerColor = tempColor;
                Settings.fetch.SaveSettings();
                if (NetworkWorker.fetch.state == DarkMultiPlayerCommon.ClientState.RUNNING)
                {
                    PlayerColorWorker.fetch.SendPlayerColorToServer();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            //Cache
            GUILayout.Label(LanguageWorker.fetch.GetString("cacheSizeLabel"));
            GUILayout.Label(LanguageWorker.fetch.GetFormattedString(LanguageWorker.fetch.GetString("currentCacheSizeLabel"), new string[] { Math.Round((UniverseSyncCache.fetch.currentCacheSize / (float)(1024 * 1024)), 3).ToString() }));
            GUILayout.Label(LanguageWorker.fetch.GetFormattedString(LanguageWorker.fetch.GetString("maxCacheSizeLabel"), new string[] { Settings.fetch.cacheSize.ToString() }));
            newCacheSize = GUILayout.TextArea(newCacheSize);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(LanguageWorker.fetch.GetString("setBtn"), buttonStyle))
            {
                int tempCacheSize;
                if (Int32.TryParse(newCacheSize, out tempCacheSize))
                {
                    if (tempCacheSize < 1)
                    {
                        tempCacheSize = 1;
                        newCacheSize = tempCacheSize.ToString();
                    }
                    if (tempCacheSize > 1000)
                    {
                        tempCacheSize = 1000;
                        newCacheSize = tempCacheSize.ToString();
                    }
                    Settings.fetch.cacheSize = tempCacheSize;
                    Settings.fetch.SaveSettings();
                }
                else
                {
                    newCacheSize = Settings.fetch.cacheSize.ToString();
                }
            }
            if (GUILayout.Button(LanguageWorker.fetch.GetString("expireCacheButton")))
            {
                UniverseSyncCache.fetch.ExpireCache();
            }
            if (GUILayout.Button(LanguageWorker.fetch.GetString("deleteCacheButton")))
            {
                UniverseSyncCache.fetch.DeleteCache();
            }
            GUILayout.EndHorizontal();
            //Key bindings
            GUILayout.Space(10);
            string chatDescription = LanguageWorker.fetch.GetFormattedString(LanguageWorker.fetch.GetString("setChatKeyBtn"), new string[] { LanguageWorker.fetch.GetString("currentKey"), Settings.fetch.chatKey.ToString() });
            if (settingChat)
            {
                chatDescription = "Setting chat key (click to cancel)...";
                if (Event.current.isKey)
                {
                    if (Event.current.keyCode != KeyCode.Escape)
                    {
                        Settings.fetch.chatKey = Event.current.keyCode;
                        Settings.fetch.SaveSettings();
                        settingChat = false;
                    }
                    else
                    {
                        settingChat = false;
                    }
                }
            }
            if (GUILayout.Button(chatDescription))
            {
                settingChat = !settingChat;
            }
            string screenshotDescription = LanguageWorker.fetch.GetFormattedString(LanguageWorker.fetch.GetString("setScrnShotKeyBtn"), new string[]{ LanguageWorker.fetch.GetString("currentKey"), Settings.fetch.screenshotKey.ToString() });
            if (settingScreenshot)
            {
                screenshotDescription = "Setting screenshot key (click to cancel)...";
                if (Event.current.isKey)
                {
                    if (Event.current.keyCode != KeyCode.Escape)
                    {
                        Settings.fetch.screenshotKey = Event.current.keyCode;
                        Settings.fetch.SaveSettings();
                        settingScreenshot = false;
                    }
                    else
                    {
                        settingScreenshot = false;
                    }
                }
            }
            if (GUILayout.Button(screenshotDescription))
            {
                settingScreenshot = !settingScreenshot;
            }
            GUILayout.Space(10);
            GUILayout.Label(LanguageWorker.fetch.GetString("generateModCntrlLabel"));
            if (GUILayout.Button(LanguageWorker.fetch.GetString("generateModBlacklistBtn")))
            {
                ModWorker.fetch.GenerateModControlFile(false);
            }
            if (GUILayout.Button(LanguageWorker.fetch.GetString("generateModWhitelistBtn")))
            {
                ModWorker.fetch.GenerateModControlFile(true);
            }
            UniverseConverterWindow.fetch.display = GUILayout.Toggle(UniverseConverterWindow.fetch.display, LanguageWorker.fetch.GetString("generateUniverseSavedGameBtn"), buttonStyle);
            if (GUILayout.Button(LanguageWorker.fetch.GetString("resetDisclaimerBtn")))
            {
                Settings.fetch.disclaimerAccepted = 0;
                Settings.fetch.SaveSettings();
            }
            bool settingCompression = GUILayout.Toggle(Settings.fetch.compressionEnabled, LanguageWorker.fetch.GetFormattedString(LanguageWorker.fetch.GetString("enableCompressionBtn"), new string[] { (Settings.fetch.compressionEnabled ? LanguageWorker.fetch.GetString("disable") : LanguageWorker.fetch.GetString("enable")) }), buttonStyle);
            if (settingCompression != Settings.fetch.compressionEnabled)
            {
                Settings.fetch.compressionEnabled = settingCompression;
                Settings.fetch.SaveSettings();
            }
            bool settingRevert = GUILayout.Toggle(Settings.fetch.revertEnabled, LanguageWorker.fetch.GetFormattedString(LanguageWorker.fetch.GetString("enableRevertBtn"), new string[] { (Settings.fetch.revertEnabled ? LanguageWorker.fetch.GetString("disable") : LanguageWorker.fetch.GetString("enable")) }), buttonStyle);
            if (settingRevert != Settings.fetch.revertEnabled)
            {
                Settings.fetch.revertEnabled = settingRevert;
                Settings.fetch.SaveSettings();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(LanguageWorker.fetch.GetString("toolbarModeLabel"), smallOption);
            if (GUILayout.Button(toolbarMode, buttonStyle))
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
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(LanguageWorker.fetch.GetString("langLabel"), smallOption);
            if (GUILayout.Button(LanguageWorker.fetch.GetLanguageById(Settings.fetch.useLanguage), buttonStyle))
            {
                int newSetting = (int)Settings.fetch.useLanguage + 1;

                if (!Enum.IsDefined(typeof(DMPLanguage), newSetting))
                {
                    newSetting = 0;
                }
                Settings.fetch.useLanguage = (DMPLanguage)newSetting;
                Settings.fetch.SaveSettings();
                LanguageWorker.fetch.LoadLanguage(Settings.fetch.useLanguage);
            }
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(LanguageWorker.fetch.GetString("closeBtn"), buttonStyle))
            {
                display = false;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
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
    }
}

