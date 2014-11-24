using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class KerbalProto
    {
        public static void HandleKerbalProto(ClientObject client, byte[] messageData)
        {
            //Send kerbal
            using (MessageReader mr = new MessageReader(messageData))
            {
                //Don't care about subspace / send time.
                mr.Read<double>();
                string kerbalName = mr.Read<string>();
                DarkLog.Debug("Saving kerbal " + kerbalName + " from " + client.playerName);
                byte[] kerbalData = mr.Read<byte[]>();
                lock (Server.universeSizeLock)
                {
                    File.WriteAllBytes(Path.Combine(Server.universeDirectory, "Kerbals", kerbalName + ".txt"), kerbalData);
                }
            }
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.KERBAL_REPLY;
            newMessage.data = messageData;
            ClientHandler.SendToAll(client, newMessage, false);
        }

    }
}

