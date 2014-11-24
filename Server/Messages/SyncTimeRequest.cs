using System;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class SyncTimeRequest
    {
        public static void HandleSyncTimeRequest(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SYNC_TIME_REPLY;
            using (MessageWriter mw = new MessageWriter())
            {
                using (MessageReader mr = new MessageReader(messageData))
                {
                    //Client send time
                    mw.Write<long>(mr.Read<long>());
                    //Server receive time
                    mw.Write<long>(DateTime.UtcNow.Ticks);
                    newMessage.data = mw.GetMessageBytes();
                }
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

