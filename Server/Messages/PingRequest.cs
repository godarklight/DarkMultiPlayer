using System;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class PingRequest
    {
        public static void HandlePingRequest(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.PING_REPLY, messageData.Length);
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

