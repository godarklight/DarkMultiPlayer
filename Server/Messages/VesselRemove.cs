using System;
using System.IO;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class VesselRemove
    {
        public static void HandleVesselRemoval(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                //Don't care about the subspace on the server.
                mr.Read<double>();
                string vesselID = mr.Read<string>();
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
                NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.VESSEL_REMOVE, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
                Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
                ClientHandler.SendToAll(client, newMessage, false);
            }
        }

        public static void HandleKerbalRemoval(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                //Don't care about the subspace on the server.
                mr.Read<double>();
                string kerbalName = mr.Read<string>();
                DarkLog.Debug("Removing kerbal " + kerbalName + " from " + client.playerName);
                if (File.Exists(Path.Combine(Server.universeDirectory, "Kerbals", kerbalName + ".txt")))
                {
                    lock (Server.universeSizeLock)
                    {
                        File.Delete(Path.Combine(Server.universeDirectory, "Kerbals", kerbalName + ".txt"));
                    }
                }
                //Relay the message.
                NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.KERBAL_REMOVE, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
                Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
                ClientHandler.SendToAll(client, newMessage, false);
            }
        }
    }
}

