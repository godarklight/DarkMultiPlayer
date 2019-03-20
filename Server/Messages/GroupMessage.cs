using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class GroupMessage
    {
        public static void HandleMessage(ClientObject client, byte[] data)
        {
            using (MessageReader mr = new MessageReader(data))
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
                            if (Groups.fetch.PlayerIsAdmin(client.playerName, groupName))
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
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)GroupMessageType.GROUP_RESET);
                ServerMessage sm = new ServerMessage();
                sm.type = ServerMessageType.GROUP;
                sm.data = mw.GetMessageBytes();
                ClientHandler.SendToAll(null, sm, true);
            }
            foreach (KeyValuePair<string, List<string>> kvp in playerGroups)
            {
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)GroupMessageType.GROUP_INFO);
                    mw.Write<string>(kvp.Key);
                    mw.Write<string[]>(kvp.Value.ToArray());
                    ServerMessage sm = new ServerMessage();
                    sm.type = ServerMessageType.GROUP;
                    sm.data = mw.GetMessageBytes(); ;
                    ClientHandler.SendToAll(null, sm, true);
                }
            }
            foreach (KeyValuePair<string, List<string>> kvp in groupAdmins)
            {
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)GroupMessageType.ADMIN_INFO);
                    mw.Write<string>(kvp.Key);
                    mw.Write<string[]>(kvp.Value.ToArray());
                    ServerMessage sm = new ServerMessage();
                    sm.type = ServerMessageType.GROUP;
                    sm.data = mw.GetMessageBytes(); ;
                    ClientHandler.SendToAll(null, sm, true);
                }
            }
        }

        public static void SendGroupsToClient(ClientObject client)
        {
            DarkLog.Debug("Sending groups to " + client.playerName);
            Dictionary<string, List<string>> playerGroups = Groups.fetch.GetGroupsCopy();
            Dictionary<string, List<string>> groupAdmins = Groups.fetch.GetAdminsCopy();
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)GroupMessageType.GROUP_RESET);
                ServerMessage sm = new ServerMessage();
                sm.type = ServerMessageType.GROUP;
                sm.data = mw.GetMessageBytes();
                ClientHandler.SendToClient(client, sm, true);
            }
            foreach (KeyValuePair<string, List<string>> kvp in playerGroups)
            {
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)GroupMessageType.GROUP_INFO);
                    mw.Write<string>(kvp.Key);
                    mw.Write<string[]>(kvp.Value.ToArray());
                    ServerMessage sm = new ServerMessage();
                    sm.type = ServerMessageType.GROUP;
                    sm.data = mw.GetMessageBytes(); ;
                    ClientHandler.SendToClient(client, sm, true);
                }
            }
            foreach (KeyValuePair<string, List<string>> kvp in groupAdmins)
            {
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)GroupMessageType.ADMIN_INFO);
                    mw.Write<string>(kvp.Key);
                    mw.Write<string[]>(kvp.Value.ToArray());
                    ServerMessage sm = new ServerMessage();
                    sm.type = ServerMessageType.GROUP;
                    sm.data = mw.GetMessageBytes(); ;
                    ClientHandler.SendToClient(client, sm, true);
                }
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)GroupMessageType.GROUPS_SYNCED);
                ServerMessage sm = new ServerMessage();
                sm.type = ServerMessageType.GROUP;
                sm.data = mw.GetMessageBytes();
                ClientHandler.SendToClient(client, sm, true);
            }
        }
    }
}
