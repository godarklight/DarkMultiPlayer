using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class PlayerColor
    {
        public static void SendAllPlayerColors(ClientObject client)
        {
            Dictionary<string,float[]> sendColors = new Dictionary<string, float[]>();
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
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.PLAYER_COLOR;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)PlayerColorMessageType.LIST);
                mw.Write<int>(sendColors.Count);
                foreach (KeyValuePair<string, float[]> kvp in sendColors)
                {
                    mw.Write<string>(kvp.Key);
                    mw.Write<float[]>(kvp.Value);
                }
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        public static void HandlePlayerColor(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
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
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.PLAYER_COLOR;
                            newMessage.data = messageData;
                            ClientHandler.SendToAll(client, newMessage, true);
                        }
                        break;
                }
            }
        }
    }
}

