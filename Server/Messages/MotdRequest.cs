using System;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class MotdRequest
    {
        public static void HandleMotdRequest(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            SendMotdReply(client);
        }

        private static void SendMotdReply(ClientObject client)
        {
            string newMotd = Settings.settingsStore.serverMotd;
            newMotd = newMotd.Replace("%name%", client.playerName);
            newMotd = newMotd.Replace(@"\n", Environment.NewLine);

            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.MOTD_REPLY, 16 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<string>(newMotd);
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

