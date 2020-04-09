using System;
using System.IO;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class KerbalsRequest
    {
        public static void HandleKerbalsRequest(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            //The time sensitive SYNC_TIME is over by this point.
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.PLAYER_JOIN, 2048, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<string>(client.playerName);
                newMessage.data.size = (int)mw.GetMessageLength();
                ClientHandler.SendToAll(client, newMessage, true);
            }
            Messages.ServerSettings.SendServerSettings(client);
            Messages.WarpControl.SendSetSubspace(client);
            Messages.WarpControl.SendAllSubspaces(client);
            Messages.PlayerColor.SendAllPlayerColors(client);
            Messages.PlayerStatus.SendAllPlayerStatus(client);
            Messages.ScenarioData.SendScenarioModules(client);
            Messages.WarpControl.SendAllReportedSkewRates(client);
            Messages.CraftLibrary.SendCraftList(client);
            Messages.Chat.SendPlayerChatChannels(client);
            Messages.LockSystem.SendAllLocks(client);
            Messages.AdminSystem.SendAllAdmins(client);
            //Send kerbals
            lock (Server.universeSizeLock)
            {
                string[] kerbalFiles = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Kerbals"));
                foreach (string kerbalFile in kerbalFiles)
                {
                    string kerbalName = Path.GetFileNameWithoutExtension(kerbalFile);
                    byte[] kerbalData = File.ReadAllBytes(kerbalFile);
                    SendKerbal(client, kerbalName, kerbalData);
                }
                DarkLog.Debug("Sending " + client.playerName + " " + kerbalFiles.Length + " kerbals...");
            }
            SendKerbalsComplete(client);
        }

        private static void SendKerbal(ClientObject client, string kerbalName, byte[] kerbalData)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.KERBAL_REPLY, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                //Send the vessel with a send time of 0 so it instantly loads on the client.
                mw.Write<double>(0);
                mw.Write<string>(kerbalName);
                mw.Write<byte[]>(kerbalData);
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToClient(client, newMessage, false);
        }

        private static void SendKerbalsComplete(ClientObject client)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.KERBAL_COMPLETE, 0, NetworkMessageType.ORDERED_RELIABLE);
            ClientHandler.SendToClient(client, newMessage, false);
            //Send vessel list needed for sync to the client
            VesselRequest.SendVesselList(client);
        }
    }
}

