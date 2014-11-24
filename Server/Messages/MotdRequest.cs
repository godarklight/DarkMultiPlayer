using System;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class MotdRequest
    {
        public static void HandleMotdRequest(ClientObject client)
        {
            SendMotdReply(client);
        }

        private static void SendMotdReply(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.MOTD_REPLY;

            string newMotd = Settings.settingsStore.serverMotd;
            newMotd = newMotd.Replace("%name%", client.playerName);
            newMotd = newMotd.Replace(@"\n", Environment.NewLine);

            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(newMotd);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

