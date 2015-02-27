﻿using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;


namespace DarkMultiPlayerServer.Messages
{
    public class Chat
    {
        private static Dictionary<string, List<string>> playerChatChannels = new Dictionary<string, List<string>>();

        public static void SendChatMessageToClient(ClientObject client, string messageText)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.PRIVATE_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                mw.Write<string>(client.playerName);
                mw.Write(messageText);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        public static void SendChatMessageToAll(string messageText)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                //Global channel
                mw.Write<string>("");
                mw.Write(messageText);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToAll(null, newMessage, true);
        }

        public static void SendChatMessageToChannel(string channel, string messageText)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                // Channel
                mw.Write<string>(channel);
                mw.Write(messageText);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToAll(null, newMessage, true);
        }

        public static void SendConsoleMessageToClient(ClientObject client, string message)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.CONSOLE_MESSAGE);
                mw.Write<string>(message);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, false);
        }

        public static void SendConsoleMessageToAdmins(string message)
        {
            foreach (ClientObject client in ClientHandler.GetClients())
            {
                if (client.authenticated && DarkMultiPlayerServer.AdminSystem.fetch.IsAdmin(client.playerName))
                {
                    SendConsoleMessageToClient(client, message);
                }
            }
        }

        public static void HandleChatMessage(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            newMessage.data = messageData;
            using (MessageReader mr = new MessageReader(messageData))
            {
                ChatMessageType messageType = (ChatMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                if (fromPlayer != client.playerName)
                {
                    Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for sending a chat message for another player");
                    return;
                }
                switch (messageType)
                {
                    case ChatMessageType.JOIN:
                        {
                            string joinChannel = mr.Read<string>();
                            if (!playerChatChannels.ContainsKey(fromPlayer))
                            {
                                playerChatChannels.Add(fromPlayer, new List<string>());
                            }
                            if (!playerChatChannels[fromPlayer].Contains(joinChannel))
                            {
                                playerChatChannels[fromPlayer].Add(joinChannel);
                            }
                            DarkLog.Debug(fromPlayer + " joined channel: " + joinChannel);
                        }
                        ClientHandler.SendToAll(client, newMessage, true);
                        break;
                    case ChatMessageType.LEAVE:
                        {
                            string leaveChannel = mr.Read<string>();
                            if (playerChatChannels.ContainsKey(fromPlayer))
                            {
                                if (playerChatChannels[fromPlayer].Contains(leaveChannel))
                                {
                                    playerChatChannels[fromPlayer].Remove(leaveChannel);
                                }
                                if (playerChatChannels[fromPlayer].Count == 0)
                                {
                                    playerChatChannels.Remove(fromPlayer);
                                }
                            }
                            DarkLog.Debug(fromPlayer + " left channel: " + leaveChannel);
                        }
                        ClientHandler.SendToAll(client, newMessage, true);
                        break;
                    case ChatMessageType.CHANNEL_MESSAGE:
                        {
                            string channel = mr.Read<string>();
                            string message = mr.Read<string>();
                            if (channel != "")
                            {
                                foreach (KeyValuePair<string, List<string>> playerEntry in playerChatChannels)
                                {
                                    if (playerEntry.Value.Contains(channel))
                                    {
                                        ClientObject findClient = ClientHandler.GetClientByName(playerEntry.Key);
                                        if (findClient != null)
                                        {
                                            ClientHandler.SendToClient(findClient, newMessage, true);
                                        }
                                    }
                                }
                                DarkLog.ChatMessage(fromPlayer + " -> #" + channel + ": " + message);
                            }
                            else
                            {
                                ClientHandler.SendToClient(client, newMessage, true);
                                ClientHandler.SendToAll(client, newMessage, true);
                                DarkLog.ChatMessage(fromPlayer + " -> #Global: " + message);
                            }
                        }
                        break;
                    case ChatMessageType.PRIVATE_MESSAGE:
                        {
                            string toPlayer = mr.Read<string>();
                            string message = mr.Read<string>();
                            if (toPlayer != Settings.settingsStore.consoleIdentifier)
                            {
                                ClientObject findClient = ClientHandler.GetClientByName(toPlayer);
                                if (findClient != null)
                                {
                                    ClientHandler.SendToClient(client, newMessage, true);
                                    ClientHandler.SendToClient(findClient, newMessage, true);
                                    DarkLog.ChatMessage(fromPlayer + " -> @" + toPlayer + ": " + message);
                                }
                                {
                                    DarkLog.ChatMessage(fromPlayer + " -X-> @" + toPlayer + ": " + message);
                                }
                            }
                            else
                            {
                                ClientHandler.SendToClient(client, newMessage, true);
                                DarkLog.ChatMessage(fromPlayer + " -> @" + toPlayer + ": " + message);
                            }
                        }
                        break;
                    case ChatMessageType.CONSOLE_MESSAGE:
                        {
                            string message = mr.Read<string>();
                            if (client.authenticated && DarkMultiPlayerServer.AdminSystem.fetch.IsAdmin(client.playerName))
                            {
                                CommandHandler.HandleServerInput(message);
                            }
                            else
                            {
                                Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for sending a console command as a non-admin player.");
                            }
                        }
                        break;
                        // Outdated since migration to clientmessagetype
                    //case ChatMessageType.SYNTAX_BRIDGE:
                    //    {
                    //        string message = mr.Read<string>();
                    //        string playername = client.playerName;
                    //        if (!SyntaxMPProtection.SyntaxCode.SyntaxPlayer.isProtected(client.playerName))
                    //        {
                    //            // TODO: popup a window or warning for the user that wants to protect a vessel.
                    //            playername = client.playerName;
                    //            SyntaxMPProtection.SyntaxCode.SyntaxPlayer.SaveCredentials(playername);
                    //        }
                    //        string[] args = message.Split(',');

                    //        DarkLog.Normal("Determining player vessel arguments");
                    //        DarkLog.Normal(string.Format("SyntaxCode: For debug sake show arguments: {0},{1},{2}", args[0], args[1], args[2]));
                    //        if (args[0] == "personal")
                    //        {
                    //            if (args[1] == "private")
                    //            {
                    //                DarkLog.Normal("SyntaxCodes: Claiming vessel: " + args[2] + " for player " + playername);
                    //                SyntaxMPProtection.SyntaxCode.SyntaxPlayerVessel.ClaimVessel(playername, args[2], SyntaxMPProtection.VesselAccessibilityTypes.Private);
                    //                DarkLog.Normal("SyntaxCodes: Vessel claimed.");
                    //            }
                    //            else if (args[1] == "public")
                    //            {
                    //                SyntaxMPProtection.SyntaxCode.SyntaxPlayerVessel.ClaimVessel(playername, args[2], SyntaxMPProtection.VesselAccessibilityTypes.Public);
                    //            }
                    //            else if (args[1] == "spectate")
                    //            {
                    //                SyntaxMPProtection.SyntaxCode.SyntaxPlayerVessel.ClaimVessel(playername, args[2], SyntaxMPProtection.VesselAccessibilityTypes.Spectate);
                    //            }
                    //        }
                    //        else if (args[0] == "group")
                    //        {
                    //            if (args[1] == "private")
                    //            {
                    //                SyntaxMPProtection.SyntaxCode.SyntaxPlayerGroup.ClaimVesselForGroup(playername, args[2], SyntaxMPProtection.VesselAccessibilityTypes.Private);
                    //            }
                    //            else if (args[1] == "group")
                    //            {
                    //                SyntaxMPProtection.SyntaxCode.SyntaxPlayerGroup.ClaimVesselForGroup(playername, args[2], SyntaxMPProtection.VesselAccessibilityTypes.Group);
                    //            }
                    //            else if (args[1] == "public")
                    //            {
                    //                SyntaxMPProtection.SyntaxCode.SyntaxPlayerGroup.ClaimVesselForGroup(playername, args[2], SyntaxMPProtection.VesselAccessibilityTypes.Public);
                    //            }
                    //            else if (args[1] == "spectate")
                    //            {
                    //                SyntaxMPProtection.SyntaxCode.SyntaxPlayerVessel.ClaimVessel(playername, args[2], SyntaxMPProtection.VesselAccessibilityTypes.Spectate);
                    //            }
                    //        }
                    //    }
                    //    break;
                }
            }
        }
        
        public static void SendPlayerChatChannels(ClientObject client)
        {
            List<string> playerList = new List<string>();
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.LIST);
                foreach (KeyValuePair<string, List<string>> playerEntry in playerChatChannels)
                {
                    playerList.Add(playerEntry.Key);
                }
                mw.Write<string[]>(playerList.ToArray());
                foreach (string player in playerList)
                {
                    mw.Write<string[]>(playerChatChannels[player].ToArray());
                }
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.CHAT_MESSAGE;
                newMessage.data = mw.GetMessageBytes();
                ClientHandler.SendToClient(client, newMessage, true);
            }
        }

        public static void RemovePlayer(string playerName)
        {
            if (playerChatChannels.ContainsKey(playerName))
            {
                playerChatChannels.Remove(playerName);
            }
        }

        public static void Reset()
        {
            playerChatChannels.Clear();
        }
    }
}

