using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class PlayerStatusWindow
    {
        public bool display = false;
        public bool disconnectEventHandled = true;
        public bool colorEventHandled = true;
        private bool isWindowLocked = false;
        //private parts
        private bool initialized;
        private Vector2 scrollPosition;
        public bool minmized;
        private bool safeMinimized;
        private int lastUpdateTime;
        //GUI Layout
        private bool calculatedMinSize;
        private Rect windowRect;
        private Rect minWindowRect;
        private Rect moveRect;
        private GUILayoutOption[] layoutOptions;
        private GUILayoutOption[] minLayoutOptions;
        //Styles
        private GUIStyle windowStyle;
        private GUIStyle subspaceStyle;
        private GUIStyle buttonStyle;
        private GUIStyle highlightStyle;
        private GUIStyle scrollStyle;
        private Dictionary<string, GUIStyle> playerNameStyle;
        private GUIStyle vesselNameStyle;
        private GUIStyle stateTextStyle;
        //Player status dictionaries
        private Tuple<SubspaceDisplayEntry[], int> subspaceDisplay;
        private double lastStatusUpdate;
        //const
        private const float WINDOW_HEIGHT = 400;
        private const float WINDOW_WIDTH = 300;
        private const float UPDATE_STATUS_INTERVAL = .2f;
        //Services
        private DMPGame dmpGame;
        private Settings dmpSettings;
        private WarpWorker warpWorker;
        private ChatWorker chatWorker;
        private CraftLibraryWorker craftLibraryWorker;
        private ScreenshotWorker screenshotWorker;
        private TimeSyncer timeSyncer;
        private PlayerStatusWorker playerStatusWorker;
        private OptionsWindow optionsWindow;
        private PlayerColorWorker playerColorWorker;
        private GroupsWindow groupsWindow;
        private PermissionsWindow permissionsWindow;
        private NamedAction updateAction;
        private NamedAction drawAction;
        private StringBuilder stringBuilder = new StringBuilder(128);

        public PlayerStatusWindow(DMPGame dmpGame, Settings dmpSettings, WarpWorker warpWorker, ChatWorker chatWorker, CraftLibraryWorker craftLibraryWorker, ScreenshotWorker screenshotWorker, TimeSyncer timeSyncer, PlayerStatusWorker playerStatusWorker, OptionsWindow optionsWindow, PlayerColorWorker playerColorWorker, GroupsWindow groupsWindow, PermissionsWindow permissionsWindow)
        {
            this.dmpGame = dmpGame;
            this.dmpSettings = dmpSettings;
            this.warpWorker = warpWorker;
            this.chatWorker = chatWorker;
            this.craftLibraryWorker = craftLibraryWorker;
            this.screenshotWorker = screenshotWorker;
            this.timeSyncer = timeSyncer;
            this.playerStatusWorker = playerStatusWorker;
            this.optionsWindow = optionsWindow;
            this.playerColorWorker = playerColorWorker;
            this.groupsWindow = groupsWindow;
            this.permissionsWindow = permissionsWindow;
            updateAction = new NamedAction(Update);
            drawAction = new NamedAction(Draw);
            this.dmpGame.updateEvent.Add(updateAction);
            this.dmpGame.drawEvent.Add(drawAction);
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width * 0.9f - WINDOW_WIDTH, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
            minWindowRect = new Rect(float.NegativeInfinity, float.NegativeInfinity, 0, 0);
            moveRect = new Rect(0, 0, 10000, 20);

            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);
            highlightStyle = new GUIStyle(GUI.skin.button);
            highlightStyle.normal.textColor = Color.red;
            highlightStyle.active.textColor = Color.red;
            highlightStyle.hover.textColor = Color.red;
            scrollStyle = new GUIStyle(GUI.skin.scrollView);
            subspaceStyle = new GUIStyle();
            Texture2D blackTexture = new Texture2D(1, 1);
            Color black = new Color(0, 0, 0, 0.5f);
            blackTexture.SetPixel(0, 0, black);
            blackTexture.Apply();
            subspaceStyle.normal.background = blackTexture;

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.MinWidth(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.MaxWidth(WINDOW_WIDTH);
            layoutOptions[2] = GUILayout.MinHeight(WINDOW_HEIGHT);
            layoutOptions[3] = GUILayout.MaxHeight(WINDOW_HEIGHT);

            minLayoutOptions = new GUILayoutOption[4];
            minLayoutOptions[0] = GUILayout.MinWidth(0);
            minLayoutOptions[1] = GUILayout.MinHeight(0);
            minLayoutOptions[2] = GUILayout.ExpandHeight(true);
            minLayoutOptions[3] = GUILayout.ExpandWidth(true);

            //Adapted from KMP.
            playerNameStyle = new Dictionary<string, GUIStyle>();

            vesselNameStyle = new GUIStyle(GUI.skin.label);
            vesselNameStyle.normal.textColor = Color.white;
            vesselNameStyle.hover.textColor = vesselNameStyle.normal.textColor;
            vesselNameStyle.active.textColor = vesselNameStyle.normal.textColor;
            vesselNameStyle.fontStyle = FontStyle.Normal;
            vesselNameStyle.fontSize = 12;
            vesselNameStyle.stretchWidth = true;

            stateTextStyle = new GUIStyle(GUI.skin.label);
            stateTextStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            stateTextStyle.hover.textColor = stateTextStyle.normal.textColor;
            stateTextStyle.active.textColor = stateTextStyle.normal.textColor;
            stateTextStyle.fontStyle = FontStyle.Normal;
            stateTextStyle.fontSize = 12;
            stateTextStyle.stretchWidth = true;

            subspaceDisplay = new Tuple<SubspaceDisplayEntry[], int>(new SubspaceDisplayEntry[0], 0);
        }

        private void Update()
        {
            display = dmpGame.running;
            if (display)
            {
                safeMinimized = minmized;
                if (!calculatedMinSize && minWindowRect.width != 0 && minWindowRect.height != 0)
                {
                    calculatedMinSize = true;
                }
                if ((Client.realtimeSinceStartup - lastStatusUpdate) > UPDATE_STATUS_INTERVAL)
                {
                    lastStatusUpdate = Client.realtimeSinceStartup;
                    subspaceDisplay = warpWorker.GetSubspaceDisplayEntries();
                }
            }
        }

        private void Draw()
        {
            if (!colorEventHandled)
            {
                playerNameStyle = new Dictionary<string, GUIStyle>();
                colorEventHandled = true;
            }
            if (!initialized)
            {
                initialized = true;
                InitGUI();
            }
            if (display)
            {
                //Calculate the minimum size of the minimize window by drawing it off the screen
                if (!calculatedMinSize)
                {
                    minWindowRect = GUILayout.Window(6701 + Client.WINDOW_OFFSET, minWindowRect, DrawMaximize, "DMP", windowStyle, minLayoutOptions);
                }
                if (!safeMinimized)
                {
                    windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6703 + Client.WINDOW_OFFSET, windowRect, DrawContent, "DarkMultiPlayer - Status", windowStyle, layoutOptions));
                }
                else
                {
                    minWindowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6703 + Client.WINDOW_OFFSET, minWindowRect, DrawMaximize, "DMP", windowStyle, minLayoutOptions));
                }
            }
            CheckWindowLock();
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUIStyle chatButtonStyle = buttonStyle;
            if (chatWorker.chatButtonHighlighted)
            {
                chatButtonStyle = highlightStyle;
            }
            chatWorker.display = GUILayout.Toggle(chatWorker.display, "Chat", chatButtonStyle);
            craftLibraryWorker.display = GUILayout.Toggle(craftLibraryWorker.display, "Craft", buttonStyle);
            groupsWindow.display = GUILayout.Toggle(groupsWindow.display, "Group", buttonStyle);
            permissionsWindow.display = GUILayout.Toggle(permissionsWindow.display, "Permissions", buttonStyle);
            GUIStyle screenshotButtonStyle = buttonStyle;
            if (screenshotWorker.screenshotButtonHighlighted)
            {
                screenshotButtonStyle = highlightStyle;
            }
            screenshotWorker.display = GUILayout.Toggle(screenshotWorker.display, "Screenshot", screenshotButtonStyle);
            if (GUILayout.Button("-", buttonStyle))
            {
                minmized = true;
                minWindowRect.x = windowRect.xMax - minWindowRect.width;
                minWindowRect.y = windowRect.y;
            }
            GUILayout.EndHorizontal();
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, scrollStyle);
            //Draw subspaces
            double ourTime = timeSyncer.locked ? timeSyncer.GetUniverseTime() : Planetarium.GetUniversalTime();
            long serverClock = timeSyncer.GetServerClock();
            bool updateSubspace = false;
            if (lastUpdateTime != (int)Client.realtimeSinceStartup)
            {
                updateSubspace = true;
                lastUpdateTime = (int)Client.realtimeSinceStartup;
            }
            for (int i = 0; i < subspaceDisplay.Item2; i++)
            {
                SubspaceDisplayEntry currentEntry = subspaceDisplay.Item1[i];
                int diffTime = 0;
                if (updateSubspace || currentEntry.relativeTimeDisplay == null)
                {
                    int currentTime = 0;
                    if (!currentEntry.isUnknown)
                    {
                        if (!currentEntry.isUs)
                        {
                            if (!currentEntry.isWarping)
                            {
                                //Subspace entry
                                if (currentEntry.subspaceEntry != null)
                                {
                                    long serverClockDiff = serverClock - currentEntry.subspaceEntry.serverClock;
                                    double secondsDiff = serverClockDiff / 10000000d;
                                    currentTime = (int)(currentEntry.subspaceEntry.planetTime + (currentEntry.subspaceEntry.subspaceSpeed * secondsDiff));
                                    diffTime = (int)(currentTime - ourTime);
                                    if (diffTime > 0)
                                    {
                                        currentEntry.relativeTimeDisplay = String.Format("T+ {0} - {1} in the future", SecondsToShortString(currentTime), SecondsToVeryShortString(diffTime));
                                    }
                                    if (diffTime < 0)
                                    {
                                        currentEntry.relativeTimeDisplay = String.Format("T+ {0} - {1} in the past", SecondsToShortString(currentTime), SecondsToVeryShortString(-diffTime));
                                    }
                                    if (diffTime == 0)
                                    {
                                        currentEntry.relativeTimeDisplay = String.Format("T+ {0} - NOW", SecondsToShortString(currentTime));
                                    }
                                }
                            }
                            else
                            {
                                //Warp entry
                                if (currentEntry.warpingEntry != null)
                                {
                                    float[] warpRates = TimeWarp.fetch.warpRates;
                                    if (currentEntry.warpingEntry.isPhysWarp)
                                    {
                                        warpRates = TimeWarp.fetch.physicsWarpRates;
                                    }
                                    long serverClockDiff = serverClock - currentEntry.warpingEntry.serverClock;
                                    double secondsDiff = serverClockDiff / 10000000d;
                                    currentTime = (int)(currentEntry.warpingEntry.planetTime + (warpRates[currentEntry.warpingEntry.rateIndex] * secondsDiff));
                                    diffTime = (int)(currentTime - ourTime);
                                    if (diffTime > 0)
                                    {
                                        currentEntry.relativeTimeDisplay = String.Format("T+ {0} - {1} in the future", SecondsToShortString(currentTime), SecondsToVeryShortString(diffTime));
                                    }
                                    if (diffTime < 0)
                                    {
                                        currentEntry.relativeTimeDisplay = String.Format("T+ {0} - {1} in the past", SecondsToShortString(currentTime), SecondsToVeryShortString(-diffTime));
                                    }
                                    if (diffTime == 0)
                                    {
                                        currentEntry.relativeTimeDisplay = String.Format("T+ {0} - NOW", SecondsToShortString(currentTime));
                                    }
                                }
                            }
                        }
                        else
                        {
                            currentTime = (int)ourTime;
                            currentEntry.relativeTimeDisplay = String.Format("T+ {0} - NOW", SecondsToShortString(currentTime));
                        }
                    }
                    else
                    {
                        currentEntry.relativeTimeDisplay = "Unknown";
                    }
                }

                //Draw the subspace black bar.
                GUILayout.BeginHorizontal(subspaceStyle);
                GUILayout.Label(currentEntry.relativeTimeDisplay);
                GUILayout.FlexibleSpace();
                //Draw the sync button if needed
#if !DEBUG
                if ((warpWorker.warpMode == WarpMode.SUBSPACE) && !currentEntry.isUs && !currentEntry.isWarping && (currentEntry.subspaceEntry != null) && (diffTime > 0))
#else
                if ((warpWorker.warpMode == WarpMode.SUBSPACE) && !currentEntry.isUs && !currentEntry.isWarping && (currentEntry.subspaceEntry != null))
#endif
                {
                    if (GUILayout.Button("Sync", buttonStyle))
                    {
                        timeSyncer.LockSubspace(currentEntry.subspaceID);
                    }
                }
                GUILayout.EndHorizontal();

                foreach (string currentPlayer in currentEntry.players)
                {
                    if (currentPlayer == dmpSettings.playerName)
                    {
                        DrawPlayerEntry(playerStatusWorker.myPlayerStatus, playerColorWorker.GetPlayerColor(currentPlayer));
                    }
                    else
                    {
                        DrawPlayerEntry(playerStatusWorker.GetPlayerStatus(currentPlayer), playerColorWorker.GetPlayerColor(currentPlayer));
                    }
                }
            }
            if (DateTime.Now.Day == 1 && DateTime.Now.Month == 4)
            {
                PlayerStatus easterEgg = new PlayerStatus();
                easterEgg.playerName = "Princess Luna";
                easterEgg.statusText = "Stranded on the Mün";
                easterEgg.vesselText = "";
                DrawPlayerEntry(easterEgg, new Color(0.251f, 0.275f, 0.502f));
            }
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Disconnect", buttonStyle))
            {
                disconnectEventHandled = false;
            }
            optionsWindow.display = GUILayout.Toggle(optionsWindow.display, "Options", buttonStyle);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
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

            if (display)
            {
                Vector2 mousePos = Input.mousePosition;
                mousePos.y = Screen.height - mousePos.y;

                bool shouldLock = (minmized ? minWindowRect.Contains(mousePos) : windowRect.Contains(mousePos));

                if (shouldLock && !isWindowLocked)
                {
                    InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, "DMP_PlayerStatusLock");
                    isWindowLocked = true;
                }
                if (!shouldLock && isWindowLocked)
                {
                    RemoveWindowLock();
                }
            }

            if (!display && isWindowLocked)
            {
                RemoveWindowLock();
            }
        }

        private void RemoveWindowLock()
        {
            if (isWindowLocked)
            {
                isWindowLocked = false;
                InputLockManager.RemoveControlLock("DMP_PlayerStatusLock");
            }
        }

        private string SecondsToLongString(int time)
        {
            // Use Kerbin days (6h days, 426d years)
            int day_unit = 24;
            if (GameSettings.KERBIN_TIME)
                day_unit = 6;

            //Every month is feburary ok?
            int years = time / (60 * 60 * day_unit * 7 * 4 * 12);
            time -= years * (60 * 60 * day_unit * 7 * 4 * 12);
            int months = time / (60 * 60 * day_unit * 7 * 4);
            time -= months * (60 * 60 * day_unit * 7 * 4);
            int weeks = time / (60 * 60 * day_unit * 7);
            time -= weeks * (60 * 60 * day_unit * 7);
            int days = time / (60 * 60 * day_unit);
            time -= days * (60 * 60 * day_unit);
            int hours = time / (60 * 60);
            time -= hours * (60 * 60);
            int minutes = time / 60;
            time -= minutes * 60;
            int seconds = time;
            stringBuilder.Clear();
            bool addComma = false;
            if (years > 0)
            {
                if (years == 1)
                {
                    stringBuilder.Append("1 year");
                }
                else
                {
                    stringBuilder.Append(years);
                    stringBuilder.Append(" years");
                }
                addComma = true;
            }
            if (months > 0)
            {
                if (addComma)
                {
                    addComma = false;
                    stringBuilder.Append(", ");
                }
                if (months == 1)
                {
                    stringBuilder.Append("1 month");
                }
                else
                {
                    stringBuilder.Append(months);
                    stringBuilder.Append(" month");
                }
                addComma = true;
            }
            if (weeks > 0)
            {
                if (addComma)
                {
                    addComma = false;
                    stringBuilder.Append(", ");
                }
                if (weeks == 1)
                {
                    stringBuilder.Append("1 week");
                }
                else
                {
                    stringBuilder.Append(weeks);
                    stringBuilder.Append(" weeks");
                }
                addComma = true;
            }
            if (days > 0)
            {
                if (addComma)
                {
                    addComma = false;
                    stringBuilder.Append(", ");
                }
                if (days == 1)
                {
                    stringBuilder.Append("1 day");
                }
                else
                {
                    stringBuilder.Append(days);
                    stringBuilder.Append(" days");
                }
                addComma = true;
            }
            if (hours > 0)
            {
                if (addComma)
                {
                    addComma = false;
                    stringBuilder.Append(", ");
                }
                if (hours == 1)
                {
                    stringBuilder.Append("1 hour");
                }
                else
                {
                    stringBuilder.Append(hours);
                    stringBuilder.Append(" hours");
                }
                addComma = true;
            }
            if (minutes > 0)
            {
                if (addComma)
                {
                    addComma = false;
                    stringBuilder.Append(", ");
                }
                if (minutes == 1)
                {
                    stringBuilder.Append("1 minute");
                }
                else
                {
                    stringBuilder.Append(minutes);
                    stringBuilder.Append(" minutes");
                }
                addComma = true;
            }
            if (seconds == 1)
            {
                if (addComma)
                {
                    addComma = false;
                    stringBuilder.Append(", ");
                }
                stringBuilder.Append("1 second");
            }
            else
            {
                if (addComma)
                {
                    addComma = false;
                    stringBuilder.Append(", ");
                }
                stringBuilder.Append(seconds);
                stringBuilder.Append(" seconds");
            }
            return stringBuilder.ToString();
        }

        private string SecondsToShortString(int time)
        {
            // Use Kerbin days (6h days, 426d years)
            int day_unit = 24;
            if (GameSettings.KERBIN_TIME)
                day_unit = 6;

            int years = time / (60 * 60 * day_unit * 7 * 4 * 12);
            time -= years * (60 * 60 * day_unit * 7 * 4 * 12);
            int months = time / (60 * 60 * day_unit * 7 * 4);
            time -= months * (60 * 60 * day_unit * 7 * 4);
            int weeks = time / (60 * 60 * day_unit * 7);
            time -= weeks * (60 * 60 * day_unit * 7);
            int days = time / (60 * 60 * day_unit);
            time -= days * (60 * 60 * day_unit);
            int hours = time / (60 * 60);
            time -= hours * (60 * 60);
            int minutes = time / 60;
            time -= minutes * 60;
            int seconds = time;
            stringBuilder.Clear();
            if (years > 0)
            {
                if (years == 1)
                {
                    stringBuilder.Append("1y, ");
                }
                else
                {
                    stringBuilder.Append(years);
                    stringBuilder.Append("y, ");
                }
            }
            if (months > 0)
            {
                if (months == 1)
                {
                    stringBuilder.Append("1m, ");
                }
                else
                {
                    stringBuilder.Append(months);
                    stringBuilder.Append("m, ");
                }
            }
            if (weeks > 0)
            {
                if (weeks == 1)
                {
                    stringBuilder.Append("1w, ");
                }
                else
                {
                    stringBuilder.Append(weeks);
                    stringBuilder.Append("w, ");
                }
            }
            if (days > 0)
            {
                if (days == 1)
                {
                    stringBuilder.Append("1d, ");
                }
                else
                {
                    stringBuilder.Append(days);
                    stringBuilder.Append("d, ");
                }
            }
            stringBuilder.AppendFormat("{0:D2}:{1:D2}:{2:D2}", hours, minutes, seconds);
            return stringBuilder.ToString();
        }

        private string SecondsToVeryShortString(int time)
        {
            // Use Kerbin days (6h days, 426d years)
            int day_unit = 24;
            if (GameSettings.KERBIN_TIME)
                day_unit = 6;

            int years = time / (60 * 60 * day_unit * 7 * 4 * 12);
            time -= years * (60 * 60 * day_unit * 7 * 4 * 12);
            int months = time / (60 * 60 * day_unit * 7 * 4);
            time -= months * (60 * 60 * day_unit * 7 * 4);
            int weeks = time / (60 * 60 * day_unit * 7);
            time -= weeks * (60 * 60 * day_unit * 7);
            int days = time / (60 * 60 * day_unit);
            time -= days * (60 * 60 * day_unit);
            int hours = time / (60 * 60);
            time -= hours * (60 * 60);
            int minutes = time / 60;
            time -= minutes * 60;
            int seconds = time;
            if (years > 0)
            {
                if (years == 1)
                {
                    return "1 year";
                }
                else
                {
                    return String.Format("{0} years", years);
                }
            }
            if (months > 0)
            {
                if (months == 1)
                {
                    return "1 month";
                }
                else
                {
                    return String.Format("{0} months", months);
                }
            }
            if (weeks > 0)
            {
                if (weeks == 1)
                {
                    return "1 week";
                }
                else
                {
                    return String.Format("{0} weeks", weeks);
                }
            }
            if (days > 0)
            {
                if (days == 1)
                {
                    return "1 day";
                }
                else
                {
                    return String.Format("{0} days", days);
                }
            }
            if (hours > 0)
            {
                if (hours == 1)
                {
                    return "1 hour";
                }
                else
                {
                    return String.Format("{0} hours", hours);
                }
            }
            if (minutes > 0)
            {
                if (minutes == 1)
                {
                    return "1 minute";
                }
                else
                {
                    return String.Format("{0} minutes", minutes);
                }
            }
            if (seconds == 1)
            {
                return "1 second";
            }
            else
            {
                return String.Format("{0} seconds", seconds);
            }
        }

        private void DrawMaximize(int windowID)
        {
            GUI.DragWindow(moveRect);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUIStyle chatButtonStyle = buttonStyle;
            if (chatWorker.chatButtonHighlighted)
            {
                chatButtonStyle = highlightStyle;
            }
            chatWorker.display = GUILayout.Toggle(chatWorker.display, "C", chatButtonStyle);
            groupsWindow.display = GUILayout.Toggle(groupsWindow.display, "G", buttonStyle);
            permissionsWindow.display = GUILayout.Toggle(permissionsWindow.display, "P", buttonStyle);
            GUIStyle screenshotButtonStyle = buttonStyle;
            if (screenshotWorker.screenshotButtonHighlighted)
            {
                screenshotButtonStyle = highlightStyle;
            }
            screenshotWorker.display = GUILayout.Toggle(screenshotWorker.display, "S", screenshotButtonStyle);
            optionsWindow.display = GUILayout.Toggle(optionsWindow.display, "O", buttonStyle);
            if (GUILayout.Button("+", buttonStyle))
            {
                windowRect.xMax = minWindowRect.xMax;
                windowRect.yMin = minWindowRect.yMin;
                windowRect.xMin = minWindowRect.xMax - WINDOW_WIDTH;
                windowRect.yMax = minWindowRect.yMin + WINDOW_HEIGHT;
                minmized = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawPlayerEntry(PlayerStatus playerStatus, Color playerColor)
        {
            if (playerStatus == null)
            {
                //Just connected or disconnected.
                return;
            }
            GUILayout.BeginHorizontal();
            if (!playerNameStyle.ContainsKey(playerStatus.playerName))
            {
                playerNameStyle[playerStatus.playerName] = new GUIStyle(GUI.skin.label);
                playerNameStyle[playerStatus.playerName].normal.textColor = playerColor;
                playerNameStyle[playerStatus.playerName].hover.textColor = playerColor;
                playerNameStyle[playerStatus.playerName].active.textColor = playerColor;
                playerNameStyle[playerStatus.playerName].fontStyle = FontStyle.Bold;
                playerNameStyle[playerStatus.playerName].stretchWidth = true;
                playerNameStyle[playerStatus.playerName].wordWrap = false;
            }
            GUILayout.Label(playerStatus.playerName, playerNameStyle[playerStatus.playerName]);
            GUILayout.FlexibleSpace();
            GUILayout.Label(playerStatus.statusText, stateTextStyle);
            GUILayout.EndHorizontal();
            if (playerStatus.vesselText != "")
            {
                GUILayout.Label("Pilot: " + playerStatus.vesselText, vesselNameStyle);
            }
        }

        public void Stop()
        {
            display = false;
            RemoveWindowLock();
            dmpGame.updateEvent.Remove(updateAction);
            dmpGame.drawEvent.Remove(drawAction);

        }
    }
}

