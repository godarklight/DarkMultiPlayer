using System;
using System.IO;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class WarpControl
    {
        private static Dictionary<int, Subspace> subspaces = new Dictionary<int, Subspace>();
        private static Dictionary<string, int> playerSubspace = new Dictionary<string, int>();

        public static void SendAllReportedSkewRates(ClientObject client)
        {
            foreach (ClientObject otherClient in ClientHandler.GetClients())
            {
                if (otherClient.authenticated)
                {
                    if (otherClient != client)
                    {
                        ServerMessage newMessage = new ServerMessage();
                        newMessage.type = ServerMessageType.WARP_CONTROL;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<int>((int)WarpMessageType.REPORT_RATE);
                            mw.Write<string>(otherClient.playerName);
                            mw.Write<float>(otherClient.subspace);
                            mw.Write<float>(otherClient.subspaceRate);
                            newMessage.data = mw.GetMessageBytes();
                        }
                        ClientHandler.SendToClient(client, newMessage, true);
                    }
                }
            }
        }

        public static void HandleWarpControl(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.WARP_CONTROL;
            newMessage.data = messageData;
            using (MessageReader mr = new MessageReader(messageData))
            {
                WarpMessageType warpType = (WarpMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                if (fromPlayer == client.playerName)
                {
                    if (warpType == WarpMessageType.NEW_SUBSPACE)
                    {
                        int newSubspaceID = mr.Read<int>();
                        if (subspaces.ContainsKey(newSubspaceID))
                        {
                            DarkLog.Debug("Kicked for trying to create an existing subspace");
                            Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for trying to create an existing subspace");
                            return;
                        }
                        else
                        {
                            Subspace newSubspace = new Subspace();
                            newSubspace.serverClock = mr.Read<long>();
                            newSubspace.planetTime = mr.Read<double>();
                            newSubspace.subspaceSpeed = mr.Read<float>();
                            subspaces.Add(newSubspaceID, newSubspace);
                            client.subspace = newSubspaceID;
                            SaveLatestSubspace();
                        }
                    }
                    if (warpType == WarpMessageType.CHANGE_SUBSPACE)
                    {
                        client.subspace = mr.Read<int>();
                    }
                    if (warpType == WarpMessageType.REPORT_RATE)
                    {
                        int reportedSubspace = mr.Read<int>();
                        if (client.subspace != reportedSubspace)
                        {
                            DarkLog.Debug("Warning, setting client " + client.playerName + " to subspace " + client.subspace);
                            client.subspace = reportedSubspace;
                        }
                        float newSubspaceRate = mr.Read<float>();
                        client.subspaceRate = newSubspaceRate;
                        foreach (ClientObject otherClient in ClientHandler.GetClients())
                        {
                            if (otherClient.authenticated && otherClient.subspace == reportedSubspace)
                            {
                                if (newSubspaceRate > otherClient.subspaceRate)
                                {
                                    newSubspaceRate = otherClient.subspaceRate;
                                }
                            }
                        }
                        if (newSubspaceRate < 0.3f)
                        {
                            newSubspaceRate = 0.3f;
                        }
                        if (newSubspaceRate > 1f)
                        {
                            newSubspaceRate = 1f;
                        }
                        //Relock the subspace if the rate is more than 3% out of the average
                        if (Math.Abs(subspaces[reportedSubspace].subspaceSpeed - newSubspaceRate) > 0.03f)
                        {
                            UpdateSubspace(reportedSubspace);
                            subspaces[reportedSubspace].subspaceSpeed = newSubspaceRate;
                            ServerMessage relockMessage = new ServerMessage();
                            relockMessage.type = ServerMessageType.WARP_CONTROL;
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write<int>((int)WarpMessageType.RELOCK_SUBSPACE);
                                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                                mw.Write<int>(reportedSubspace);
                                mw.Write<long>(subspaces[reportedSubspace].serverClock);
                                mw.Write<double>(subspaces[reportedSubspace].planetTime);
                                mw.Write<float>(subspaces[reportedSubspace].subspaceSpeed);
                                relockMessage.data = mw.GetMessageBytes();
                            }
                            SaveLatestSubspace();
                            //DarkLog.Debug("Subspace " + client.subspace + " locked to " + newSubspaceRate + "x speed.");
                            ClientHandler.SendToClient(client, relockMessage, true);
                            ClientHandler.SendToAll(client, relockMessage, true);
                        }
                    }
                }
                else
                {
                    DarkLog.Debug(client.playerName + " tried to send an update for " + fromPlayer + ", kicking.");
                    Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for sending an update for another player");
                    return;
                }
            }
            ClientHandler.SendToAll(client, newMessage, true);
        }

        public static void SendAllSubspaces(ClientObject client)
        {
            //Send all the locks.
            foreach (KeyValuePair<int, Subspace> subspace in subspaces)
            {
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.WARP_CONTROL;
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)WarpMessageType.NEW_SUBSPACE);
                    mw.Write<string>("");
                    mw.Write<int>(subspace.Key);
                    mw.Write<long>(subspace.Value.serverClock);
                    mw.Write<double>(subspace.Value.planetTime);
                    mw.Write<float>(subspace.Value.subspaceSpeed);
                    newMessage.data = mw.GetMessageBytes();
                }
                ClientHandler.SendToClient(client, newMessage, true);
            }
            //Tell the player "when" everyone is.
            foreach (ClientObject otherClient in ClientHandler.GetClients())
            {
                if (otherClient.authenticated && (otherClient.playerName != client.playerName))
                {
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.WARP_CONTROL;
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)WarpMessageType.CHANGE_SUBSPACE);
                        mw.Write<string>(otherClient.playerName);
                        mw.Write<int>(otherClient.subspace);
                        newMessage.data = mw.GetMessageBytes();
                    }
                    ClientHandler.SendToClient(client, newMessage, true);
                }
            }
        }

        public static void SendSetSubspace(ClientObject client)
        {
            if (!Settings.settingsStore.keepTickingWhileOffline && ClientHandler.GetClients().Length == 1)
            {
                DarkLog.Debug("Reverting server time to last player connection");
                long currentTime = DateTime.UtcNow.Ticks;
                foreach (KeyValuePair<int, Subspace> subspace in subspaces)
                {
                    subspace.Value.serverClock = currentTime;
                    subspace.Value.subspaceSpeed = 1f;
                    SaveLatestSubspace();
                }
            }
            int targetSubspace = -1;
            if (Settings.settingsStore.sendPlayerToLatestSubspace || !playerSubspace.ContainsKey(client.playerName))
            {
                DarkLog.Debug("Sending " + client.playerName + " to the latest subspace " + targetSubspace);
                targetSubspace = GetLatestSubspace();
            }
            else
            {
                DarkLog.Debug("Sending " + client.playerName + " to the previous subspace " + targetSubspace);
                targetSubspace = playerSubspace[client.playerName];
            }
            client.subspace = targetSubspace;
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SET_SUBSPACE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(targetSubspace);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        private static void LoadSavedSubspace()
        {
            try
            {
                string subspaceFile = Path.Combine(Server.universeDirectory, "subspace.txt");
                using (StreamReader sr = new StreamReader(subspaceFile))
                {
                    //Ignore the comment line.
                    string firstLine = "";
                    while (firstLine.StartsWith("#") || String.IsNullOrEmpty(firstLine))
                    {
                        firstLine = sr.ReadLine().Trim();
                    }
                    Subspace savedSubspace = new Subspace();
                    int subspaceID = Int32.Parse(firstLine);
                    savedSubspace.serverClock = Int64.Parse(sr.ReadLine().Trim());
                    savedSubspace.planetTime = Double.Parse(sr.ReadLine().Trim());
                    savedSubspace.subspaceSpeed = Single.Parse(sr.ReadLine().Trim());
                    subspaces.Add(subspaceID, savedSubspace);
                }
            }
            catch
            {
                DarkLog.Debug("Creating new subspace lock file");
                Subspace newSubspace = new Subspace();
                newSubspace.serverClock = DateTime.UtcNow.Ticks;
                newSubspace.planetTime = 100d;
                newSubspace.subspaceSpeed = 1f;
                subspaces.Add(0, newSubspace);
                SaveSubspace(0, newSubspace);
            }
        }

        public static int GetLatestSubspace()
        {
            int latestID = 0;
            double latestPlanetTime = 0;
            long currentTime = DateTime.UtcNow.Ticks;
            foreach (KeyValuePair<int,Subspace> subspace in subspaces)
            {
                double currentPlanetTime = subspace.Value.planetTime + (((currentTime - subspace.Value.serverClock) / 10000000) * subspace.Value.subspaceSpeed);
                if (currentPlanetTime > latestPlanetTime)
                {
                    latestID = subspace.Key;
                }
            }
            return latestID;
        }

        private static void SaveLatestSubspace()
        {
            int latestID = GetLatestSubspace();
            SaveSubspace(latestID, subspaces[latestID]);
        }

        private static void UpdateSubspace(int subspaceID)
        {
            //New time = Old time + (seconds since lock * subspace rate)
            long newServerClockTime = DateTime.UtcNow.Ticks;
            float timeSinceLock = (DateTime.UtcNow.Ticks - subspaces[subspaceID].serverClock) / 10000000f;
            double newPlanetariumTime = subspaces[subspaceID].planetTime + (timeSinceLock * subspaces[subspaceID].subspaceSpeed);
            subspaces[subspaceID].serverClock = newServerClockTime;
            subspaces[subspaceID].planetTime = newPlanetariumTime;
        }

        private static void SaveSubspace(int subspaceID, Subspace subspace)
        {
            string subspaceFile = Path.Combine(Server.universeDirectory, "subspace.txt");
            using (StreamWriter sw = new StreamWriter(subspaceFile))
            {
                sw.WriteLine("#Incorrectly editing this file will cause weirdness. If there is any errors, the universe time will be reset.");
                sw.WriteLine("#This file can only be edited if the server is stopped.");
                sw.WriteLine("#Each variable is on a new line. They are subspaceID, server clock (from DateTime.UtcNow.Ticks), universe time, and subspace speed.");
                sw.WriteLine(subspaceID);
                sw.WriteLine(subspace.serverClock);
                sw.WriteLine(subspace.planetTime);
                sw.WriteLine(subspace.subspaceSpeed);
            }
        }

        internal static void HoldSubspace()
        {
            //When the last player disconnects and we are a no-tick-offline server, save the universe time.
            UpdateSubspace(GetLatestSubspace());
            SaveLatestSubspace();
        }

        public static void Reset()
        {
            subspaces.Clear();
            playerSubspace.Clear();
            LoadSavedSubspace();
        }
    }
}

