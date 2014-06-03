using System;
using System.Collections.Generic;
using UnityEngine;
using MessageStream;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class ScreenshotWorker
    {
        //Height setting
        public int screenshotHeight = 720;
        private static ScreenshotWorker singleton;
        //GUI stuff
        private bool initialized;
        public bool workerEnabled;
        private GUIStyle windowStyle;
        private GUILayoutOption[] windowLayoutOption;
        private GUIStyle buttonStyle;
        private GUIStyle highlightStyle;
        private GUILayoutOption[] fixedButtonSizeOption;
        private GUIStyle scrollStyle;
        public bool display;
        private bool safeDisplay;
        private Rect windowRect;
        private Rect moveRect;
        private Vector2 scrollPos;
        //State tracking
        public bool screenshotButtonHighlighted;
        List<string> highlightedPlayers = new List<string>();
        private string selectedPlayer = "";
        private string safeSelectedPlayer = "";
        private Dictionary<string, Texture2D> screenshots = new Dictionary<string, Texture2D>();
        private bool uploadEventHandled = true;
        public bool uploadScreenshot = false;
        private float lastScreenshotSend;
        private Queue<ScreenshotEntry> newScreenshotQueue = new Queue<ScreenshotEntry>();
        private Queue<ScreenshotWatchEntry> newScreenshotWatchQueue = new Queue<ScreenshotWatchEntry>();
        private Queue<string> newScreenshotNotifiyQueue = new Queue<string>();
        private Dictionary<string, string> watchPlayers = new Dictionary<string, string>();
        //Screenshot uploading message
        private bool displayScreenshotUploadingMessage = false;
        public bool finishedUploadingScreenshot = false;
        private float lastScreenshotMessageCheck;
        ScreenMessage screenshotUploadMessage;
        //delay the screenshot message until we've taken a screenshot
        public bool screenshotTaken;

        //const
        private const float MIN_WINDOW_HEIGHT = 200;
        private const float MIN_WINDOW_WIDTH = 150;
        private const float BUTTON_WIDTH = 150;
        private const float SCREENSHOT_MESSAGE_CHECK_INTERVAL = .2f;
        private const float MIN_SCREENSHOT_SEND_INTERVAL = 3f;

        public static ScreenshotWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        private void InitGUI()
        {
            windowRect = new Rect(50, (Screen.height / 2f) - (MIN_WINDOW_HEIGHT / 2f), MIN_WINDOW_WIDTH, MIN_WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);
            windowLayoutOption = new GUILayoutOption[4];
            windowLayoutOption[0] = GUILayout.MinWidth(MIN_WINDOW_WIDTH);
            windowLayoutOption[1] = GUILayout.MinHeight(MIN_WINDOW_HEIGHT);
            windowLayoutOption[2] = GUILayout.ExpandWidth(true);
            windowLayoutOption[3] = GUILayout.ExpandHeight(true);
            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);
            highlightStyle = new GUIStyle(GUI.skin.button);
            highlightStyle.normal.textColor = Color.red;
            highlightStyle.active.textColor = Color.red;
            highlightStyle.hover.textColor = Color.red;
            fixedButtonSizeOption = new GUILayoutOption[2];
            fixedButtonSizeOption[0] = GUILayout.Width(BUTTON_WIDTH);
            fixedButtonSizeOption[1] = GUILayout.ExpandWidth(true);
            scrollStyle = new GUIStyle(GUI.skin.scrollView);
            scrollPos = new Vector2(0, 0);
        }

        private void Update()
        {
            safeDisplay = display;

            if (workerEnabled)
            {

                while (newScreenshotNotifiyQueue.Count > 0)
                {
                    string notifyPlayer = newScreenshotNotifiyQueue.Dequeue();
                    if (!display)
                    {
                        screenshotButtonHighlighted = true;
                    }
                    if (selectedPlayer != notifyPlayer)
                    {
                        if (!highlightedPlayers.Contains(notifyPlayer))
                        {
                            highlightedPlayers.Add(notifyPlayer);
                        }
                    }
                    ChatWorker.fetch.QueueChannelMessage("Server", "", notifyPlayer + " shared screenshot");
                }

                //Update highlights
                if (screenshotButtonHighlighted && display)
                {
                    screenshotButtonHighlighted = false;
                }

                if (highlightedPlayers.Contains(selectedPlayer))
                {
                    highlightedPlayers.Remove(selectedPlayer);
                }


                while (newScreenshotQueue.Count > 0)
                {
                    ScreenshotEntry se = newScreenshotQueue.Dequeue();
                    Texture2D screenshotTexture = new Texture2D(4,4,TextureFormat.RGB24, false, true);
                    if (screenshotTexture.LoadImage(se.screenshotData))
                    {
                        screenshotTexture.Apply();
                        //Make sure screenshots aren't bigger than 2/3rds of the screen.
                        ResizeTextureIfNeeded(ref screenshotTexture);
                        //Save the texture in memory
                        screenshots[se.fromPlayer] = screenshotTexture;
                        DarkLog.Debug("Loaded screenshot from " + se.fromPlayer);
                    }
                    else
                    {
                        DarkLog.Debug("Error loading screenshot from " + se.fromPlayer);
                    }
                }

                while (newScreenshotWatchQueue.Count > 0)
                {
                    ScreenshotWatchEntry swe = newScreenshotWatchQueue.Dequeue();
                    if (swe.watchPlayer != "")
                    {
                        watchPlayers[swe.fromPlayer] = swe.watchPlayer;
                    }
                    else
                    {
                        if (watchPlayers.ContainsKey(swe.fromPlayer))
                        {
                            watchPlayers.Remove(swe.fromPlayer);
                        }
                    }
                }

                if (safeSelectedPlayer != selectedPlayer)
                {
                    windowRect.height = 0;
                    windowRect.width = 0;
                    safeSelectedPlayer = selectedPlayer;
                    WatchPlayer(selectedPlayer);
                }

                if (Input.GetKey(KeyCode.F8))
                {
                    uploadEventHandled = false;
                }

                if (!uploadEventHandled)
                {
                    uploadEventHandled = true;
                    if ((UnityEngine.Time.realtimeSinceStartup - lastScreenshotSend) > MIN_SCREENSHOT_SEND_INTERVAL)
                    {
                        lastScreenshotSend = UnityEngine.Time.realtimeSinceStartup;
                        screenshotTaken = false;
                        finishedUploadingScreenshot = false;
                        uploadScreenshot = true;
                        displayScreenshotUploadingMessage = true;
                    }
                }

                if (screenshotTaken && displayScreenshotUploadingMessage && ((UnityEngine.Time.realtimeSinceStartup - lastScreenshotMessageCheck) > SCREENSHOT_MESSAGE_CHECK_INTERVAL))
                {
                    lastScreenshotMessageCheck = UnityEngine.Time.realtimeSinceStartup;
                    if (screenshotUploadMessage != null)
                    {
                        screenshotUploadMessage.duration = 0f;
                    }
                    if (finishedUploadingScreenshot)
                    {
                        displayScreenshotUploadingMessage = false;
                        screenshotUploadMessage = ScreenMessages.PostScreenMessage("Screenshot uploaded!", 2f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    else
                    {
                        screenshotUploadMessage = ScreenMessages.PostScreenMessage("Uploading screenshot...", 1f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }

            }
        }

        private void ResizeTextureIfNeeded(ref Texture2D screenshotTexture)
        {
            //Make sure screenshots aren't bigger than 2/3rds of the screen.
            int resizeWidth = (int)(Screen.width * .66);
            int resizeHeight = (int)(Screen.height * .66);
            if (screenshotTexture.width > resizeWidth || screenshotTexture.height > resizeHeight)
            {
                RenderTexture renderTexture = new RenderTexture(resizeWidth, resizeHeight, 24);
                renderTexture.useMipMap = false;
                Graphics.Blit(screenshotTexture, renderTexture);
                RenderTexture.active = renderTexture;
                Texture2D resizeTexture = new Texture2D(resizeWidth, resizeHeight, TextureFormat.RGB24, false);
                resizeTexture.ReadPixels(new Rect(0, 0, resizeWidth, resizeHeight), 0, 0);
                resizeTexture.Apply();
                screenshotTexture = resizeTexture;
                RenderTexture.active = null;
            }
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
                windowRect = GUILayout.Window(GUIUtility.GetControlID(6710, FocusType.Passive), windowRect, DrawContent, "Screenshots", windowStyle, windowLayoutOption);
            }
        }

        private void DrawContent(int windowID)
        {
            GUI.DragWindow(moveRect);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            scrollPos = GUILayout.BeginScrollView(scrollPos, scrollStyle, fixedButtonSizeOption);
            DrawPlayerButton(Settings.fetch.playerName);
            foreach (PlayerStatus player in PlayerStatusWorker.fetch.playerStatusList)
            {
                DrawPlayerButton(player.playerName);
            }
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            GUI.enabled = ((UnityEngine.Time.realtimeSinceStartup - lastScreenshotSend) > MIN_SCREENSHOT_SEND_INTERVAL);
            if (GUILayout.Button("Upload (F8)", buttonStyle))
            {
                uploadEventHandled = false;
            }
            GUI.enabled = true;
            GUILayout.EndVertical();
            if (safeSelectedPlayer != "")
            {
                if (screenshots.ContainsKey(safeSelectedPlayer))
                {
                    GUILayout.Box(screenshots[safeSelectedPlayer]);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawPlayerButton(string playerName)
        {
            GUIStyle playerButtonStyle = buttonStyle;
            if (highlightedPlayers.Contains(playerName))
            {
                playerButtonStyle = highlightStyle;
            }
            bool newValue = GUILayout.Toggle(safeSelectedPlayer == playerName, playerName, playerButtonStyle);
            if (newValue && (safeSelectedPlayer != playerName))
            {
                selectedPlayer = playerName;
            }
            if (!newValue && (safeSelectedPlayer == playerName))
            {
                selectedPlayer = "";
            }
        }

        private void WatchPlayer(string playerName)
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ScreenshotMessageType.WATCH);
                mw.Write<string>(Settings.fetch.playerName);
                mw.Write<string>(playerName);
                NetworkWorker.fetch.SendScreenshotMessage(mw.GetMessageBytes());
            }
        }

        //Called from main due to WaitForEndOfFrame timing.
        public void SendScreenshot()
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ScreenshotMessageType.SCREENSHOT);
                mw.Write<string>(Settings.fetch.playerName);
                mw.Write<byte[]>(GetScreenshotBytes());
                NetworkWorker.fetch.SendScreenshotMessage(mw.GetMessageBytes());
            }
        }
        //Adapted from KMP.
        private byte[] GetScreenshotBytes()
        {
            int screenshotWidth = (int)(Screen.width * (screenshotHeight / (float)Screen.height));

            //Read the screen pixels into a texture
            Texture2D fullScreenTexture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            fullScreenTexture.filterMode = FilterMode.Bilinear;
            fullScreenTexture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
            fullScreenTexture.Apply();

            RenderTexture renderTexture = new RenderTexture(screenshotWidth, screenshotHeight, 24);
            renderTexture.useMipMap = false;

            Graphics.Blit(fullScreenTexture, renderTexture); //Blit the screen texture to a render texture

            RenderTexture.active = renderTexture;

            //Read the pixels from the render texture into a Texture2D
            Texture2D resizedTexture = new Texture2D(screenshotWidth, screenshotHeight, TextureFormat.RGB24, false);
            Texture2D ourTexture = new Texture2D(screenshotWidth, screenshotHeight, TextureFormat.RGB24, false);
            resizedTexture.ReadPixels(new Rect(0, 0, screenshotWidth, screenshotHeight), 0, 0);
            resizedTexture.Apply();
            //Save a copy locally in case we need to resize it.
            ourTexture.ReadPixels(new Rect(0, 0, screenshotWidth, screenshotHeight), 0, 0);
            ourTexture.Apply();
            ResizeTextureIfNeeded(ref ourTexture);
            //Save our texture in memory.
            screenshots[Settings.fetch.playerName] = ourTexture;

            RenderTexture.active = null;
            return resizedTexture.EncodeToPNG();
        }

        public void QueueNewScreenshot(string fromPlayer, byte[] screenshotData)
        {
            ScreenshotEntry se = new ScreenshotEntry();
            se.fromPlayer = fromPlayer;
            se.screenshotData = screenshotData;
            newScreenshotQueue.Enqueue(se);
        }

        public void QueueNewScreenshotWatch(string fromPlayer, string watchPlayer)
        {
            ScreenshotWatchEntry swe = new ScreenshotWatchEntry();
            swe.fromPlayer = fromPlayer;
            swe.watchPlayer = watchPlayer;
            newScreenshotWatchQueue.Enqueue(swe);
        }

        public void QueueNewNotify(string fromPlayer)
        {
            newScreenshotNotifiyQueue.Enqueue(fromPlayer);
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    Client.updateEvent.Remove(singleton.Update);
                    Client.drawEvent.Remove(singleton.Draw);
                }
                singleton = new ScreenshotWorker();
                Client.updateEvent.Add(singleton.Update);
                Client.drawEvent.Add(singleton.Draw);
            }
        }
    }

    class ScreenshotEntry
    {
        public string fromPlayer;
        public byte[] screenshotData;
    }

    class ScreenshotWatchEntry
    {
        public string fromPlayer;
        public string watchPlayer;
    }
}

