using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream;

namespace DarkMultiPlayerServer.Messages
{
    public class VesselProto
    {
        public static void HandleVesselProto(ClientObject client, byte[] messageData)
        {
            //TODO: Relay the message as is so we can optimize it
            //Send vessel
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //Don't care about planet time
                mr.Read<double>();
                string vesselGuid = mr.Read<string>();
                bool isDockingUpdate = mr.Read<bool>();
                bool isFlyingUpdate = mr.Read<bool>();
                byte[] vesselData = mr.Read<byte[]>();
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
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.VESSEL_PROTO;
                newMessage.data = messageData;
                ClientHandler.SendToAll(client, newMessage, false);
            }
        }
    }
}

