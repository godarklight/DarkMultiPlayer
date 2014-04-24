using System;
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
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle warpButtonStyle;
        private GUIStyle scrollStyle;
        //Shamelessly stolen from KMP
        private GUIStyle playerNameStyle;
        private GUIStyle vesselNameStyle;
        private GUIStyle stateTextStyle;
        //const
        private const float WINDOW_HEIGHT = 400;
        private const float WINDOW_WIDTH = 400;

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

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.MinWidth(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.MaxWidth(WINDOW_WIDTH);
            layoutOptions[2] = GUILayout.MinHeight(WINDOW_HEIGHT);
            layoutOptions[3] = GUILayout.MaxHeight(WINDOW_HEIGHT);

            minLayoutOptions = new GUILayoutOption[2];
            minLayoutOptions[0] = GUILayout.MinWidth(40);
            minLayoutOptions[1] = GUILayout.MinHeight(20);

            //"Borrowed" from KMP.
            playerNameStyle = new GUIStyle(GUI.skin.label);
            playerNameStyle.normal.textColor = Color.white;
            playerNameStyle.hover.textColor = Color.white;
            playerNameStyle.active.textColor = Color.white;
            playerNameStyle.alignment = TextAnchor.MiddleLeft;
            playerNameStyle.margin = new RectOffset(0, 0, 2, 0);
            playerNameStyle.padding = new RectOffset(0, 0, 0, 0);
            playerNameStyle.stretchWidth = true;
            playerNameStyle.fontStyle = FontStyle.Bold;

            vesselNameStyle = new GUIStyle(GUI.skin.label);
            vesselNameStyle.normal.textColor = Color.white;
            vesselNameStyle.stretchWidth = true;
            vesselNameStyle.fontStyle = FontStyle.Bold;
            vesselNameStyle.margin = new RectOffset(4, 0, 0, 0);
            vesselNameStyle.alignment = TextAnchor.LowerLeft;
            vesselNameStyle.padding = new RectOffset(0, 0, 0, 0);

            stateTextStyle = new GUIStyle(GUI.skin.label);
            stateTextStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            stateTextStyle.margin = new RectOffset(4, 0, 0, 0);
            stateTextStyle.padding = new RectOffset(0, 0, 0, 0);
            stateTextStyle.stretchWidth = true;
            stateTextStyle.fontStyle = FontStyle.Normal;
            stateTextStyle.fontSize = 12;

            warpButtonStyle = new GUIStyle(GUI.skin.button);
            warpButtonStyle.fontSize = 10;
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
            DrawPlayerEntry(parent.playerStatusWorker.myPlayerStatus);
            foreach (PlayerStatus playerStatus in parent.playerStatusWorker.playerStatusList)
            {
                DrawPlayerEntry(playerStatus);
                int clientSubspace = parent.warpWorker.GetClientSubspace(playerStatus.playerName);
                if ((parent.warpWorker.warpMode == WarpMode.SUBSPACE) && (parent.timeSyncer.currentSubspace != -1) && (clientSubspace != -1) && (clientSubspace != parent.timeSyncer.currentSubspace))
                {
                    double timeDiff = parent.timeSyncer.GetUniverseTime(clientSubspace) - parent.timeSyncer.GetUniverseTime();
                    if (timeDiff > 0)
                    {
                        if (GUILayout.Button("Sync to " + playerStatus.playerName + " (" + SecondsToString(timeDiff) + " in the future)", warpButtonStyle))
                        {
                            parent.timeSyncer.LockSubspace(clientSubspace);
                        }
                    }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            displayNTP = GUILayout.Toggle(displayNTP, "Display subspace status", buttonStyle);
            if (displayNTP)
            {
                string ntpText = "Current subspace: " + parent.timeSyncer.currentSubspace + ".\n";
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

        private string SecondsToString(double time)
        {
            int years = (int)time / (60 * 60 * 24 * 7 * 4 * 52);
            time -= years * (60 * 60 * 24 * 7 * 4 * 52);
            //Every month is feburary ok?
            int months = (int)time / (60 * 60 * 24 * 7 * 4);
            time -= months * (60 * 60 * 24 * 7 * 4);
            int weeks = (int)time / (60 * 60 * 24 * 7);
            time -= weeks * (60 * 60 * 24 * 7);
            int days = (int)time / (60 * 60 * 24);
            time -= days * (60 * 60 * 24);
            int hours = (int)time / (60 * 60);
            time -= hours * (60 * 60);
            int minutes = (int)time / 60;
            time -= minutes * 60;
            int seconds = (int)time;
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
            if (seconds > 0)
            {
                if (days == 1)
                {
                    returnString += "1 second";
                }
                else
                {
                    returnString += seconds + " seconds";
                }
            }
            return returnString;
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
            GUILayout.Label(playerStatus.playerName, playerNameStyle);
            if (playerStatus.vesselText != "")
            {
                GUILayout.Label(playerStatus.vesselText, vesselNameStyle);
                GUILayout.Label(playerStatus.statusText, stateTextStyle);
            }
            else
            {
                GUILayout.Label(playerStatus.statusText, stateTextStyle);
            }
        }
    }
}

