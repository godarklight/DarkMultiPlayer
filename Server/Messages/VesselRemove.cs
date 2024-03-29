﻿using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class VesselRemove
    {
        public static void HandleVesselRemoval(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                //Don't care about the subspace on the server.
                mr.Read<double>();
                string vesselID = mr.Read<string>();
                if (!SafeFile.IsNameSafe(vesselID))
                {
                    Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for an invalid vessel ID");
                    return;
                }
                Permissions.fetch.SetVesselOwnerIfUnowned(new Guid(vesselID), client.playerName);
                if (!Permissions.fetch.PlayerHasVesselPermission(client.playerName, new Guid(vesselID)))
                {
                    Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked from the server, tried to remove protected vessel");
                    return;
                }
                bool isDockingUpdate = mr.Read<bool>();
                if (!isDockingUpdate)
                {
                    DarkLog.Debug("Removing vessel " + vesselID + " from " + client.playerName);
                }
                else
                {
                    DarkLog.Debug("Removing DOCKED vessel " + vesselID + " from " + client.playerName);
                }
                if (File.Exists(Path.Combine(Server.universeDirectory, "Vessels", vesselID + ".txt")))
                {
                    lock (Server.universeSizeLock)
                    {
                        File.Delete(Path.Combine(Server.universeDirectory, "Vessels", vesselID + ".txt"));
                    }
                }
                //Relay the message.
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.VESSEL_REMOVE;
                newMessage.data = messageData;
                ClientHandler.SendToAll(client, newMessage, false);
            }
        }

        public static void HandleKerbalRemoval(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                //Don't care about the subspace on the server.
                mr.Read<double>();
                string kerbalName = mr.Read<string>();
                if (!SafeFile.IsNameSafe(kerbalName))
                {
                    Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for an invalid kerbal name");
                    return;
                }
                DarkLog.Debug("Removing kerbal " + kerbalName + " from " + client.playerName);
                if (File.Exists(Path.Combine(Server.universeDirectory, "Kerbals", kerbalName + ".txt")))
                {
                    lock (Server.universeSizeLock)
                    {
                        File.Delete(Path.Combine(Server.universeDirectory, "Kerbals", kerbalName + ".txt"));
                    }
                }
                //Relay the message.
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.KERBAL_REMOVE;
                newMessage.data = messageData;
                ClientHandler.SendToAll(client, newMessage, false);
            }
        }
    }
}

