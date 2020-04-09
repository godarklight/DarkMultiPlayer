using System;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class ConnectionEnd
    {
        public static void SendConnectionEnd(ClientObject client, string reason)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.CONNECTION_END, 2048, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<string>(reason);
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        public static void SendConnectionEndToAll(string reason)
        {
            foreach (ClientObject client in ClientHandler.GetClients())
            {
                if (client.authenticated)
                {
                    SendConnectionEnd(client, reason);
                }
            }
        }

        public static void HandleConnectionEnd(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            string reason = "Unknown";
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                reason = mr.Read<string>();
            }
            DarkLog.Debug(client.playerName + " sent connection end message, reason: " + reason);
            ClientHandler.DisconnectClient(client);
        }
    }
}

