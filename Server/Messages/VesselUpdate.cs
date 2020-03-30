using System;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class VesselUpdate
    {
        public static void HandleVesselUpdate(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            //We only relay this message.
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.VESSEL_UPDATE, messageData.Length);
            newMessage.reliable = true;
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            ClientHandler.SendToAll(client, newMessage, false);
        }
    }
}

