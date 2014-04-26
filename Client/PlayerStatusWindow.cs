using System;
using System.Collections.Generic;
using UnityEngine;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class PlayerStatusWindow
    {
        public bool display;
        public bool disconnectEventHandled;
        //private parts
        private Client parent;
        private bool initialized;
        private Vector2 scrollPosition;
        private bool displayNTP;
        public bool minmized;
        public bool safeMinimized;
        //GUI Layout
        private Rect windowRect;
        private Rect minWindowRect;
        private Rect moveRect;
        private GUILayoutOption[] layoutOptions;
        private GUILayoutOption[] minLayoutOptions;
        //Styles
        private GUIStyle windowStyle;
        private GUIStyle subspaceStyle;
        private GUIStyle buttonStyle;
        private GUIStyle scrollStyle;
        private GUIStyle playerNameStyle;
        private GUIStyle vesselNameStyle;
        private GUIStyle stateTextStyle;
        //Player status dictionaries
        private List<int> activeSubspaces;
        private Dictionary<int, List<string>> subspacePlayers;
        private double lastStatusUpdate;
        //const
        private const float WINDOW_HEIGHT = 400;
        private const float WINDOW_WIDTH = 300;
        private const float UPDATE_STATUS_INTERVAL = .2f;

        public PlayerStatusWindow(Client parent)
        {
            //Main setup
            display = false;
            disconnectEventHandled = true;
            this.parent = parent;
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width * 0.9f - WINDOW_WIDTH, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
            minWindowRect = new Rect(windowRect);
            minWindowRect.xMax = minWindowRect.xMin + 40;
            minWindowRect.yMax = minWindowRect.yMin + 20;
            moveRect = new Rect(0, 0, 10000, 20);

            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);
            scrollStyle = new GUIStyle(GUI.skin.scrollView);
            subspaceStyle = new GUIStyle();
            subspaceStyle.normal.background = new Texture2D(1, 1);
            subspaceStyle.normal.background.SetPixel(0, 0, Color.black);

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.MinWidth(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.MaxWidth(WINDOW_WIDTH);
            layoutOptions[2] = GUILayout.MinHeight(WINDOW_HEIGHT);
            layoutOptions[3] = GUILayout.MaxHeight(WINDOW_HEIGHT);

            minLayoutOptions = new GUILayoutOption[2];
            minLayoutOptions[0] = GUILayout.MinWidth(40);
            minLayoutOptions[1] = GUILayout.MinHeight(20);

            //Adapted from KMP.
            playerNameStyle = new GUIStyle(GUI.skin.label);
            playerNameStyle.normal.textColor = Color.white;
            playerNameStyle.hover.textColor = Color.white;
            playerNameStyle.active.textColor = Color.white;
            playerNameStyle.fontStyle = FontStyle.Bold;
            playerNameStyle.stretchWidth = true;
            playerNameStyle.wordWrap = false;

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

            activeSubspaces = new List<int>();
            subspacePlayers = new Dictionary<int, List<string>>();
        }

        public void Update()
        {
            if (display)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastStatusUpdate) > UPDATE_STATUS_INTERVAL)
                {
                    lastStatusUpdate = UnityEngine.Time.realtimeSinceStartup;
                    activeSubspaces = parent.warpWorker.GetActiveSubspaces();
                    subspacePlayers.Clear();
                    foreach (int subspace in activeSubspaces)
                    {
                        subspacePlayers.Add(subspace, parent.warpWorker.GetClientsInSubspace(subspace));
                    }
                }
            }
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
                if (!safeMinimized)
                {
                    windowRect = GUILayout.Window(GUIUtility.GetControlID(6703, FocusType.Passive), windowRect, DrawContent, "DarkMultiPlayer - Status", windowStyle, layoutOptions);
                }
                else
                {
                    minWindowRect = GUILayout.Window(GUIUtility.GetControlID(6703, FocusType.Passive), minWindowRect, DrawMaximize, "", windowStyle, minLayoutOptions);
                }
            }
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            parent.chatWindow.display = GUILayout.Toggle(parent.chatWindow.display, "C", buttonStyle);
            if (GUILayout.Button("-", buttonStyle))
            {
                minWindowRect.xMax = windowRect.xMax;
                minWindowRect.yMin = windowRect.yMin;
                minWindowRect.xMin = minWindowRect.xMax - 40;
                minWindowRect.yMax = minWindowRect.yMin + 20;
                minmized = true;
            }
            GUILayout.EndHorizontal();
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, scrollStyle);
            foreach (int activeSubspace in activeSubspaces)
            {
                double ourtime = (parent.timeSyncer.currentSubspace != -1) ? parent.timeSyncer.GetUniverseTime() : Planetarium.GetUniversalTime();
                double diffTime = parent.timeSyncer.GetUniverseTime(activeSubspace) - ourtime;
                string diffState = "NOW";
                if (activeSubspace != -1)
                {
                    if (activeSubspace != parent.timeSyncer.currentSubspace)
                    {
                        diffState = (diffTime > 0) ? SecondsToVeryShortString((int)diffTime) + " in the future" : SecondsToVeryShortString(-(int)diffTime) + " in the past";
                    }
                }
                else
                {
                    diffState = "Unknown";
                }
                GUILayout.BeginHorizontal(subspaceStyle);
                GUILayout.Label("T+ " + SecondsToShortString((int)parent.timeSyncer.GetUniverseTime(activeSubspace)) + " - " + diffState);
                if ((activeSubspace != parent.timeSyncer.currentSubspace) && (activeSubspace != -1))
                {
                    GUILayout.FlexibleSpace();
                    if (parent.warpWorker.warpMode == WarpMode.SUBSPACE)
                    {
                        if (GUILayout.Button("Sync", buttonStyle))
                        {
                            parent.timeSyncer.LockSubspace(activeSubspace);
                        }
                    }
                }
                GUILayout.EndHorizontal();
                foreach (string activeclient in subspacePlayers[activeSubspace])
                {
                    if (activeclient == parent.settings.playerName)
                    {
                        DrawPlayerEntry(parent.playerStatusWorker.myPlayerStatus);
                    }
                    else
                    {
                        DrawPlayerEntry(parent.playerStatusWorker.GetPlayerStatus(activeclient));
                    }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            displayNTP = GUILayout.Toggle(displayNTP, "Display subspace status", buttonStyle);
            if (displayNTP)
            {
                string ntpText = "Warp rate: " + Math.Round(Time.timeScale , 3) + "x.\n";
                ntpText += "Current subspace: " + parent.timeSyncer.currentSubspace + ".\n";
                ntpText += "Current Error: " + Math.Round((parent.timeSyncer.GetCurrentError() * 1000), 0) + " ms.\n";
                ntpText += "Current universe time: " + Math.Round(Planetarium.GetUniversalTime(), 3) + " UT\n";
                ntpText += "Network latency: " + Math.Round((parent.timeSyncer.networkLatencyAverage / 10000f), 3) + " ms\n";
                ntpText += "Server clock difference: " + Math.Round((parent.timeSyncer.clockOffsetAverage / 10000f), 3) + " ms\n";
                ntpText += "Server lag: " + Math.Round((parent.timeSyncer.serverLag / 10000f), 3) + " ms\n";
                GUILayout.Label(ntpText);
            }
            if (GUILayout.Button("Disconnect", buttonStyle))
            {
                disconnectEventHandled = false;
            }
            GUILayout.EndVertical();
        }

        private string SecondsToLongString(int time)
        {
            //Every month is feburary ok?
            int years = time / (60 * 60 * 24 * 7 * 4 * 12);
            time -= years * (60 * 60 * 24 * 7 * 4 * 12);
            int months = time / (60 * 60 * 24 * 7 * 4);
            time -= months * (60 * 60 * 24 * 7 * 4);
            int weeks = time / (60 * 60 * 24 * 7);
            time -= weeks * (60 * 60 * 24 * 7);
            int days = time / (60 * 60 * 24);
            time -= days * (60 * 60 * 24);
            int hours = time / (60 * 60);
            time -= hours * (60 * 60);
            int minutes = time / 60;
            time -= minutes * 60;
            int seconds = time;
            string returnString = "";
            if (years > 0)
            {
                if (years == 1)
                {
                    returnString += "1 year";
                }
                else
                {
                    returnString += years + " years";
                }
            }
            if (returnString != "")
            {
                returnString += ", ";
            }
            if (months > 0)
            {
                if (months == 1)
                {
                    returnString += "1 month";
                }
                else
                {
                    returnString += months + " month";
                }
            }
            if (returnString != "")
            {
                returnString += ", ";
            }
            if (weeks > 0)
            {
                if (weeks == 1)
                {
                    returnString += "1 week";
                }
                else
                {
                    returnString += weeks + " weeks";
                }
            }
            if (returnString != "")
            {
                returnString += ", ";
            }
            if (days > 0)
            {
                if (days == 1)
                {
                    returnString += "1 day";
                }
                else
                {
                    returnString += days + " days";
                }
            }
            if (returnString != "")
            {
                returnString += ", ";
            }
            if (hours > 0)
            {
                if (hours == 1)
                {
                    returnString += "1 hour";
                }
                else
                {
                    returnString += hours + " hours";
                }
            }
            if (returnString != "")
            {
                returnString += ", ";
            }
            if (minutes > 0)
            {
                if (minutes == 1)
                {
                    returnString += "1 minute";
                }
                else
                {
                    returnString += minutes + " minutes";
                }
            }
            if (returnString != "")
            {
                returnString += ", ";
            }
            if (seconds == 1)
            {
                returnString += "1 second";
            }
            else
            {
                returnString += seconds + " seconds";
            }
            return returnString;
        }

        private string SecondsToShortString(int time)
        {
            int years = time / (60 * 60 * 24 * 7 * 4 * 12);
            time -= years * (60 * 60 * 24 * 7 * 4 * 12);
            int months = time / (60 * 60 * 24 * 7 * 4);
            time -= months * (60 * 60 * 24 * 7 * 4);
            int weeks = time / (60 * 60 * 24 * 7);
            time -= weeks * (60 * 60 * 24 * 7);
            int days = time / (60 * 60 * 24);
            time -= days * (60 * 60 * 24);
            int hours = time / (60 * 60);
            time -= hours * (60 * 60);
            int minutes = time / 60;
            time -= minutes * 60;
            int seconds = time;
            string returnString = "";
            if (years > 0)
            {
                if (years == 1)
                {
                    returnString += "1y, ";
                }
                else
                {
                    returnString += years + "y, ";
                }
            }
            if (months > 0)
            {
                if (months == 1)
                {
                    returnString += "1m, ";
                }
                else
                {
                    returnString += months + "m, ";
                }
            }
            if (weeks > 0)
            {
                if (weeks == 1)
                {
                    returnString += "1w, ";
                }
                else
                {
                    returnString += weeks + "w, ";
                }
            }
            if (days > 0)
            {
                if (days == 1)
                {
                    returnString += "1d, ";
                }
                else
                {
                    returnString += days + "d, ";
                }
            }
            returnString += hours.ToString("00") + ":" + minutes.ToString("00") + ":" + seconds.ToString("00");
            return returnString;
        }

        private string SecondsToVeryShortString(int time)
        {
            int years = time / (60 * 60 * 24 * 7 * 4 * 12);
            time -= years * (60 * 60 * 24 * 7 * 4 * 12);
            int months = time / (60 * 60 * 24 * 7 * 4);
            time -= months * (60 * 60 * 24 * 7 * 4);
            int weeks = time / (60 * 60 * 24 * 7);
            time -= weeks * (60 * 60 * 24 * 7);
            int days = time / (60 * 60 * 24);
            time -= days * (60 * 60 * 24);
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
                    return years + " years";
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
                    return months + " months";
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
                    return weeks + " weeks";
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
                    return days + " days";
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
                    return hours + " hours";
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
                    return minutes + " minutes";
                }
            }
            if (seconds == 1)
            {
                return "1 second";
            }
            else
            {
                return seconds + " seconds";
            }
        }

        private void DrawMaximize(int windowID)
        {
            GUI.DragWindow(moveRect);
            GUILayout.BeginVertical();
            if (GUILayout.Button("+", buttonStyle))
            {
                windowRect.xMax = minWindowRect.xMax;
                windowRect.yMin = minWindowRect.yMin;
                windowRect.xMin = minWindowRect.xMax - WINDOW_WIDTH;
                windowRect.yMax = minWindowRect.yMin + WINDOW_HEIGHT;
                minmized = false;
            }
            GUILayout.EndVertical();
        }

        private void DrawPlayerEntry(PlayerStatus playerStatus)
        {
            if (playerStatus == null)
            {
                //Just connected or disconnected.
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(playerStatus.playerName, playerNameStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(playerStatus.statusText, stateTextStyle);
            GUILayout.EndHorizontal();
            if (playerStatus.vesselText != "")
            {
                GUILayout.Label("Pilot: " + playerStatus.vesselText, vesselNameStyle);
            }
        }
    }
}

