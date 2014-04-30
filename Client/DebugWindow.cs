using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class DebugWindow
    {
        public bool display;
        private bool safeDisplay;
        private bool initialized;
        private Client parent;
        //private parts
        private bool displayFast;
        private bool displayNTP;
        private bool displayConnectionQueue;
        private string ntpText;
        private string connectionText;
        private float lastUpdateTime;
        //GUI Layout
        private Rect windowRect;
        private Rect moveRect;
        private GUILayoutOption[] layoutOptions;
        private GUILayoutOption[] textAreaOptions;
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;
        //const
        private const float WINDOW_HEIGHT = 400;
        private const float WINDOW_WIDTH = 300;
        private const float DISPLAY_UPDATE_INTERVAL = .2f;

        public DebugWindow(Client parent)
        {
            //Main setup
            display = false;
            this.parent = parent;
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width - (WINDOW_WIDTH + 50), (Screen.height / 2f) - (WINDOW_HEIGHT / 2f), WINDOW_WIDTH, WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.MinWidth(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.MaxWidth(WINDOW_WIDTH);
            layoutOptions[2] = GUILayout.MinHeight(WINDOW_HEIGHT);
            layoutOptions[3] = GUILayout.MaxHeight(WINDOW_HEIGHT);

            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);

            textAreaOptions = new GUILayoutOption[1];
            textAreaOptions[0] = GUILayout.ExpandWidth(true);

            labelStyle = new GUIStyle(GUI.skin.label);
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
                windowRect = GUILayout.Window(GUIUtility.GetControlID(6705, FocusType.Passive), windowRect, DrawContent, "DarkMultiPlayer - Debug", windowStyle, layoutOptions);
            }
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            displayFast = GUILayout.Toggle(displayFast, "Fast debug update", buttonStyle);
            displayNTP = GUILayout.Toggle(displayNTP, "Display NTP/Subspace statistics", buttonStyle);
            if (displayNTP)
            {
                GUILayout.Label(ntpText, labelStyle);
            }
            displayConnectionQueue = GUILayout.Toggle(displayConnectionQueue, "Display connection queue statistics", buttonStyle);
            if (displayConnectionQueue)
            {
                GUILayout.Label(connectionText, labelStyle);
            }
            GUILayout.EndVertical();
        }

        public void Update()
        {
            safeDisplay = display;
            if (((UnityEngine.Time.realtimeSinceStartup - lastUpdateTime) > DISPLAY_UPDATE_INTERVAL) || displayFast)
            {
                lastUpdateTime = UnityEngine.Time.realtimeSinceStartup;
                //TODO: Fix up whatever is throwing later.
                try
                {
                    //NTP text
                    ntpText = "Warp rate: " + Math.Round(Time.timeScale, 3) + "x.\n";
                    ntpText += "Current subspace: " + parent.timeSyncer.currentSubspace + ".\n";
                    ntpText += "Current Error: " + Math.Round((parent.timeSyncer.GetCurrentError() * 1000), 0) + " ms.\n";
                    ntpText += "Current universe time: " + Math.Round(Planetarium.GetUniversalTime(), 3) + " UT\n";
                    ntpText += "Network latency: " + Math.Round((parent.timeSyncer.networkLatencyAverage / 10000f), 3) + " ms\n";
                    ntpText += "Server clock difference: " + Math.Round((parent.timeSyncer.clockOffsetAverage / 10000f), 3) + " ms\n";
                    ntpText += "Server lag: " + Math.Round((parent.timeSyncer.serverLag / 10000f), 3) + " ms\n";

                    //Connection queue text
                    connectionText = "";
                    connectionText += "Last send time: " + parent.networkWorker.GetStatistics("LastSendTime") + "ms.\n";
                    connectionText += "Last receive time: " + parent.networkWorker.GetStatistics("LastReceiveTime") + "ms.\n";
                    connectionText += "Queued outgoing messages (High): " + parent.networkWorker.GetStatistics("HighPriorityQueueLength") + "\n";
                    connectionText += "Queued outgoing messages (Split): " + parent.networkWorker.GetStatistics("SplitPriorityQueueLength") + "\n";
                    connectionText += "Queued outgoing messages (Low): " + parent.networkWorker.GetStatistics("LowPriorityQueueLength") + "\n";
                    connectionText += "Stored future updates: " + parent.vesselWorker.GetStatistics("StoredFutureUpdates") + "\n";
                    connectionText += "Stored future proto updates: " + parent.vesselWorker.GetStatistics("StoredFutureProtoUpdates") + "\n";
                }
                catch
                {
                    ntpText = "";
                    connectionText = "";
                }
            }
        }
    }
}

