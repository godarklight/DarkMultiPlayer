using System;
using System.Collections.Generic;
using System.IO;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class ScreenshotLibrary
    {
        private static Dictionary<string, int> playerUploadedScreenshotIndex = new Dictionary<string, int>();
        private static Dictionary<string, Dictionary<string, int>> playerDownloadedScreenshotIndex = new Dictionary<string, Dictionary<string, int>>();
        private static Dictionary<string, string> playerWatchScreenshot = new Dictionary<string, string>();

        public static void HandleScreenshotLibrary(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            string screenshotDirectory = Path.Combine(Server.universeDirectory, "Screenshots");
            if (Settings.settingsStore.screenshotDirectory != "")
            {
                if (Directory.Exists(Settings.settingsStore.screenshotDirectory))
                {
                    screenshotDirectory = Settings.settingsStore.screenshotDirectory;
                }
            }
            if (!Directory.Exists(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
            }
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                ScreenshotMessageType messageType = (ScreenshotMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                switch (messageType)
                {
                    case ScreenshotMessageType.SCREENSHOT:
                        {
                            if (Settings.settingsStore.screenshotsPerPlayer > -1)
                            {
                                string playerScreenshotDirectory = Path.Combine(screenshotDirectory, fromPlayer);
                                if (!Directory.Exists(playerScreenshotDirectory))
                                {
                                    Directory.CreateDirectory(playerScreenshotDirectory);
                                }
                                string screenshotFile = Path.Combine(playerScreenshotDirectory, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
                                DarkLog.Debug("Saving screenshot from " + fromPlayer);

                                byte[] screenshotData = mr.Read<byte[]>();

                                File.WriteAllBytes(screenshotFile, screenshotData);
                                if (Settings.settingsStore.screenshotsPerPlayer != 0)
                                {
                                    while (Directory.GetFiles(playerScreenshotDirectory).Length > Settings.settingsStore.screenshotsPerPlayer)
                                    {
                                        string[] currentFiles = Directory.GetFiles(playerScreenshotDirectory);
                                        string deleteFile = currentFiles[0];
                                        //Find oldest file
                                        foreach (string testFile in currentFiles)
                                        {
                                            if (File.GetCreationTime(testFile) < File.GetCreationTime(deleteFile))
                                            {
                                                deleteFile = testFile;
                                            }
                                        }
                                        File.Delete(deleteFile);
                                        DarkLog.Debug("Removing old screenshot " + Path.GetFileName(deleteFile));
                                    }
                                }

                                //Notify players that aren't watching that there's a new screenshot availabe. This only works if there's a file available on the server.
                                //The server does not keep the screenshots in memory.
                                NetworkMessage notifyMessage = NetworkMessage.Create((int)ServerMessageType.CHAT_MESSAGE, 2048, NetworkMessageType.ORDERED_RELIABLE);
                                using (MessageWriter mw = new MessageWriter(notifyMessage.data.data))
                                {
                                    mw.Write<int>((int)ScreenshotMessageType.NOTIFY);
                                    mw.Write(fromPlayer);
                                    notifyMessage.data.size = (int)mw.GetMessageLength();
                                }
                                ClientHandler.SendToAll(client, notifyMessage, false);
                            }
                            if (!playerUploadedScreenshotIndex.ContainsKey(fromPlayer))
                            {
                                playerUploadedScreenshotIndex.Add(fromPlayer, 0);
                            }
                            else
                            {
                                playerUploadedScreenshotIndex[fromPlayer]++;
                            }
                            if (!playerDownloadedScreenshotIndex.ContainsKey(fromPlayer))
                            {
                                playerDownloadedScreenshotIndex.Add(fromPlayer, new Dictionary<string, int>());
                            }
                            if (!playerDownloadedScreenshotIndex[fromPlayer].ContainsKey(fromPlayer))
                            {
                                playerDownloadedScreenshotIndex[fromPlayer].Add(fromPlayer, playerUploadedScreenshotIndex[fromPlayer]);
                            }
                            else
                            {
                                playerDownloadedScreenshotIndex[fromPlayer][fromPlayer] = playerUploadedScreenshotIndex[fromPlayer];
                            }
                            foreach (KeyValuePair<string, string> entry in playerWatchScreenshot)
                            {
                                if (entry.Key != fromPlayer)
                                {
                                    if (entry.Value == fromPlayer && entry.Key != client.playerName)
                                    {
                                        ClientObject toClient = ClientHandler.GetClientByName(entry.Key);
                                        if (toClient != null && toClient != client)
                                        {
                                            if (!playerDownloadedScreenshotIndex.ContainsKey(entry.Key))
                                            {
                                                playerDownloadedScreenshotIndex.Add(entry.Key, new Dictionary<string, int>());
                                            }
                                            if (!playerDownloadedScreenshotIndex[entry.Key].ContainsKey(fromPlayer))
                                            {
                                                playerDownloadedScreenshotIndex[entry.Key].Add(fromPlayer, 0);
                                            }
                                            playerDownloadedScreenshotIndex[entry.Key][fromPlayer] = playerUploadedScreenshotIndex[fromPlayer];
                                            DarkLog.Debug("Sending screenshot from " + fromPlayer + " to " + entry.Key);
                                            NetworkMessage sendStartMessage = NetworkMessage.Create((int)ServerMessageType.SCREENSHOT_LIBRARY, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                                            using (MessageWriter mw = new MessageWriter(sendStartMessage.data.data))
                                            {
                                                mw.Write<int>((int)ScreenshotMessageType.SEND_START_NOTIFY);
                                                mw.Write<string>(fromPlayer);
                                                sendStartMessage.data.size = (int)mw.GetMessageLength();
                                            }
                                            ClientHandler.SendToClient(toClient, sendStartMessage, true);
                                            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.SCREENSHOT_LIBRARY, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
                                            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
                                            ClientHandler.SendToClient(toClient, newMessage, false);
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    case ScreenshotMessageType.WATCH:
                        {
                            string watchPlayer = mr.Read<string>();
                            if (watchPlayer == "")
                            {
                                if (playerWatchScreenshot.ContainsKey(fromPlayer))
                                {
                                    DarkLog.Debug(fromPlayer + " is no longer watching screenshots from " + playerWatchScreenshot[fromPlayer]);
                                    playerWatchScreenshot.Remove(fromPlayer);
                                }
                            }
                            else
                            {
                                DarkLog.Debug(fromPlayer + " is watching screenshots from " + watchPlayer);
                                playerWatchScreenshot[fromPlayer] = watchPlayer;
                                if (!playerDownloadedScreenshotIndex.ContainsKey(fromPlayer))
                                {
                                    playerDownloadedScreenshotIndex.Add(fromPlayer, new Dictionary<string, int>());
                                }
                                string watchPlayerScreenshotDirectory = Path.Combine(screenshotDirectory, watchPlayer);
                                //Find latest screenshot
                                string sendFile = null;
                                if (Directory.Exists(watchPlayerScreenshotDirectory))
                                {
                                    string[] playerScreenshots = Directory.GetFiles(watchPlayerScreenshotDirectory);
                                    if (playerScreenshots.Length > 0)
                                    {
                                        sendFile = playerScreenshots[0];
                                        foreach (string testFile in playerScreenshots)
                                        {
                                            if (File.GetCreationTime(testFile) > File.GetCreationTime(sendFile))
                                            {
                                                sendFile = testFile;
                                            }
                                        }
                                        if (!playerUploadedScreenshotIndex.ContainsKey(watchPlayer))
                                        {
                                            playerUploadedScreenshotIndex.Add(watchPlayer, 0);
                                        }
                                    }
                                }
                                //Send screenshot if needed
                                if (sendFile != null)
                                {
                                    bool sendScreenshot = false;
                                    if (!playerDownloadedScreenshotIndex[fromPlayer].ContainsKey(watchPlayer))
                                    {
                                        playerDownloadedScreenshotIndex[fromPlayer].Add(watchPlayer, playerUploadedScreenshotIndex[watchPlayer]);
                                        sendScreenshot = true;
                                    }
                                    else
                                    {
                                        if (playerDownloadedScreenshotIndex[fromPlayer][watchPlayer] != playerUploadedScreenshotIndex[watchPlayer])
                                        {
                                            sendScreenshot = true;
                                            playerDownloadedScreenshotIndex[fromPlayer][watchPlayer] = playerUploadedScreenshotIndex[watchPlayer];
                                        }
                                    }
                                    if (sendScreenshot)
                                    {
                                        NetworkMessage sendStartMessage = NetworkMessage.Create((int)ServerMessageType.SCREENSHOT_LIBRARY, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                                        using (MessageWriter mw = new MessageWriter(sendStartMessage.data.data))
                                        {
                                            mw.Write<int>((int)ScreenshotMessageType.SEND_START_NOTIFY);
                                            mw.Write<string>(fromPlayer);
                                            sendStartMessage.data.size = (int)mw.GetMessageLength();
                                        }
                                        NetworkMessage screenshotMessage = NetworkMessage.Create((int)ServerMessageType.SCREENSHOT_LIBRARY, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                                        using (MessageWriter mw = new MessageWriter(screenshotMessage.data.data))
                                        {
                                            mw.Write<int>((int)ScreenshotMessageType.SCREENSHOT);
                                            mw.Write<string>(watchPlayer);
                                            mw.Write<byte[]>(File.ReadAllBytes(sendFile));
                                            screenshotMessage.data.size = (int)mw.GetMessageLength();
                                        }
                                        ClientObject toClient = ClientHandler.GetClientByName(fromPlayer);
                                        if (toClient != null)
                                        {
                                            DarkLog.Debug("Sending saved screenshot from " + watchPlayer + " to " + fromPlayer);
                                            ClientHandler.SendToClient(toClient, sendStartMessage, false);
                                            ClientHandler.SendToClient(toClient, screenshotMessage, false);
                                        }
                                    }
                                }
                            }
                            //Relay the message
                            NetworkMessage newMessage2 = NetworkMessage.Create((int)ServerMessageType.SCREENSHOT_LIBRARY, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
                            Array.Copy(messageData.data, 0, newMessage2.data.data, 0, messageData.Length);
                            ClientHandler.SendToAll(client, newMessage2, false);
                        }
                        break;
                }
            }
        }

        public static void RemovePlayer(string playerName)
        {
            if (playerDownloadedScreenshotIndex.ContainsKey(playerName))
            {
                playerDownloadedScreenshotIndex.Remove(playerName);
            }
            if (playerUploadedScreenshotIndex.ContainsKey(playerName))
            {
                playerUploadedScreenshotIndex.Remove(playerName);
            }
            if (playerWatchScreenshot.ContainsKey(playerName))
            {
                playerWatchScreenshot.Remove(playerName);
            }
        }

        public static void Reset()
        {
            playerUploadedScreenshotIndex.Clear();
            playerDownloadedScreenshotIndex.Clear();
            playerWatchScreenshot.Clear();
        }
    }
}

