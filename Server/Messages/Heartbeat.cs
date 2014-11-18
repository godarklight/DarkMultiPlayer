using System;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer.Messages
{
    public class Heartbeat
    {
        public static void CheckHeartBeat(ClientObject client)
        {
            long currentTime = Server.serverClock.ElapsedMilliseconds;
            if ((currentTime - client.lastReceiveTime) > Common.CONNECTION_TIMEOUT)
            {
                //Heartbeat timeout
                DarkLog.Normal("Disconnecting client " + client.playerName + ", endpoint " + client.endpoint + ", Connection timed out");
                ClientHandler.DisconnectClient(client);
            }
            else
            {
                if (client.sendMessageQueueHigh.Count == 0 && client.sendMessageQueueSplit.Count == 0 && client.sendMessageQueueLow.Count == 0)
                {
                    if ((currentTime - client.lastSendTime) > Common.HEART_BEAT_INTERVAL)
                    {
                        Messages.Heartbeat.Send(client);
                    }
                }
            }
        }

        public static void Send(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.HEARTBEAT;
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

