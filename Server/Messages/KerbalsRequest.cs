using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class KerbalsRequest
    {
        public static void HandleKerbalsRequest(ClientObject client)
        {
            //The time sensitive SYNC_TIME is over by this point.
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(client.playerName);
                ServerMessage joinMessage = new ServerMessage();
                joinMessage.type = ServerMessageType.PLAYER_JOIN;
                joinMessage.data = mw.GetMessageBytes();
                ClientHandler.SendToAll(client, joinMessage, true);
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
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.KERBAL_REPLY;
            using (MessageWriter mw = new MessageWriter())
            {
                //Send the vessel with a send time of 0 so it instantly loads on the client.
                mw.Write<double>(0);
                mw.Write<string>(kerbalName);
                mw.Write<byte[]>(kerbalData);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, false);
        }

        private static void SendKerbalsComplete(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.KERBAL_COMPLETE;
            ClientHandler.SendToClient(client, newMessage, false);
            //Send vessel list needed for sync to the client
            VesselRequest.SendVesselList(client);
        }
    }
}

