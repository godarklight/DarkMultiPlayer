using System;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class AdminSystem
    {
        public static void SendAllAdmins(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.ADMIN_SYSTEM;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write((int)AdminMessageType.LIST);
                mw.Write<string[]>(DarkMultiPlayerServer.AdminSystem.fetch.GetAdmins());
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

