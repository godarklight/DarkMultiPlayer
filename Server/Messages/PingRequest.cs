using System;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class PingRequest
    {
        public static void HandlePingRequest(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.PING_REPLY;
            newMessage.data = messageData;
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

