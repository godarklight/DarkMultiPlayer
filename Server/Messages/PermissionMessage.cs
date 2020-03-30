using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class PermissionMessage
    {
        public static void HandleMessage(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                int type = mr.Read<int>();
                switch ((PermissionMessageType)type)
                {
                    case PermissionMessageType.PERMISSION_REQUEST:
                        Messages.PermissionMessage.SendVesselPermissionsToClient(client);
                        break;
                    case PermissionMessageType.SET_OWNER:
                        {
                            Guid guid = Guid.Parse(mr.Read<string>());
                            string owner = mr.Read<string>();
                            if (Permissions.fetch.PlayerIsVesselOwner(client.playerName, guid))
                            {
                                Permissions.fetch.SetVesselOwner(guid, owner);
                            }
                        }
                        break;
                    case PermissionMessageType.SET_GROUP:
                        {
                            Guid guid = Guid.Parse(mr.Read<string>());
                            string group = mr.Read<string>();
                            if (Permissions.fetch.PlayerIsVesselOwner(client.playerName, guid))
                            {
                                Permissions.fetch.SetVesselGroup(guid, group);
                            }
                        }
                        break;
                    case PermissionMessageType.SET_PERMISSION_LEVEL:
                        {
                            Guid guid = Guid.Parse(mr.Read<string>());
                            string permissionLevel = mr.Read<string>();
                            VesselProtectionType vesselProtection = (VesselProtectionType)Enum.Parse(typeof(VesselProtectionType), permissionLevel);
                            if (Permissions.fetch.PlayerIsVesselOwner(client.playerName, guid))
                            {
                                Permissions.fetch.SetVesselProtection(guid, vesselProtection);
                            }
                        }
                        break;
                }
            }
        }

        public static void SendVesselPermissionsToAll()
        {
            DarkLog.Debug("Sending permissions to everyone");
            Dictionary<Guid, VesselPermission> vesselPermissions = Permissions.fetch.GetPermissionsCopy();
            foreach (KeyValuePair<Guid, VesselPermission> kvp in vesselPermissions)
            {
                NetworkMessage sm = NetworkMessage.Create((int)ServerMessageType.PERMISSION, 512 * 1024);
                sm.reliable = true;
                using (MessageWriter mw = new MessageWriter(sm.data.data))
                {
                    mw.Write<int>((int)PermissionMessageType.PERMISSION_INFO);
                    mw.Write<string>(kvp.Key.ToString());
                    mw.Write<string>(kvp.Value.owner);
                    mw.Write<string>(kvp.Value.protection.ToString());
                    if (!string.IsNullOrEmpty(kvp.Value.group))
                    {
                        mw.Write<bool>(true);
                        mw.Write<string>(kvp.Value.group);
                    }
                    else
                    {
                        mw.Write<bool>(false);
                    }
                    sm.data.size = (int)mw.GetMessageLength();
                    ClientHandler.SendToAll(null, sm, true);
                }
            }
        }

        public static void SendVesselPermissionsToClient(ClientObject client)
        {
            DarkLog.Debug("Sending permissions to " + client.playerName);
            Dictionary<Guid, VesselPermission> vesselPermissions = Permissions.fetch.GetPermissionsCopy();
            foreach (KeyValuePair<Guid, VesselPermission> kvp in vesselPermissions)
            {
                NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.PERMISSION, 512 * 1024);
                newMessage.reliable = true;
                using (MessageWriter mw = new MessageWriter(newMessage.data.data))
                {
                    mw.Write<int>((int)PermissionMessageType.PERMISSION_INFO);
                    mw.Write<string>(kvp.Key.ToString());
                    mw.Write<string>(kvp.Value.owner);
                    mw.Write<string>(kvp.Value.protection.ToString());
                    if (!string.IsNullOrEmpty(kvp.Value.group))
                    {
                        mw.Write<bool>(true);
                        mw.Write<string>(kvp.Value.group);
                    }
                    else
                    {
                        mw.Write<bool>(false);
                    }
                    newMessage.data.size = (int)mw.GetMessageLength();
                    ClientHandler.SendToClient(client, newMessage, true);
                }
            }
            NetworkMessage newMessage2 = NetworkMessage.Create((int)ServerMessageType.PERMISSION, 4);
            newMessage2.reliable = true;
            using (MessageWriter mw = new MessageWriter(newMessage2.data.data))
            {
                mw.Write<int>((int)PermissionMessageType.PERMISSION_SYNCED);
            }
            ClientHandler.SendToClient(client, newMessage2, true);
        }

        public static void SendVesselPermissionToAll(Guid vesselID)
        {
            DarkLog.Debug("Sending permissions to everyone, vessel: " + vesselID);
            Dictionary<Guid, VesselPermission> vesselPermissions = Permissions.fetch.GetPermissionsCopy();
            if (!vesselPermissions.ContainsKey(vesselID))
            {
                return;
            }
            VesselPermission vesselPermission = vesselPermissions[vesselID];
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.PERMISSION, 512 * 1024);
            newMessage.reliable = true;
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<int>((int)PermissionMessageType.PERMISSION_INFO);
                mw.Write<string>(vesselID.ToString());
                mw.Write<string>(vesselPermission.owner);
                mw.Write<string>(vesselPermission.protection.ToString());
                if (!string.IsNullOrEmpty(vesselPermission.group))
                {
                    mw.Write<bool>(true);
                    mw.Write<string>(vesselPermission.group);
                }
                else
                {
                    mw.Write<bool>(false);
                }
                newMessage.data.size = (int)mw.GetMessageLength();
                ClientHandler.SendToAll(null, newMessage, true);
            }
        }
    }
}