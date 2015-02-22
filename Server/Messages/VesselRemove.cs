using System;
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
                mr.Read<int>();
                mr.Read<double>();
                string vesselID = mr.Read<string>();
                bool isDockingUpdate = mr.Read<bool>();
                if (!PermissionSystem.Core.PVessel.IsOwner(client.playerName, client.activeVessel))
                {
                    client.disconnectClient = true;
                    ClientHandler.DisconnectClient(client);
                    return;
                }
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
    }
}

