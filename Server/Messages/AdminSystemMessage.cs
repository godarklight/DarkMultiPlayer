using System;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class AdminSystem
    {
        public static void SendAllAdmins(ClientObject client)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.ADMIN_SYSTEM, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write((int)AdminMessageType.LIST);
                mw.Write<string[]>(DarkMultiPlayerServer.AdminSystem.fetch.GetAdmins());
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

