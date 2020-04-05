using System;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class SyncTimeRequest
    {
        public static void HandleSyncTimeRequest(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.SYNC_TIME_REPLY, 16, NetworkMessageType.UNORDERED_UNRELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                using (MessageReader mr = new MessageReader(messageData.data))
                {
                    //Client send time
                    mw.Write<long>(mr.Read<long>());
                    //Server receive time
                    mw.Write<long>(DateTime.UtcNow.Ticks);
                }
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

