using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class PlayerColor
    {
        public static void SendAllPlayerColors(ClientObject client)
        {
            Dictionary<string, float[]> sendColors = new Dictionary<string, float[]>();
            foreach (ClientObject otherClient in ClientHandler.GetClients())
            {
                if (otherClient.authenticated && otherClient.playerColor != null)
                {
                    if (otherClient != client)
                    {
                        sendColors[otherClient.playerName] = otherClient.playerColor;
                    }
                }
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.PLAYER_COLOR, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<int>((int)PlayerColorMessageType.LIST);
                mw.Write<int>(sendColors.Count);
                foreach (KeyValuePair<string, float[]> kvp in sendColors)
                {
                    mw.Write<string>(kvp.Key);
                    mw.Write<float[]>(kvp.Value);
                }
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        public static void HandlePlayerColor(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                PlayerColorMessageType messageType = (PlayerColorMessageType)mr.Read<int>();
                switch (messageType)
                {
                    case PlayerColorMessageType.SET:
                        {
                            string playerName = mr.Read<string>();
                            if (playerName != client.playerName)
                            {
                                DarkLog.Debug(client.playerName + " tried to send a color update for " + playerName + ", kicking.");
                                Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for sending a color update for another player");
                                return;
                            }
                            client.playerColor = mr.Read<float[]>();
                            //Relay the message
                            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.PLAYER_COLOR, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
                            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
                            ClientHandler.SendToAll(client, newMessage, true);
                        }
                        break;
                }
            }
        }
    }
}

