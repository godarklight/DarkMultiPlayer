using System;
using System.IO;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class VesselProto
    {
        public static void HandleVesselProto(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            //TODO: Relay the message as is so we can optimize it
            //Send vessel
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                //Don't care about planet time
                double planetTime = mr.Read<double>();
                string vesselGuid = mr.Read<string>();
                Permissions.fetch.SetVesselOwnerIfUnowned(new Guid(vesselGuid), client.playerName);
                if (!Permissions.fetch.PlayerHasVesselPermission(client.playerName, new Guid(vesselGuid)))
                {
                    Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked from the server, tried to update protected vessel");
                    return;
                }
                bool isDockingUpdate = mr.Read<bool>();
                bool isFlyingUpdate = mr.Read<bool>();
                byte[] possibleCompressedBytes = mr.Read<byte[]>();
                byte[] vesselData = Compression.DecompressIfNeeded(possibleCompressedBytes);
                if (isFlyingUpdate)
                {
                    DarkLog.Debug("Relaying FLYING vessel " + vesselGuid + " from " + client.playerName);
                }
                else
                {
                    if (!isDockingUpdate)
                    {
                        DarkLog.Debug("Saving vessel " + vesselGuid + " from " + client.playerName);
                    }
                    else
                    {
                        DarkLog.Debug("Saving DOCKED vessel " + vesselGuid + " from " + client.playerName);
                    }
                    lock (Server.universeSizeLock)
                    {
                        File.WriteAllBytes(Path.Combine(Server.universeDirectory, "Vessels", vesselGuid + ".txt"), vesselData);
                    }
                }

                //Relay message
                NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.VESSEL_PROTO, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
                Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
                ClientHandler.SendToAll(client, newMessage, false);
            }
        }

        public static void SendVessel(ClientObject client, string vesselGUID, byte[] vesselData)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.VESSEL_PROTO, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<double>(0);
                mw.Write<string>(vesselGUID);
                mw.Write<bool>(false);
                mw.Write<bool>(false);
                mw.Write<byte[]>(Compression.CompressIfNeeded(vesselData));
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToClient(client, newMessage, false);
        }
    }
}

