using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class GroupMessage
    {
        public static void HandleMessage(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                int type = mr.Read<int>();
                switch ((GroupMessageType)type)
                {
                    case GroupMessageType.ADD_ADMIN:
                        {

                            string playerName = mr.Read<string>();
                            string groupName = mr.Read<string>();
                            if (Groups.fetch.PlayerIsAdmin(client.playerName, groupName))
                            {
                                Groups.fetch.AddPlayerAdmin(playerName, groupName);
                            }
                        }
                        break;
                    case GroupMessageType.ADD_PLAYER:
                        {
                            string playerName = mr.Read<string>();
                            string groupName = mr.Read<string>();
                            if (Groups.fetch.GroupExists(groupName))
                            {
                                if (Groups.fetch.PlayerIsAdmin(client.playerName, groupName))
                                {
                                    Groups.fetch.AddPlayerToGroup(playerName, groupName);
                                }
                            }
                            else
                            {
                                //We can only add ourselves to new groups.
                                if (playerName == client.playerName)
                                {
                                    Groups.fetch.AddPlayerToGroup(playerName, groupName);
                                }
                            }
                        }
                        break;
                    case GroupMessageType.REMOVE_ADMIN:
                        {
                            string playerName = mr.Read<string>();
                            string groupName = mr.Read<string>();
                            if (Groups.fetch.PlayerIsAdmin(client.playerName, groupName))
                            {
                                Groups.fetch.RemovePlayerAdmin(playerName, groupName);
                            }
                        }
                        break;
                    case GroupMessageType.REMOVE_PLAYER:
                        {
                            string playerName = mr.Read<string>();
                            string groupName = mr.Read<string>();
                            if (playerName == client.playerName || Groups.fetch.PlayerIsAdmin(client.playerName, groupName))
                            {
                                Groups.fetch.RemovePlayerFromGroup(playerName, groupName);
                            }
                        }
                        break;
                    case GroupMessageType.REMOVE_GROUP:
                        {
                            string groupName = mr.Read<string>();
                            if (Groups.fetch.PlayerIsAdmin(client.playerName, groupName))
                            {
                                Groups.fetch.RemoveGroup(groupName);
                            }
                        }
                        break;
                    case GroupMessageType.GROUP_REQUEST:
                        Messages.GroupMessage.SendGroupsToClient(client);
                        break;
                }
            }
        }

        public static void SendGroupsToAll()
        {
            DarkLog.Debug("Sending groups to everyone");
            Dictionary<string, List<string>> playerGroups = Groups.fetch.GetGroupsCopy();
            Dictionary<string, List<string>> groupAdmins = Groups.fetch.GetAdminsCopy();
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.GROUP, 4, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<int>((int)GroupMessageType.GROUP_RESET);
            }
            ClientHandler.SendToAll(null, newMessage, true);

            foreach (KeyValuePair<string, List<string>> kvp in playerGroups)
            {
                NetworkMessage newMessage2 = NetworkMessage.Create((int)ServerMessageType.GROUP, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                using (MessageWriter mw = new MessageWriter(newMessage2.data.data))
                {
                    mw.Write<int>((int)GroupMessageType.GROUP_INFO);
                    mw.Write<string>(kvp.Key);
                    mw.Write<string[]>(kvp.Value.ToArray());
                    newMessage2.data.size = (int)mw.GetMessageLength();
                }
                ClientHandler.SendToAll(null, newMessage2, true);
            }
            foreach (KeyValuePair<string, List<string>> kvp in groupAdmins)
            {
                NetworkMessage newMessage3 = NetworkMessage.Create((int)ServerMessageType.GROUP, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                using (MessageWriter mw = new MessageWriter(newMessage3.data.data))
                {
                    mw.Write<int>((int)GroupMessageType.ADMIN_INFO);
                    mw.Write<string>(kvp.Key);
                    mw.Write<string[]>(kvp.Value.ToArray());
                    newMessage3.data.size = (int)mw.GetMessageLength();
                }
                ClientHandler.SendToAll(null, newMessage3, true);
            }
        }

        public static void SendGroupsToClient(ClientObject client)
        {
            DarkLog.Debug("Sending groups to " + client.playerName);
            Dictionary<string, List<string>> playerGroups = Groups.fetch.GetGroupsCopy();
            Dictionary<string, List<string>> groupAdmins = Groups.fetch.GetAdminsCopy();
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.GROUP, 4, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<int>((int)GroupMessageType.GROUP_RESET);
            }
            ClientHandler.SendToClient(client, newMessage, true);
            foreach (KeyValuePair<string, List<string>> kvp in playerGroups)
            {
                NetworkMessage newMessage2 = NetworkMessage.Create((int)ServerMessageType.GROUP, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                using (MessageWriter mw = new MessageWriter(newMessage2.data.data))
                {
                    mw.Write<int>((int)GroupMessageType.GROUP_INFO);
                    mw.Write<string>(kvp.Key);
                    mw.Write<string[]>(kvp.Value.ToArray());
                    newMessage2.data.size = (int)mw.GetMessageLength();
                    ClientHandler.SendToClient(client, newMessage2, true);
                }
            }
            foreach (KeyValuePair<string, List<string>> kvp in groupAdmins)
            {
                NetworkMessage newMessage3 = NetworkMessage.Create((int)ServerMessageType.GROUP, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                using (MessageWriter mw = new MessageWriter(newMessage3.data.data))
                {
                    mw.Write<int>((int)GroupMessageType.ADMIN_INFO);
                    mw.Write<string>(kvp.Key);
                    mw.Write<string[]>(kvp.Value.ToArray());
                    newMessage3.data.size = (int)mw.GetMessageLength();
                    ClientHandler.SendToClient(client, newMessage3, true);
                }
            }
            NetworkMessage newMessage4 = NetworkMessage.Create((int)ServerMessageType.GROUP, 4, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage4.data.data))
            {
                mw.Write<int>((int)GroupMessageType.GROUPS_SYNCED);
            }
            ClientHandler.SendToClient(client, newMessage4, true);
        }
    }
}
