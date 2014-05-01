using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using MessageStream;
using System.IO;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    public class ClientHandler
    {
        //No point support IPv6 until KSP enables it on their windows builds.
        private static TcpListener TCPServer;
        private static Queue<ClientObject> addClients;
        private static List<ClientObject> clients;
        private static Queue<ClientObject> deleteClients;
        private static Dictionary<int, Subspace> subspaces;
        private static string subspaceFile = Path.Combine(Server.universeDirectory, "subspace.txt");
        #region Main loop
        public static void ThreadMain()
        {
            addClients = new Queue<ClientObject>();
            clients = new List<ClientObject>();
            deleteClients = new Queue<ClientObject>();
            subspaces = new Dictionary<int, Subspace>();
            LoadSavedSubspace();
            SetupTCPServer();
            while (Server.serverRunning)
            {
                //Add new clients
                while (addClients.Count > 0)
                {
                    clients.Add(addClients.Dequeue());
                }
                //Process current clients
                foreach (ClientObject client in clients)
                {
                    CheckHeartBeat(client);
                    SendOutgoingMessages(client);
                }
                //Delete old clients
                while (deleteClients.Count > 0)
                {
                    clients.Remove(deleteClients.Dequeue());
                }
                Thread.Sleep(10);
            }
            ShutdownTCPServer();
        }
        #endregion
        #region Server setup
        private static void LoadSavedSubspace()
        {
            try
            {
                using (StreamReader sr = new StreamReader(subspaceFile))
                {
                    //Ignore the comment line.
                    string firstLine = "";
                    while (firstLine.StartsWith("#") || String.IsNullOrEmpty(firstLine))
                    {
                        firstLine = sr.ReadLine().Trim();
                    }
                    Subspace savedSubspace = new Subspace();
                    int subspaceID = Int32.Parse(firstLine);
                    savedSubspace.serverClock = Int64.Parse(sr.ReadLine().Trim());
                    savedSubspace.planetTime = Double.Parse(sr.ReadLine().Trim());
                    savedSubspace.subspaceSpeed = Single.Parse(sr.ReadLine().Trim());
                    subspaces.Add(subspaceID, savedSubspace);
                }
            }
            catch
            {
                DarkLog.Debug("Creating new subspace lock file");
                Subspace newSubspace = new Subspace();
                newSubspace.serverClock = DateTime.UtcNow.Ticks;
                newSubspace.planetTime = 100d;
                newSubspace.subspaceSpeed = 1f;
                subspaces.Add(0, newSubspace);
                SaveSubspace(0, newSubspace);
            }
        }

        private static int GetLatestSubspace()
        {
            int latestID = 0;
            double latestPlanetTime = 0;
            long currentTime = DateTime.UtcNow.Ticks;
            foreach (KeyValuePair<int,Subspace> subspace in subspaces)
            {
                double currentPlanetTime = subspace.Value.planetTime + (((currentTime - subspace.Value.serverClock) / 10000000) * subspace.Value.subspaceSpeed);
                if (currentPlanetTime > latestPlanetTime)
                {
                    latestID = subspace.Key;
                }
            }
            return latestID;
        }

        private static void SaveLatestSubspace()
        {
            int latestID = GetLatestSubspace();
            SaveSubspace(latestID, subspaces[latestID]);
        }

        private static void SaveSubspace(int subspaceID, Subspace subspace)
        {
            string subspaceFile = Path.Combine(Server.universeDirectory, "subspace.txt");
            using (StreamWriter sw = new StreamWriter(subspaceFile))
            {
                sw.WriteLine("#Incorrectly editing this file will cause weirdness. If there is any errors, the universe time will be reset.");
                sw.WriteLine("#This file can only be edited if the server is stopped.");
                sw.WriteLine("#Each variable is on a new line. They are subspaceID, server clock (from DateTime.UtcNow.Ticks), universe time, and subspace speed.");
                sw.WriteLine(subspaceID);
                sw.WriteLine(subspace.serverClock);
                sw.WriteLine(subspace.planetTime);
                sw.WriteLine(subspace.subspaceSpeed);
            }
        }

        private static void SetupTCPServer()
        {
            try
            {
                TCPServer = new TcpListener(new IPEndPoint(IPAddress.Any, Settings.port));
                TCPServer.Start(4);
                TCPServer.BeginAcceptTcpClient(new AsyncCallback(NewClientCallback), null);
            }
            catch (Exception e)
            {
                DarkLog.Normal("Error setting up server, Exception: " + e);
                Server.serverRunning = false;
            }
            Server.serverStarting = false;
        }

        private static void ShutdownTCPServer()
        {
            TCPServer.Stop();
        }

        private static void NewClientCallback(IAsyncResult ar)
        {
            if (Server.serverRunning)
            {
                try
                {
                    TcpClient newClient = TCPServer.EndAcceptTcpClient(ar);
                    SetupClient(newClient);
                    DarkLog.Normal("New client connection from " + newClient.Client.RemoteEndPoint);
                }
                catch
                {
                    DarkLog.Normal("Error accepting client!");
                }
                TCPServer.BeginAcceptTcpClient(new AsyncCallback(NewClientCallback), null);
            }
        }

        private static void SetupClient(TcpClient newClientConnection)
        {
            ClientObject newClientObject = new ClientObject();
            newClientObject.subspace = GetLatestSubspace();
            newClientObject.playerStatus = new PlayerStatus();
            newClientObject.connectionStatus = ConnectionStatus.CONNECTED;
            newClientObject.playerName = "Unknown";
            newClientObject.activeVessel = "";
            newClientObject.subspaceRate = 1f;
            newClientObject.endpoint = newClientConnection.Client.RemoteEndPoint.ToString();
            //Keep the connection reference
            newClientObject.connection = newClientConnection;
            //Add the queues
            newClientObject.sendMessageQueueHigh = new Queue<ServerMessage>();
            newClientObject.sendMessageQueueSplit = new Queue<ServerMessage>();
            newClientObject.sendMessageQueueLow = new Queue<ServerMessage>();
            newClientObject.receiveMessageQueue = new Queue<ClientMessage>();
            StartReceivingIncomingMessages(newClientObject);
            addClients.Enqueue(newClientObject);
        }
        #endregion
        #region Network related methods
        private static void CheckHeartBeat(ClientObject client)
        {
            if (client.sendMessageQueueHigh.Count == 0 && client.sendMessageQueueSplit.Count == 0 && client.sendMessageQueueLow.Count == 0)
            {
                long currentTime = Server.serverClock.ElapsedMilliseconds;
                if ((currentTime - client.lastSendTime) > Common.HEART_BEAT_INTERVAL)
                {
                    SendHeartBeat(client);
                }
            }
        }

        private static void SendOutgoingMessages(ClientObject client)
        {
            if (!client.isSendingToClient)
            {
                ServerMessage message = null;
                if (message == null && client.sendMessageQueueHigh.Count > 0)
                {
                    message = client.sendMessageQueueHigh.Dequeue();
                }
                if (message == null && client.sendMessageQueueSplit.Count > 0)
                {
                    message = client.sendMessageQueueSplit.Dequeue();
                }
                if (message == null && client.sendMessageQueueLow.Count > 0)
                {
                    message = client.sendMessageQueueLow.Dequeue();
                    //Splits large messages to higher priority messages can get into the queue faster
                    SplitAndRewriteMessage(client, ref message);
                }
                if (message != null)
                {
                    SendNetworkMessage(client, message);
                }
            }
        }

        private static void SplitAndRewriteMessage(ClientObject client, ref ServerMessage message)
        {
            if (message == null)
            {
                return;
            }
            if (message.data == null)
            {
                return;
            }
            if (message.data.Length > Common.SPLIT_MESSAGE_LENGTH)
            {
                ServerMessage newSplitMessage = new ServerMessage();
                newSplitMessage.type = ServerMessageType.SPLIT_MESSAGE;
                int splitBytesLeft = message.data.Length;
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)message.type);
                    mw.Write<int>(message.data.Length);
                    byte[] firstSplit = new byte[Common.SPLIT_MESSAGE_LENGTH];
                    Array.Copy(message.data, 0, firstSplit, 0, Common.SPLIT_MESSAGE_LENGTH);
                    mw.Write<byte[]>(firstSplit);
                    splitBytesLeft -= Common.SPLIT_MESSAGE_LENGTH;
                    newSplitMessage.data = mw.GetMessageBytes();
                    client.sendMessageQueueSplit.Enqueue(newSplitMessage);
                }

                int currentSplits = 1;

                while (splitBytesLeft > 0)
                {
                    ServerMessage currentSplitMessage = new ServerMessage();
                    currentSplitMessage.type = ServerMessageType.SPLIT_MESSAGE;
                    currentSplitMessage.data = new byte[Math.Min(splitBytesLeft, Common.SPLIT_MESSAGE_LENGTH)];
                    Array.Copy(message.data, message.data.Length - splitBytesLeft, currentSplitMessage.data, 0, currentSplitMessage.data.Length);
                    splitBytesLeft -= currentSplitMessage.data.Length;
                    currentSplits++;
                    client.sendMessageQueueSplit.Enqueue(currentSplitMessage);
                }
                message = client.sendMessageQueueSplit.Dequeue();
            }
        }

        private static void SendNetworkMessage(ClientObject client, ServerMessage message)
        {
            //Write the send times down in SYNC_TIME_REPLY packets
            if (message.type == ServerMessageType.SYNC_TIME_REPLY)
            {
                try
                {
                    using (MessageWriter mw = new MessageWriter())
                    {
                        using (MessageReader mr = new MessageReader(message.data, false))
                        {
                            //Client send time
                            mw.Write<long>(mr.Read<long>());
                            //Server receive time
                            mw.Write<long>(mr.Read<long>());
                            //Server send time
                            mw.Write<long>(DateTime.UtcNow.Ticks);
                            message.data = mw.GetMessageBytes();
                        }
                    }
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Error rewriting SYNC_TIME packet, Exception " + e);
                }
            }
            //Continue sending
            byte[] messageBytes;
            using (MessageWriter mw = new MessageWriter((int)message.type))
            {
                if (message.data != null)
                {
                    mw.Write<byte[]>(message.data);
                }
                messageBytes = mw.GetMessageBytes();
            }
            client.isSendingToClient = true;
            client.lastSendTime = Server.serverClock.ElapsedMilliseconds;
            if (client.connectionStatus == ConnectionStatus.CONNECTED)
            {
                try
                {
                    client.connection.GetStream().BeginWrite(messageBytes, 0, messageBytes.Length, new AsyncCallback(SendMessageCallback), client);
                }
                catch (Exception e)
                {
                    DarkLog.Normal("Client " + client.playerName + " disconnected, endpoint " + client.endpoint + " error: " + e.Message);
                    DisconnectClient(client);
                }
            }
            if (message.type == ServerMessageType.CONNECTION_END)
            {
                DarkLog.Normal("Client " + client.playerName + " disconnected, sent CONNECTION_END to endpoint " + client.endpoint);
                DisconnectClient(client);
            }
        }

        private static void SendMessageCallback(IAsyncResult ar)
        {
            ClientObject client = (ClientObject)ar.AsyncState;
            try
            {
                client.connection.GetStream().EndWrite(ar);
            }
            catch (Exception e)
            {
                DarkLog.Normal("Client " + client.playerName + " disconnected, endpoint " + client.endpoint + ", error: " + e.Message);
                DisconnectClient(client);
            }
            client.isSendingToClient = false;
        }

        private static void StartReceivingIncomingMessages(ClientObject client)
        {
            client.lastReceiveTime = Server.serverClock.ElapsedMilliseconds;
            //Allocate byte for header
            client.receiveMessage = new ClientMessage();
            client.receiveMessage.data = new byte[8];
            client.receiveMessageBytesLeft = client.receiveMessage.data.Length;
            try
            {
                client.connection.GetStream().BeginRead(client.receiveMessage.data, client.receiveMessage.data.Length - client.receiveMessageBytesLeft, client.receiveMessageBytesLeft, new AsyncCallback(ReceiveCallback), client);
            }
            catch (Exception e)
            {
                DarkLog.Normal("Connection error: " + e.Message);
                DisconnectClient(client);
            }
        }

        private static void ReceiveCallback(IAsyncResult ar)
        {
            ClientObject client = (ClientObject)ar.AsyncState;
            try
            {
                client.receiveMessageBytesLeft -= client.connection.GetStream().EndRead(ar);
                if (client.receiveMessageBytesLeft == 0)
                {
                    //We either have the header or the message data, let's do something
                    if (!client.isReceivingMessage)
                    {
                        //We have the header
                        using (MessageReader mr = new MessageReader(client.receiveMessage.data, true))
                        {
                            if (mr.GetMessageType() > (Enum.GetNames(typeof(ClientMessageType)).Length - 1))
                            {
                                //Malformed message, most likely from a non DMP-client.
                                SendConnectionEnd(client, "Invalid DMP message. Disconnected.");
                                DarkLog.Normal("Invalid DMP message from " + client.endpoint);
                                //Returning from ReceiveCallback will break the receive loop and stop processing any further messages.
                                return;
                            }
                            client.receiveMessage.type = (ClientMessageType)mr.GetMessageType();
                            int length = mr.GetMessageLength();
                            if (length == 0)
                            {
                                //Null message, handle it.
                                client.receiveMessage.data = null;
                                HandleMessage(client, client.receiveMessage);
                                client.receiveMessage.type = 0;
                                client.receiveMessage.data = new byte[8];
                                client.receiveMessageBytesLeft = client.receiveMessage.data.Length;
                            }
                            else
                            {
                                if (length < Common.MAX_MESSAGE_SIZE)
                                {
                                    client.isReceivingMessage = true;
                                    client.receiveMessage.data = new byte[length];
                                    client.receiveMessageBytesLeft = client.receiveMessage.data.Length;
                                }
                                else
                                {
                                    //Malformed message, most likely from a non DMP-client.
                                    SendConnectionEnd(client, "Invalid DMP message. Disconnected.");
                                    DarkLog.Normal("Invalid DMP message from " + client.endpoint);
                                    //Returning from ReceiveCallback will break the receive loop and stop processing any further messages.
                                    return;
                                }
                            }
                        }
                    }
                    else
                    {
                        //We have the message data to a non-null message, handle it
                        client.isReceivingMessage = false;
                        using (MessageReader mr = new MessageReader(client.receiveMessage.data, false))
                        {
                            client.receiveMessage.data = mr.Read<byte[]>();
                        }
                        HandleMessage(client, client.receiveMessage);
                        client.receiveMessage.type = 0;
                        client.receiveMessage.data = new byte[8];
                        client.receiveMessageBytesLeft = client.receiveMessage.data.Length;
                    }
                }
                if (client.connectionStatus == ConnectionStatus.CONNECTED)
                {
                    client.lastReceiveTime = Server.serverClock.ElapsedMilliseconds;
                    client.connection.GetStream().BeginRead(client.receiveMessage.data, client.receiveMessage.data.Length - client.receiveMessageBytesLeft, client.receiveMessageBytesLeft, new AsyncCallback(ReceiveCallback), client);
                }
            }
            catch (Exception e)
            {
                DarkLog.Normal("Connection error: " + e.Message);
                DisconnectClient(client);
            }
        }

        private static void DisconnectClient(ClientObject client)
        {
            if (client.connectionStatus != ConnectionStatus.DISCONNECTED)
            {
                client.connectionStatus = ConnectionStatus.DISCONNECTED;
                if (client.authenticated)
                {
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.PLAYER_DISCONNECT;
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<string>(client.playerName);
                        newMessage.data = mw.GetMessageBytes();
                    }
                    SendToAll(client, newMessage, true);
                }
                deleteClients.Enqueue(client);
                if (client.connection != null)
                {
                    client.connection.Close();
                }
            }
        }
        #endregion
        #region Message handling
        private static void HandleMessage(ClientObject client, ClientMessage message)
        {
            //Clients can only send HEARTBEATS, HANDSHAKE_REQUEST or CONNECTION_END's until they are authenticated.
            if (!client.authenticated && !(message.type == ClientMessageType.HEARTBEAT || message.type == ClientMessageType.HANDSHAKE_REQUEST || message.type == ClientMessageType.CONNECTION_END))
            {
                SendConnectionEnd(client, "You must authenticate before attempting to send a " + message.type.ToString() + " message");
                return;
            }

            try
            {

                switch (message.type)
                {
                    case ClientMessageType.HEARTBEAT:
                    //Don't do anything for heartbeats, they just keep the connection alive
                        break;
                    case ClientMessageType.HANDSHAKE_REQUEST:
                        HandleHandshakeRequest(client, message.data);
                        break;
                    case ClientMessageType.CHAT_MESSAGE:
                        HandleChatMessage(client, message.data);
                        break;
                    case ClientMessageType.PLAYER_STATUS:
                        HandlePlayerStatus(client, message.data);
                        break;
                    case ClientMessageType.SCENARIO_DATA:
                        HandleScenarioModuleData(client, message.data);
                        break;
                    case ClientMessageType.SYNC_TIME_REQUEST:
                        HandleSyncTimeRequest(client, message.data);
                        break;
                    case ClientMessageType.KERBALS_REQUEST:
                        HandleKerbalsRequest(client);
                        break;
                    case ClientMessageType.KERBAL_PROTO:
                        HandleKerbalProto(client, message.data);
                        break;
                    case ClientMessageType.VESSELS_REQUEST:
                        HandleVesselsRequest(client);
                        break;
                    case ClientMessageType.VESSEL_PROTO:
                        HandleVesselProto(client, message.data);
                        break;
                    case ClientMessageType.VESSEL_UPDATE:
                        HandleVesselUpdate(client, message.data);
                        break;
                    case ClientMessageType.VESSEL_REMOVE:
                        HandleVesselRemoval(client, message.data);
                        break;
                    case ClientMessageType.SEND_ACTIVE_VESSEL:
                        HandleSendActiveVessel(client, message.data);
                        break;
                    case ClientMessageType.WARP_CONTROL:
                        HandleWarpControl(client, message.data);
                        break;
                    case ClientMessageType.SPLIT_MESSAGE:
                        HandleSplitMessage(client, message.data);
                        break;
                    case ClientMessageType.CONNECTION_END:
                        HandleConnectionEnd(client, message.data);
                        break;
                    default:
                        DarkLog.Debug("Unhandled message type " + message.type);
                        SendConnectionEnd(client, "Unhandled message type " + message.type);
                        break;
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling " + message.type + " from " + client.playerName + ", exception: " + e);
                SendConnectionEnd(client, "Server failed to process " + message.type + " message");
            }
        }

        private static void HandleHandshakeRequest(ClientObject client, byte[] messageData)
        {

            int protocolVersion;
            string playerName = "";
            string playerGuid = Guid.Empty.ToString();
            string reason = "";
            //0 - Success
            int handshakeReponse = 0;            
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    protocolVersion = mr.Read<int>();
                    playerName = mr.Read<string>();
                    playerGuid = mr.Read<string>();
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error in HANDSHAKE_REQUEST from " + client.playerName + ": " + e);
                SendHandshakeReply(client, 99);
                SendConnectionEnd(client, "Malformed handshake");
                return;
            }
            if (protocolVersion != Common.PROTOCOL_VERSION)
            {
                //Protocol mismatch
                handshakeReponse = 1;
                reason = "Protocol mismatch";
            }
            if (handshakeReponse == 0)
            {
                //Check client isn't already connected
                foreach (ClientObject testClient in clients)
                {
                    if (client != testClient && testClient.playerName == playerName)
                    {
                        handshakeReponse = 2;
                        reason = "Client already connected";
                    }
                }
            }
            if (handshakeReponse == 0)
            {
                //Check the client isn't using a reserved name
                switch (playerName)
                {
                    case "Server":
                    case "Initial":
                        handshakeReponse = 3;
                        reason = "Kicked for using a reserved name";
                        break;
                }
            }
            if (handshakeReponse == 0)
            {
                //Check the client matches any database entry
                string storedPlayerFile = Path.Combine(Server.universeDirectory, "Players", playerName + ".txt");
                string storedPlayerGuid = "";
                if (File.Exists(storedPlayerFile))
                {
                    using (StreamReader sr = new StreamReader(storedPlayerFile))
                    {
                        storedPlayerGuid = sr.ReadLine();
                    }
                    if (playerGuid != storedPlayerGuid)
                    {
                        handshakeReponse = 4;
                        reason = "Invalid player token for user";
                    }
                }
                else
                {
                    DarkLog.Debug("Client " + playerName + " registered!");
                    using (StreamWriter sw = new StreamWriter(storedPlayerFile))
                    {
                        sw.WriteLine(playerGuid);
                    }
                }
            }
            client.playerName = playerName;
            if (handshakeReponse == 0)
            {
                client.authenticated = true;
                DarkLog.Normal("Client " + playerName + " handshook successfully!");
                //SEND ALL THE THINGS!

                if (!Directory.Exists(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)))
                {
                    Directory.CreateDirectory(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName));
                    foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", "Initial")))
                    {
                        File.Copy(file, Path.Combine(Server.universeDirectory, "Scenarios", playerName, Path.GetFileName(file)));
                    }
                }
                SendHandshakeReply(client, handshakeReponse);
                SendServerSettings(client);
                SendSetSubspace(client);
                SendAllActiveVessels(client);
                SendAllSubspaces(client);
                SendAllPlayerStatus(client);
                SendScenarioModules(client);
                SendAllReportedSkewRates(client);
            }
            else
            {
                DarkLog.Normal("Client " + playerName + " failed to handshake, reason " + reason);
                SendHandshakeReply(client, handshakeReponse);
                SendConnectionEnd(client, reason);
            }


        }

        private static void HandleChatMessage(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            newMessage.data = messageData;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string playerName = mr.Read<string>();
                string playerText = mr.Read<string>();
                DarkLog.Normal(playerName + ": " + playerText);
            }
            //Relay it back and to the other clients.
            SendToClient(client, newMessage, true);
            SendToAll(client, newMessage, true);
        }

        private static void HandleSyncTimeRequest(ClientObject client, byte[] messageData)
        {
            try
            {
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.SYNC_TIME_REPLY;
                using (MessageWriter mw = new MessageWriter())
                {
                    using (MessageReader mr = new MessageReader(messageData, false))
                    {
                        //Client send time
                        mw.Write<long>(mr.Read<long>());
                        //Server receive time
                        mw.Write<long>(DateTime.UtcNow.Ticks);
                        newMessage.data = mw.GetMessageBytes();
                    }
                }
                SendToClient(client, newMessage, true);

            }
            catch (Exception e)
            {
                DarkLog.Debug("Error in SYNC_TIME_REQUEST from " + client.playerName + ": " + e);
                DisconnectClient(client);
            }
        }

        private static void HandlePlayerStatus(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string playerName = mr.Read<string>();
                if (playerName != client.playerName)
                {
                    DarkLog.Debug(client.playerName + " tried to send an update for " + playerName + ", kicking.");
                    SendConnectionEnd(client, "Kicked for sending an update for another player");
                    return;
                }
                client.playerStatus.vesselText = mr.Read<string>();
                client.playerStatus.statusText = mr.Read<string>();
            }
            //Relay the message
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.PLAYER_STATUS;
            newMessage.data = messageData;
            SendToAll(client, newMessage, false);
        }

        private static void HandleScenarioModuleData(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //Don't care about subspace / send time.
                string[] scenarioName = mr.Read<string[]>();
                string[] scenarioData = mr.Read<string[]>();
                DarkLog.Debug("Saving " + scenarioName.Length + " scenario modules from " + client.playerName);

                for (int i = 0; i < scenarioName.Length; i++)
                {
                    using (StreamWriter sw = new StreamWriter(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName, scenarioName[i] + ".txt")))
                    {
                        sw.Write(scenarioData[i]);
                    }
                }
            }
        }

        private static void HandleKerbalsRequest(ClientObject client)
        {
            int kerbalCount = 0;
            while (File.Exists(Path.Combine(Server.universeDirectory, "Kerbals", kerbalCount + ".txt")))
            {
                using (StreamReader sr = new StreamReader(Path.Combine(Server.universeDirectory, "Kerbals", kerbalCount + ".txt")))
                {
                    string kerbalData = sr.ReadToEnd();
                    SendKerbal(client, kerbalCount, kerbalData);
                    kerbalCount++;
                }
            }
            DarkLog.Debug("Sending " + client.playerName + " " + kerbalCount + " kerbals...");
            SendKerbalsComplete(client);
        }

        private static void HandleKerbalProto(ClientObject client, byte[] messageData)
        {
            //Send kerbal
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //Don't care about subspace / send time.
                mr.Read<int>();
                mr.Read<double>();
                int kerbalID = mr.Read<int>();
                DarkLog.Debug("Saving kerbal " + kerbalID + " from " + client.playerName);
                string kerbalData = mr.Read<string>();
                using (StreamWriter sw = new StreamWriter(Path.Combine(Server.universeDirectory, "Kerbals", kerbalID + ".txt")))
                {
                    sw.Write(kerbalData);
                }
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.KERBAL_REPLY;
                newMessage.data = messageData;
                SendToAll(client, newMessage, false);
            }
        }

        private static void HandleVesselsRequest(ClientObject client)
        {
            int vesselCount = 0;
            foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels")))
            {
                using (StreamReader sr = new StreamReader(file))
                {
                    string vesselData = sr.ReadToEnd();
                    vesselCount++;
                    SendVessel(client, vesselData);
                }
            }
            DarkLog.Debug("Sending " + client.playerName + " " + vesselCount + " vessels...");
            SendVesselsComplete(client);
        }

        private static void HandleVesselProto(ClientObject client, byte[] messageData)
        {
            //Send vessel
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int subspaceID = mr.Read<int>();
                double planetTime = mr.Read<double>();
                string vesselGuid = mr.Read<string>();
                DarkLog.Debug("Saving vessel " + vesselGuid + " from " + client.playerName);
                string vesselData = mr.Read<string>();
                using (StreamWriter sw = new StreamWriter(Path.Combine(Server.universeDirectory, "Vessels", vesselGuid + ".txt")))
                {
                    sw.Write(vesselData);
                }
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>(subspaceID);
                    mw.Write<double>(planetTime);
                    mw.Write<string>(vesselData);
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.VESSEL_PROTO;
                    newMessage.data = mw.GetMessageBytes();
                    SendToAll(client, newMessage, false);
                }
            }
        }

        private static void HandleVesselUpdate(ClientObject client, byte[] messageData)
        {
            //We only relay this message.
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_UPDATE;
            newMessage.data = messageData;
            SendToAll(client, newMessage, false);
        }

        private static void HandleVesselRemoval(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //Don't care about the subspace on the server.
                mr.Read<int>();
                mr.Read<double>();
                string vesselID = mr.Read<string>();
                if (File.Exists(Path.Combine(Server.universeDirectory, "Vessels", vesselID + ".txt")))
                {
                    DarkLog.Debug("Removing vessel " + vesselID + " from " + client.playerName);
                    File.Delete(Path.Combine(Server.universeDirectory, "Vessels", vesselID + ".txt"));
                    //Relay the message.
                }
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.VESSEL_REMOVE;
                newMessage.data = messageData;
                SendToAll(client, newMessage, false);
            }
        }

        private static void HandleSendActiveVessel(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SET_ACTIVE_VESSEL;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //We don't care about the player name, just need to advance message reader past it.
                mr.Read<string>();
                string activeVessel = mr.Read<string>();
                client.activeVessel = activeVessel;
            }
            newMessage.data = messageData;
            SendToAll(client, newMessage, true);
        }

        private static void HandleWarpControl(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.WARP_CONTROL;
            newMessage.data = messageData;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                WarpMessageType warpType = (WarpMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                if (fromPlayer == client.playerName)
                {
                    if (warpType == WarpMessageType.NEW_SUBSPACE)
                    {
                        int newSubspaceID = mr.Read<int>();
                        if (subspaces.ContainsKey(newSubspaceID))
                        {
                            DarkLog.Debug("Kicked for trying to create an existing subspace");
                            SendConnectionEnd(client, "Kicked for trying to create an existing subspace");
                            return;
                        }
                        else
                        {
                            Subspace newSubspace = new Subspace();
                            newSubspace.serverClock = mr.Read<long>();
                            newSubspace.planetTime = mr.Read<double>();
                            newSubspace.subspaceSpeed = mr.Read<float>();
                            subspaces.Add(newSubspaceID, newSubspace);
                            client.subspace = newSubspaceID;
                            SaveLatestSubspace();
                        }
                    }
                    if (warpType == WarpMessageType.CHANGE_SUBSPACE)
                    {
                        client.subspace = mr.Read<int>();
                    }
                    if (warpType == WarpMessageType.REPORT_RATE)
                    {
                        int reportedSubspace = mr.Read<int>();
                        if (reportedSubspace == client.subspace)
                        {
                            float newSubspaceRateTotal = mr.Read<float>();
                            int newSubspaceRateCount = 1;
                            foreach (ClientObject otherClient in clients)
                            {
                                if (otherClient.authenticated && otherClient.subspace == reportedSubspace)
                                {
                                    newSubspaceRateTotal += otherClient.subspaceRate;
                                    newSubspaceRateCount++;
                                }
                            }
                            float newAverageRate = newSubspaceRateTotal / (float)newSubspaceRateCount;
                            if (newAverageRate < 0.5f)
                            {
                                newAverageRate = 0.5f;
                            }
                            if (newAverageRate > 1f)
                            {
                                newAverageRate = 1f;
                            }
                            //Relock the subspace if the rate is more than 3% out of the average
                            DarkLog.Debug("New average rate: " + newAverageRate + " for subspace " + client.subspace);
                            if (Math.Abs(subspaces[reportedSubspace].subspaceSpeed - newAverageRate) > 0.03f)
                            {
                                //New time = Old time + (seconds since lock * subspace rate)
                                long newServerClockTime = DateTime.UtcNow.Ticks;
                                float timeSinceLock = (DateTime.UtcNow.Ticks - subspaces[client.subspace].serverClock) / 10000000f;
                                double newPlanetariumTime = subspaces[client.subspace].planetTime + (timeSinceLock * subspaces[client.subspace].subspaceSpeed);
                                subspaces[client.subspace].serverClock = newServerClockTime;
                                subspaces[client.subspace].planetTime = newPlanetariumTime;
                                subspaces[client.subspace].subspaceSpeed = newAverageRate;
                                ServerMessage relockMessage = new ServerMessage();
                                relockMessage.type = ServerMessageType.WARP_CONTROL;
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write<int>((int)WarpMessageType.RELOCK_SUBSPACE);
                                    mw.Write<string>("Server");
                                    mw.Write<int>(client.subspace);
                                    mw.Write<long>(DateTime.UtcNow.Ticks);
                                    mw.Write<double>(newPlanetariumTime);
                                    mw.Write<float>(newAverageRate);
                                    relockMessage.data = mw.GetMessageBytes();
                                }
                                SaveLatestSubspace();
                                DarkLog.Debug("Subspace " + client.subspace + " locked to " + newAverageRate + "x speed.");
                                SendToClient(client, relockMessage, true);
                                SendToAll(client, relockMessage, true);
                            }
                        }
                    }
                }
                else
                {
                    DarkLog.Debug(client.playerName + " tried to send an update for " + fromPlayer + ", kicking.");
                    SendConnectionEnd(client, "Kicked for sending an update for another player");
                    return;
                }
            }
            SendToAll(client, newMessage, true);
        }

        private static void HandleSplitMessage(ClientObject client, byte[] messageData)
        {
            if (!client.isReceivingSplitMessage)
            {
                //New split message
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    client.receiveSplitMessage = new ClientMessage();
                    client.receiveSplitMessage.type = (ClientMessageType)mr.Read<int>();
                    client.receiveSplitMessage.data = new byte[mr.Read<int>()];
                    client.receiveSplitMessageBytesLeft = client.receiveSplitMessage.data.Length;
                    byte[] firstSplitData = mr.Read<byte[]>();
                    firstSplitData.CopyTo(client.receiveSplitMessage.data, 0);
                    client.receiveSplitMessageBytesLeft -= firstSplitData.Length;
                }
                client.isReceivingSplitMessage = true;
            }
            else
            {
                //Continued split message
                messageData.CopyTo(client.receiveSplitMessage.data, client.receiveSplitMessage.data.Length - client.receiveSplitMessageBytesLeft);
                client.receiveSplitMessageBytesLeft -= messageData.Length;
            }
            if (client.receiveSplitMessageBytesLeft == 0)
            {
                HandleMessage(client, client.receiveSplitMessage);
                client.receiveSplitMessage = null;
                client.isReceivingSplitMessage = false;
            }
        }

        private static void HandleConnectionEnd(ClientObject client, byte[] messageData)
        {
            string reason = "Unknown";
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    reason = mr.Read<string>();
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling CONNECTION_END message from " + client.playerName + ":" + e);
            }
            DarkLog.Debug(client.playerName + " sent connection end message, reason: " + reason);
            DisconnectClient(client);
        }
        #endregion
        #region Message sending
        //Call with null client to send to all clients
        private static void SendToAll(ClientObject ourClient, ServerMessage message, bool highPriority)
        {
            foreach (ClientObject otherClient in clients)
            {
                if (ourClient != otherClient)
                {
                    SendToClient(otherClient, message, highPriority);
                }
            }
        }

        private static void SendToClient(ClientObject client, ServerMessage message, bool highPriority)
        {
            if (message == null)
            {
                Exception up = new Exception("Cannot send a null message to a client!");
                throw up;
            }
            else
            {
                if (highPriority)
                {
                    client.sendMessageQueueHigh.Enqueue(message);
                }
                else
                {
                    client.sendMessageQueueLow.Enqueue(message);
                }
            }
        }

        private static void SendHeartBeat(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.HEARTBEAT;
            SendToClient(client, newMessage, true);
        }

        private static void SendHandshakeReply(ClientObject client, int response)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.HANDSHAKE_REPLY;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(response);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendServerSettings(ClientObject client)
        {
            int numberOfKerbals = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Kerbals")).Length;
            int numberOfVessels = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels")).Length;
            int numberOfScenarioModules = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)).Length;
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SERVER_SETTINGS;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)Settings.warpMode);
                mw.Write<int>((int)Settings.gameMode);
                //Tack the amount of kerbals, vessels and scenario modules onto this message
                mw.Write<int>(numberOfKerbals);
                mw.Write<int>(numberOfVessels);
                mw.Write<int>(numberOfScenarioModules);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendAllPlayerStatus(ClientObject client)
        {
            foreach (ClientObject otherClient in clients)
            {
                if (otherClient.authenticated)
                {
                    if (otherClient != client)
                    {
                        ServerMessage newMessage = new ServerMessage();
                        newMessage.type = ServerMessageType.PLAYER_STATUS;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<string>(otherClient.playerName);
                            mw.Write<string>(otherClient.playerStatus.vesselText);
                            mw.Write<string>(otherClient.playerStatus.statusText);
                            newMessage.data = mw.GetMessageBytes();
                        }
                        SendToClient(client, newMessage, true);
                    }
                }
            }
        }

        private static void SendAllSubspaces(ClientObject client)
        {
            //Send all the locks.
            foreach (KeyValuePair<int, Subspace> subspace in subspaces)
            {
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.WARP_CONTROL;
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)WarpMessageType.NEW_SUBSPACE);
                    mw.Write<string>("");
                    mw.Write<int>(subspace.Key);
                    mw.Write<long>(subspace.Value.serverClock);
                    mw.Write<double>(subspace.Value.planetTime);
                    mw.Write<float>(subspace.Value.subspaceSpeed);
                    newMessage.data = mw.GetMessageBytes();
                }
                SendToClient(client, newMessage, true);
            }
            //Tell the player "when" everyone is.
            foreach (ClientObject otherClient in clients)
            {
                if (otherClient.authenticated && (otherClient.playerName != client.playerName))
                {
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.WARP_CONTROL;
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)WarpMessageType.CHANGE_SUBSPACE);
                        mw.Write<string>(otherClient.playerName);
                        mw.Write<int>(otherClient.subspace);
                        newMessage.data = mw.GetMessageBytes();
                    }
                    SendToClient(client, newMessage, true);
                }
            }
        }

        private static void SendScenarioModules(ClientObject client)
        {
            int numberOfScenarioModules = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)).Length;
            int currentScenarioModule = 0;
            string[] scenarioName = new string[numberOfScenarioModules];
            string[] scenarioData = new string[numberOfScenarioModules];
            foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)))
            {
                using (StreamReader sr = new StreamReader(file))
                {
                    //Remove the .txt part for the name
                    scenarioName[currentScenarioModule] = Path.GetFileNameWithoutExtension(file);
                    scenarioData[currentScenarioModule] = sr.ReadToEnd();
                    currentScenarioModule++;
                }
            }
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SCENARIO_DATA;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string[]>(scenarioName);
                mw.Write<string[]>(scenarioData);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendSetSubspace(ClientObject client)
        {
            int latestSubspace = -1;
            double latestPlanetTime = 0;
            foreach (KeyValuePair<int, Subspace> subspace in subspaces)
            {
                double subspaceTime = (((DateTime.UtcNow.Ticks - subspace.Value.serverClock) / 10000000d) * subspace.Value.subspaceSpeed) + subspace.Value.planetTime;
                if (subspaceTime > latestPlanetTime)
                {
                    latestSubspace = subspace.Key;
                    latestPlanetTime = subspaceTime;
                }
            }
            DarkLog.Debug("Sending " + client.playerName + " to subspace " + latestSubspace + ", time: " + latestPlanetTime);
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SET_SUBSPACE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(latestSubspace);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendKerbalsComplete(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.KERBAL_COMPLETE;
            SendToClient(client, newMessage, false);
        }

        private static void SendAllActiveVessels(ClientObject client)
        {
            foreach (ClientObject otherClient in clients)
            {
                if (otherClient.authenticated && otherClient.activeVessel != "")
                {
                    if (otherClient != client)
                    {
                        ServerMessage newMessage = new ServerMessage();
                        newMessage.type = ServerMessageType.SET_ACTIVE_VESSEL;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<string>(otherClient.playerName);
                            mw.Write<string>(otherClient.activeVessel);
                            newMessage.data = mw.GetMessageBytes();
                        }
                        SendToClient(client, newMessage, true);
                    }
                }
            }
        }

        private static void SendAllReportedSkewRates(ClientObject client)
        {
            foreach (ClientObject otherClient in clients)
            {
                if (otherClient.authenticated)
                {
                    if (otherClient != client)
                    {
                        ServerMessage newMessage = new ServerMessage();
                        newMessage.type = ServerMessageType.WARP_CONTROL;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<int>((int)WarpMessageType.REPORT_RATE);
                            mw.Write<string>(otherClient.playerName);
                            mw.Write<float>(otherClient.subspace);
                            mw.Write<float>(otherClient.subspaceRate);
                            newMessage.data = mw.GetMessageBytes();
                        }
                        SendToClient(client, newMessage, true);
                    }
                }
            }
        }

        private static void SendKerbal(ClientObject client, int kerbalID, string kerbalData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.KERBAL_REPLY;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(GetLatestSubspace());
                //Send the vessel with a send time of 0 so it instantly loads on the client.
                mw.Write<double>(0);
                mw.Write<int>(kerbalID);
                mw.Write<string>(kerbalData);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, false);
        }

        private static void SendVessel(ClientObject client, string vesselData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_PROTO;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(GetLatestSubspace());
                //Send the vessel with a send time of 0 so it instantly loads on the client.
                mw.Write<double>(0);
                mw.Write<string>(vesselData);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, false);
        }

        private static void SendVesselsComplete(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_COMPLETE;
            SendToClient(client, newMessage, false);
        }

        private static void SendConnectionEnd(ClientObject client, string reason)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CONNECTION_END;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(reason);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }
        #endregion
    }

    public class ClientObject
    {
        public bool authenticated;
        public string playerName;
        //subspace tracking
        public int subspace;
        public float subspaceRate;
        //vessel tracking
        public string activeVessel;
        //connection
        public string endpoint;
        public TcpClient connection;
        //Send buffer
        public long lastSendTime;
        public bool isSendingToClient;
        public Queue<ServerMessage> sendMessageQueueHigh;
        public Queue<ServerMessage> sendMessageQueueSplit;
        public Queue<ServerMessage> sendMessageQueueLow;
        public Queue<ClientMessage> receiveMessageQueue;
        public long lastReceiveTime;
        //Receive buffer
        public bool isReceivingMessage;
        public int receiveMessageBytesLeft;
        public ClientMessage receiveMessage;
        //Receive split buffer
        public bool isReceivingSplitMessage;
        public int receiveSplitMessageBytesLeft;
        public ClientMessage receiveSplitMessage;
        //State tracking
        public ConnectionStatus connectionStatus;
        public PlayerStatus playerStatus;
    }
}

