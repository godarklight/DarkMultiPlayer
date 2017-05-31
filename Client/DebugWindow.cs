using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class DebugWindow
    {
        public bool display = false;
        private bool safeDisplay = false;
        private bool initialized = false;
        private bool isWindowLocked = false;
        //private parts
        private bool displayFast;
        private bool displayVectors;
        private bool displayNTP;
        private bool displayConnectionQueue;
        private bool displayDynamicTickStats;
        private bool displayRequestedRates;
        private bool displayProfilerStatistics;
        private string vectorText = "";
        private string ntpText = "";
        private string connectionText = "";
        private string dynamicTickText = "";
        private string requestedRateText = "";
        private string profilerText = "";
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
        private const float WINDOW_WIDTH = 350;
        private const float DISPLAY_UPDATE_INTERVAL = .2f;
        //Services
        private DMPGame dmpGame;
        private Settings dmpSettings;
        private TimeSyncer timeSyncer;
        private NetworkWorker networkWorker;
        private VesselWorker vesselWorker;
        private DynamicTickWorker dynamicTickWorker;
        private WarpWorker warpWorker;

        public DebugWindow(DMPGame dmpGame, Settings dmpSettings, TimeSyncer timeSyncer, NetworkWorker networkWorker, VesselWorker vesselWorker, DynamicTickWorker dynamicTickWorker, WarpWorker warpWorker)
        {
            this.dmpGame = dmpGame;
            this.dmpSettings = dmpSettings;
            this.timeSyncer = timeSyncer;
            this.networkWorker = networkWorker;
            this.vesselWorker = vesselWorker;
            this.dynamicTickWorker = dynamicTickWorker;
            this.warpWorker = warpWorker;
            dmpGame.updateEvent.Add(Update);
            dmpGame.drawEvent.Add(Draw);
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
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6705 + Client.WINDOW_OFFSET, windowRect, DrawContent, "DarkMultiPlayer - Debug", windowStyle, layoutOptions));
            }
            CheckWindowLock();
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            GameEvents.debugEvents = GUILayout.Toggle(GameEvents.debugEvents, "Debug GameEvents", buttonStyle);
            displayFast = GUILayout.Toggle(displayFast, "Fast debug update", buttonStyle);
            displayVectors = GUILayout.Toggle(displayVectors, "Display vessel vectors", buttonStyle);
            if (displayVectors)
            {
                GUILayout.Label(vectorText, labelStyle);
            }
            displayNTP = GUILayout.Toggle(displayNTP, "Display NTP/Subspace statistics", buttonStyle);
            if (displayNTP)
            {
                GUILayout.Label(ntpText, labelStyle);
            }
            displayConnectionQueue = GUILayout.Toggle(displayConnectionQueue, "Display connection statistics", buttonStyle);
            if (displayConnectionQueue)
            {
                GUILayout.Label(connectionText, labelStyle);
            }
            displayDynamicTickStats = GUILayout.Toggle(displayDynamicTickStats, "Display dynamic tick statistics", buttonStyle);
            if (displayDynamicTickStats)
            {
                GUILayout.Label(dynamicTickText, labelStyle);
            }
            displayRequestedRates = GUILayout.Toggle(displayRequestedRates, "Display requested rates", buttonStyle);
            if (displayRequestedRates)
            {
                GUILayout.Label(requestedRateText, labelStyle);
            }
            displayProfilerStatistics = GUILayout.Toggle(displayProfilerStatistics, "Display Profiler Statistics", buttonStyle);
            if (displayProfilerStatistics)
            {
                if (System.Diagnostics.Stopwatch.IsHighResolution)
                {
                    if (GUILayout.Button("Reset Profiler history", buttonStyle))
                    {
                        Profiler.updateData = new ProfilerData();
                        Profiler.fixedUpdateData = new ProfilerData();
                        Profiler.guiData = new ProfilerData();
                    }
                    GUILayout.Label("Timer resolution: " + System.Diagnostics.Stopwatch.Frequency + " hz", labelStyle);
                    GUILayout.Label(profilerText, labelStyle);
                }
                else
                {
                    GUILayout.Label("Timer resolution: " + System.Diagnostics.Stopwatch.Frequency + " hz", labelStyle);
                    GUILayout.Label("Profiling statistics unavailable without a high resolution timer");
                }
            }
            GUILayout.EndVertical();
        }

        private void Update()
        {
            safeDisplay = display;
            if (display)
            {
                if (((Client.realtimeSinceStartup - lastUpdateTime) > DISPLAY_UPDATE_INTERVAL) || displayFast)
                {
                    lastUpdateTime = Client.realtimeSinceStartup;
                    //Vector text
                    if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.ready && FlightGlobals.fetch.activeVessel != null)
                    {
                        Vessel ourVessel = FlightGlobals.fetch.activeVessel;
                        vectorText = "Forward vector: " + ourVessel.GetFwdVector() + "\n";
                        vectorText += "Up vector: " + (Vector3)ourVessel.upAxis + "\n";
                        vectorText += "Srf Rotation: " + ourVessel.srfRelRotation + "\n";
                        vectorText += "Vessel Rotation: " + ourVessel.transform.rotation + "\n";
                        vectorText += "Vessel Local Rotation: " + ourVessel.transform.localRotation + "\n";
                        vectorText += "mainBody Rotation: " + (Quaternion)ourVessel.mainBody.rotation + "\n";
                        vectorText += "mainBody Transform Rotation: " + (Quaternion)ourVessel.mainBody.bodyTransform.rotation + "\n";
                        vectorText += "Surface Velocity: " + ourVessel.GetSrfVelocity() + ", |v|: " + ourVessel.GetSrfVelocity().magnitude + "\n";
                        vectorText += "Orbital Velocity: " + ourVessel.GetObtVelocity() + ", |v|: " + ourVessel.GetObtVelocity().magnitude + "\n";
                        if (ourVessel.orbitDriver != null && ourVessel.orbitDriver.orbit != null)
                        {
                            vectorText += "Frame Velocity: " + (Vector3)ourVessel.orbitDriver.orbit.GetFrameVel() + ", |v|: " + ourVessel.orbitDriver.orbit.GetFrameVel().magnitude + "\n";
                        }
                        vectorText += "CoM offset vector: " + ourVessel.CoM.ToString() + "\n";
                        vectorText += "Angular Velocity: " + ourVessel.angularVelocity + ", |v|: " + ourVessel.angularVelocity.magnitude + "\n";
                        vectorText += "World Pos: " + (Vector3)ourVessel.GetWorldPos3D() + ", |pos|: " + ourVessel.GetWorldPos3D().magnitude + "\n";
                    }
                    else
                    {
                        vectorText = "You have to be in flight";
                    }

                    //NTP text
                    ntpText = "Warp rate: " + Math.Round(Time.timeScale, 3) + "x.\n";
                    ntpText += "Current subspace: " + timeSyncer.currentSubspace + ".\n";
                    if (timeSyncer.locked)
                    {
                        ntpText += "Current subspace rate: " + Math.Round(timeSyncer.lockedSubspace.subspaceSpeed, 3) + "x.\n";
                    }
                    else
                    {
                        ntpText += "Current subspace rate: " + Math.Round(timeSyncer.requestedRate, 3) + "x.\n";
                    }
                    ntpText += "Current Error: " + Math.Round((timeSyncer.GetCurrentError() * 1000), 0) + " ms.\n";
                    ntpText += "Current universe time: " + Math.Round(Planetarium.GetUniversalTime(), 3) + " UT\n";
                    ntpText += "Network latency: " + Math.Round((timeSyncer.networkLatencyAverage / 10000f), 3) + " ms\n";
                    ntpText += "Server clock difference: " + Math.Round((timeSyncer.clockOffsetAverage / 10000f), 3) + " ms\n";
                    ntpText += "Server lag: " + Math.Round((timeSyncer.serverLag / 10000f), 3) + " ms\n";

                    //Connection queue text
                    connectionText = "Last send time: " + networkWorker.GetStatistics("LastSendTime") + "ms.\n";
                    connectionText += "Last receive time: " + networkWorker.GetStatistics("LastReceiveTime") + "ms.\n";
                    connectionText += "Queued outgoing messages (High): " + networkWorker.GetStatistics("HighPriorityQueueLength") + ".\n";
                    connectionText += "Queued outgoing messages (Split): " + networkWorker.GetStatistics("SplitPriorityQueueLength") + ".\n";
                    connectionText += "Queued outgoing messages (Low): " + networkWorker.GetStatistics("LowPriorityQueueLength") + ".\n";
                    connectionText += "Queued out bytes: " + networkWorker.GetStatistics("QueuedOutBytes") + ".\n";
                    connectionText += "Sent bytes: " + networkWorker.GetStatistics("SentBytes") + ".\n";
                    connectionText += "Received bytes: " + networkWorker.GetStatistics("ReceivedBytes") + ".\n";
                    connectionText += "Stored future updates: " + vesselWorker.GetStatistics("StoredFutureUpdates") + "\n";
                    connectionText += "Stored future proto updates: " + vesselWorker.GetStatistics("StoredFutureProtoUpdates") + ".\n";

                    //Dynamic tick text
                    dynamicTickText = "Current tick rate: " + dynamicTickWorker.sendTickRate + "hz.\n";
                    dynamicTickText += "Current max secondry vessels: " + dynamicTickWorker.maxSecondryVesselsPerTick + ".\n";

                    //Requested rates text
                    requestedRateText = dmpSettings.playerName + ": " + Math.Round(timeSyncer.requestedRate, 3) + "x.\n";
                    foreach (KeyValuePair<string, float> playerEntry in warpWorker.clientSkewList)
                    {
                        requestedRateText += playerEntry.Key + ": " + Math.Round(playerEntry.Value, 3) + "x.\n";
                    }

                    profilerText = "Update: \n" + Profiler.updateData;
                    profilerText += "Fixed Update: \n" + Profiler.fixedUpdateData;
                    profilerText += "GUI: \n" + Profiler.guiData;
                }
            }
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

            if (safeDisplay)
            {
                Vector2 mousePos = Input.mousePosition;
                mousePos.y = Screen.height - mousePos.y;

                bool shouldLock = windowRect.Contains(mousePos);

                if (shouldLock && !isWindowLocked)
                {
                    InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, "DMP_DebugLock");
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
                InputLockManager.RemoveControlLock("DMP_DebugLock");
            }
        }

        public void Stop()
        {
            display = false;
            RemoveWindowLock();
            dmpGame.updateEvent.Remove(Update);
            dmpGame.drawEvent.Remove(Draw);
        }
    }
}

