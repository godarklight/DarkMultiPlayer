using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class Chat
    {
        private static Dictionary<string, List<string>> playerChatChannels = new Dictionary<string, List<string>>();

        public static void SendChatMessageToClient(ClientObject client, string messageText)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.CHAT_MESSAGE, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<int>((int)ChatMessageType.PRIVATE_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                mw.Write<string>(client.playerName);
                mw.Write(messageText);
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        public static void SendChatMessageToAll(string messageText)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.CHAT_MESSAGE, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                //Global channel
                mw.Write<string>("");
                mw.Write(messageText);
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToAll(null, newMessage, true);
        }

        public static void SendChatMessageToChannel(string channel, string messageText)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.CHAT_MESSAGE, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                // Channel
                mw.Write<string>(channel);
                mw.Write(messageText);
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToAll(null, newMessage, true);
        }

        public static void SendConsoleMessageToClient(ClientObject client, string message)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.CHAT_MESSAGE, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<int>((int)ChatMessageType.CONSOLE_MESSAGE);
                mw.Write<string>(message);
                newMessage.data.size = (int)mw.GetMessageLength();
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

        public static void HandleChatMessage(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.CHAT_MESSAGE, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            using (MessageReader mr = new MessageReader(messageData.data))
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
                                List<ClientObject> sendList = new List<ClientObject>();
                                foreach (KeyValuePair<string, List<string>> playerEntry in playerChatChannels)
                                {
                                    if (playerEntry.Value.Contains(channel))
                                    {
                                        ClientObject findClient = ClientHandler.GetClientByName(playerEntry.Key);
                                        if (findClient != null)
                                        {
                                            sendList.Add(findClient);
                                        }
                                    }
                                }
                                newMessage.usageCount = sendList.Count;
                                foreach (ClientObject sendClient in sendList)
                                {
                                    ClientHandler.SendToClient(sendClient, newMessage, true);
                                }
                                sendList.Clear();
                                DarkLog.ChatMessage(fromPlayer + " -> #" + channel + ": " + message);
                            }
                            else
                            {
                                ClientHandler.SendToAll(null, newMessage, true);
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
                                    newMessage.usageCount = 2;
                                    ClientHandler.SendToClient(client, newMessage, true);
                                    ClientHandler.SendToClient(findClient, newMessage, true);
                                    DarkLog.ChatMessage(fromPlayer + " -> @" + toPlayer + ": " + message);
                                }
                                else
                                {
                                    ByteRecycler.ReleaseObject(newMessage.data);
                                    Recycler<NetworkMessage>.ReleaseObject(newMessage);
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
                }
            }
        }

        public static void SendPlayerChatChannels(ClientObject client)
        {
            List<string> playerList = new List<string>();
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.CHAT_MESSAGE, 2048, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
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
                newMessage.data.size = (int)mw.GetMessageLength();
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

