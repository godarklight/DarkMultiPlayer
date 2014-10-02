using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class OptionsWindow
    {
        private static OptionsWindow singleton = new OptionsWindow();
        public bool loadEventHandled = true;
        public bool display;
        private bool isEditorLocked = false;
        private bool safeDisplay;
        private bool initialized;
        //GUI Layout
        private Rect windowRect;
        private Rect moveRect;
        private GUILayoutOption[] layoutOptions;
        //Styles
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        //const
        private const float WINDOW_HEIGHT = 400;
        private const float WINDOW_WIDTH = 300;
        //TempColour
        private Color tempColor = new Color(1f, 1f, 1f, 1f);
        private GUIStyle tempColorLabelStyle;
        //Cache size
        private string newCacheSize = "";
        //Keybindings
        private bool settingChat;
        private bool settingScreenshot;

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

            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.Width(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.Height(WINDOW_HEIGHT);
            layoutOptions[2] = GUILayout.ExpandWidth(true);
            layoutOptions[3] = GUILayout.ExpandHeight(true);

            tempColor = new Color();
            tempColorLabelStyle = new GUIStyle(GUI.skin.label);
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
                windowRect = GUILayout.Window(GUIUtility.GetControlID(6711 + Client.WINDOW_OFFSET, FocusType.Passive), windowRect, DrawContent, "DarkMultiPlayer - Options", windowStyle, layoutOptions);
            }
            CheckEditorLock();
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
            GUILayout.BeginHorizontal();
            GUILayout.Label("Player name color: ");
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
            if (GUILayout.Button("Random", buttonStyle))
            {
                tempColor = PlayerColorWorker.GenerateRandomColor();
            }
            if (GUILayout.Button("Set", buttonStyle))
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
            GUILayout.Label("Cache size");
            GUILayout.Label("Current size: " + Math.Round((UniverseSyncCache.fetch.currentCacheSize / (float)(1024 * 1024)), 3) + "MB.");
            GUILayout.Label("Max size: " + Settings.fetch.cacheSize + "MB.");
            newCacheSize = GUILayout.TextArea(newCacheSize);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Set", buttonStyle))
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
            if (GUILayout.Button("Expire cache"))
            {
                UniverseSyncCache.fetch.ExpireCache();
            }
            if (GUILayout.Button("Delete cache"))
            {
                UniverseSyncCache.fetch.DeleteCache();
            }
            GUILayout.EndHorizontal();
            //Key bindings
            GUILayout.Space(10);
            string chatDescription = "Set chat key (current: " + Settings.fetch.chatKey + ")";
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
            string screenshotDescription = "Set screenshot key (current: " + Settings.fetch.screenshotKey.ToString() + ")";
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
            GUILayout.Label("Generate a server DMPModControl:");
            if (GUILayout.Button("Generate blacklist DMPModControl.txt"))
            {
                ModWorker.fetch.GenerateModControlFile(false);
            }
            if (GUILayout.Button("Generate whitelist DMPModControl.txt"))
            {
                ModWorker.fetch.GenerateModControlFile(true);
            }
            UniverseConverterWindow.fetch.display = GUILayout.Toggle(UniverseConverterWindow.fetch.display, "Generate Universe from saved game", buttonStyle);
            if (GUILayout.Button("Reset disclaimer"))
            {
                Settings.fetch.disclaimerAccepted = 0;
                Settings.fetch.SaveSettings();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", buttonStyle))
            {
                display = false;
            }
            GUILayout.EndVertical();
        }

        private void CheckEditorLock()
        {
            if (!Client.fetch.gameRunning)
            {
                return;
            }

            EditorLogic editor = EditorLogic.fetch;

            if (editor == null)
            {
                return;
            }

            if (safeDisplay)
            {
                Vector2 mousePos = Input.mousePosition;
                mousePos.y = Screen.height - mousePos.y;

                bool shouldLock = (windowRect.Contains(mousePos) || moveRect.Contains(mousePos));


                if (shouldLock && !isEditorLocked)
                {
                    EditorLogic.fetch.Lock(true, true, true, "DMP_OptionsLock");
                    isEditorLocked = true;
                }
                else if (!shouldLock)
                {
                    if (!isEditorLocked)
                    {
                        editor.Lock(true, true, true, "DMP_OptionsLock");
                    }
                    editor.Unlock("DMP_OptionsLock");
                    isEditorLocked = false;
                }
            }

            if (!safeDisplay && isEditorLocked)
            {
                editor.Unlock("DMP_OptionsLock");
                isEditorLocked = false;
            }
        }
    }
}

