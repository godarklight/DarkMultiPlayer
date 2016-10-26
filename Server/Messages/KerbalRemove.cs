using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class KerbalRemove
    {
        public static void HandleKerbalRemoval(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                mr.Read<double>();
                string kerbalName = mr.Read<string>();
                DarkLog.Debug("Removing kerbal " + kerbalName + " from " + client.playerName);

                if (File.Exists(Path.Combine(Server.universeDirectory, "Kerbals", kerbalName + ".txt")))
                {
                    lock (Server.universeSizeLock)
                    {
                        File.Delete(Path.Combine(Server.universeDirectory, "Kerbals", kerbalName + ".txt"));
                    }
                }
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.KERBAL_REMOVE;
                newMessage.data = messageData;
                ClientHandler.SendToClient(client, newMessage, false);
                //TODO: Check for kerbal ownership (node DMP:contractKerbalOwner or refactor KerbalProto.cs to append the player name to the file or save player-specific Kerbals to the player Scenarios folder
            }
        }
    }
}

