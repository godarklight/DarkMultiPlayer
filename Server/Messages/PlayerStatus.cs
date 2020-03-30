using System;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class PlayerStatus
    {
        public static void SendAllPlayerStatus(ClientObject client)
        {
            foreach (ClientObject otherClient in ClientHandler.GetClients())
            {
                if (otherClient.authenticated)
                {
                    if (otherClient != client)
                    {
                        NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.PLAYER_STATUS, 2048);
                        newMessage.reliable = true;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<string>(otherClient.playerName);
                            mw.Write<string>(otherClient.playerStatus.vesselText);
                            mw.Write<string>(otherClient.playerStatus.statusText);
                            newMessage.data.size = (int)mw.GetMessageLength();
                        }
                        ClientHandler.SendToClient(client, newMessage, true);
                    }
                }
            }
        }

        public static void HandlePlayerStatus(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                string playerName = mr.Read<string>();
                if (playerName != client.playerName)
                {
                    DarkLog.Debug(client.playerName + " tried to send an update for " + playerName + ", kicking.");
                    Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for sending an update for another player");
                    return;
                }
                client.playerStatus.vesselText = mr.Read<string>();
                client.playerStatus.statusText = mr.Read<string>();
            }
            //Relay the message
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.PLAYER_STATUS, 2048 + messageData.Length);
            newMessage.reliable = true;
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.data.Length);
            ClientHandler.SendToAll(client, newMessage, false);
        }
    }
}

