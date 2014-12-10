using System;
using System.Collections.Generic;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class VesselRequest
    {
        public static void HandleVesselsRequest(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                int sendVesselCount = 0;
                int cachedVesselCount = 0;
                List<string> clientRequested = new List<string>(mr.Read<string[]>());
                lock (Server.universeSizeLock)
                {
                    foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels")))
                    {
                        string vesselID = Path.GetFileNameWithoutExtension(file);
                        byte[] vesselData = File.ReadAllBytes(file);
                        string vesselObject = Common.CalculateSHA256Hash(vesselData);
                        if (clientRequested.Contains(vesselObject))
                        {
                            sendVesselCount++;
                            VesselProto.SendVessel(client, vesselID, vesselData);
                        }
                        else
                        {
                            cachedVesselCount++;
                        }
                    }
                }
                DarkLog.Debug("Sending " + client.playerName + " " + sendVesselCount + " vessels, cached: " + cachedVesselCount + "...");
                SendVesselsComplete(client);
            }
        }

        public static void SendVesselList(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_LIST;
            string[] vesselFiles = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels"));
            string[] vesselObjects = new string[vesselFiles.Length];
            for (int i = 0; i < vesselFiles.Length; i++)
            {
                vesselObjects[i] = Common.CalculateSHA256Hash(vesselFiles[i]);
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string[]>(vesselObjects);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, false);
        }



        private static void SendVesselsComplete(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_COMPLETE;
            ClientHandler.SendToClient(client, newMessage, false);
        }
    }
}

