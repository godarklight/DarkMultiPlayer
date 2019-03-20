using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class PermissionMessage
    {
        public static void HandleMessage(ClientObject client, byte[] data)
        {
            using (MessageReader mr = new MessageReader(data))
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
                using (MessageWriter mw = new MessageWriter())
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
                    ServerMessage sm = new ServerMessage();
                    sm.type = ServerMessageType.PERMISSION;
                    sm.data = mw.GetMessageBytes();
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
                using (MessageWriter mw = new MessageWriter())
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
                    ServerMessage sm = new ServerMessage();
                    sm.type = ServerMessageType.PERMISSION;
                    sm.data = mw.GetMessageBytes();
                    ClientHandler.SendToClient(client, sm, true);
                }
            }
            ServerMessage sm2 = new ServerMessage();
            sm2.type = ServerMessageType.PERMISSION;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)PermissionMessageType.PERMISSION_SYNCED);
                sm2.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, sm2, true);
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
            using (MessageWriter mw = new MessageWriter())
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
                ServerMessage sm = new ServerMessage();
                sm.type = ServerMessageType.PERMISSION;
                sm.data = mw.GetMessageBytes();
                ClientHandler.SendToAll(null, sm, true);
            }
        }
    }
}