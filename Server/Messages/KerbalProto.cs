using System;
using System.IO;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class KerbalProto
    {
        public static void HandleKerbalProto(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            //Send kerbal
            using (MessageReader mr = new MessageReader(messageData.data))
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
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.KERBAL_REPLY, messageData.Length);
            newMessage.reliable = true;
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            ClientHandler.SendToAll(client, newMessage, false);
        }

    }
}

