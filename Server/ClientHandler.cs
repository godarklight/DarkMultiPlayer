using System;
using System.Threading;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
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
        private static ReadOnlyCollection<ClientObject> clients;
        private static ConcurrentQueue<ClientObject> deleteClients;
        private static Dictionary<int, Subspace> subspaces;
        private static Dictionary<string, int> playerSubspace;
        private static string subspaceFile = Path.Combine(Server.universeDirectory, "subspace.txt");
        private static string banlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPPlayerBans.txt");
        private static string ipBanlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPIPBans.txt");
        private static string guidBanlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPGuidBans.txt");
        private static string adminListFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPAdmins.txt");
        private static string whitelistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPWhitelist.txt");
        private static Dictionary<string, List<string>> playerChatChannels;
        private static List<string> bannedNames;
        private static List<IPAddress> bannedIPs;
        private static List<Guid> bannedGUIDs;
        private static List<string> banReasons;
        private static List<string> serverAdmins;
        private static List<string> serverWhitelist;
        private static Dictionary<string, int> playerUploadedScreenshotIndex;
        private static Dictionary<string, Dictionary<string,int>> playerDownloadedScreenshotIndex;
        private static Dictionary<string, string> playerWatchScreenshot;
        private static LockSystem lockSystem;
        //When a client hits 100kb on the send queue, DMPServer will throw out old duplicate messages
        private const int OPTIMIZE_QUEUE_LIMIT = 100 * 1024;
        #region Main loop
        public static void ThreadMain()
        {
            try
            {
                addClients = new Queue<ClientObject>();
                clients = new List<ClientObject>().AsReadOnly();
                deleteClients = new ConcurrentQueue<ClientObject>();
                subspaces = new Dictionary<int, Subspace>();
                playerSubspace = new Dictionary<string, int>();
                playerChatChannels = new Dictionary<string, List<string>>();
                bannedNames = new List<string>();
                bannedIPs = new List<IPAddress>();
                bannedGUIDs = new List<Guid>();
                banReasons = new List<string>();
                serverAdmins = new List<string>();
                serverWhitelist = new List<string>();
                playerUploadedScreenshotIndex = new Dictionary<string, int>();
                playerDownloadedScreenshotIndex = new Dictionary<string, Dictionary <string, int>>();
                playerWatchScreenshot = new Dictionary<string, string>();
                lockSystem = new LockSystem();
                LoadSavedSubspace();
                LoadBans();
                LoadAdmins();
                LoadWhitelist();
                SetupTCPServer();

                while (Server.serverRunning)
                {
                    //Add new clients
                    while (addClients.Count > 0)
                    {
                        //Create a new readonly collection from the current list and set clients. This prevents CollectionModified exceptions.
                        ClientObject addClient = addClients.Dequeue();
                        if (addClient != null)
                        {
                            List<ClientObject> newList = new List<ClientObject>(clients);
                            newList.Add(addClient);
                            clients = newList.AsReadOnly();
                            Server.playerCount = GetActiveClientCount();
                            Server.players = GetActivePlayerNames();
                            DarkLog.Debug("Online players is now: " + Server.playerCount + ", connected: " + clients.Count);
                        }
                    }
                    //Process current clients
                    foreach (ClientObject client in clients)
                    {
                        CheckHeartBeat(client);
                    }
                    //Check timers
                    NukeKSC.CheckTimer();
                    Dekessler.CheckTimer();
                    //Run plugin update
                    DMPPluginHandler.FireOnUpdate();
                    //Delete old clients
                    while (deleteClients.Count > 0)
                    {
                        ClientObject deleteClient;
                        if (deleteClients.TryDequeue(out deleteClient))
                        {
                            List<ClientObject> newList = new List<ClientObject>(clients);
                            newList.Remove(deleteClient);
                            clients = newList.AsReadOnly();
                            Server.playerCount = GetActiveClientCount();
                            Server.players = GetActivePlayerNames();
                            DarkLog.Debug("Online players is now: " + Server.playerCount + ", connected: " + clients.Count);
                            if (!Settings.settingsStore.keepTickingWhileOffline && clients.Count == 0)
                            {
                                UpdateSubspace(GetLatestSubspace());
                                SaveLatestSubspace();
                            }
                        }
                    }
                    Thread.Sleep(10);
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Fatal error thrown, exception: " + e);
                Server.ShutDown("Crashed!");
            }
            try
            {
                long disconnectTime = DateTime.UtcNow.Ticks;
                bool sendingHighPriotityMessages = true;
                while (sendingHighPriotityMessages)
                {
                    if ((DateTime.UtcNow.Ticks - disconnectTime) > 50000000)
                    {
                        DarkLog.Debug("Shutting down with " + Server.playerCount + " players, " + clients.Count + " connected clients");
                        break;
                    }
                    while (deleteClients.Count > 0)
                    {
                        ClientObject deleteClient = null;
                        if (deleteClients.TryDequeue(out deleteClient))
                        {
                            List<ClientObject> newList = new List<ClientObject>(clients);
                            newList.Remove(deleteClient);
                            clients = newList.AsReadOnly();
                            Server.playerCount = GetActiveClientCount();
                            Server.players = GetActivePlayerNames();
                        }
                    }
                    sendingHighPriotityMessages = false;
                    foreach (ClientObject client in clients)
                    {
                        if (client.authenticated && (client.sendMessageQueueHigh.Count > 0))
                        {
                            sendingHighPriotityMessages = true;
                        }
                    }
                    Thread.Sleep(10);
                }
                ShutdownTCPServer();
            }
            catch (Exception e)
            {
                DarkLog.Fatal("Fatal error thrown during shutdown, exception: " + e);
                throw;
            }
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

        private static void UpdateSubspace(int subspaceID)
        {
            //New time = Old time + (seconds since lock * subspace rate)
            long newServerClockTime = DateTime.UtcNow.Ticks;
            float timeSinceLock = (DateTime.UtcNow.Ticks - subspaces[subspaceID].serverClock) / 10000000f;
            double newPlanetariumTime = subspaces[subspaceID].planetTime + (timeSinceLock * subspaces[subspaceID].subspaceSpeed);
            subspaces[subspaceID].serverClock = newServerClockTime;
            subspaces[subspaceID].planetTime = newPlanetariumTime;
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
                IPAddress bindAddress = IPAddress.Parse(Settings.settingsStore.address);
                TCPServer = new TcpListener(new IPEndPoint(bindAddress, Settings.settingsStore.port));
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
            newClientObject.endpoint = newClientConnection.Client.RemoteEndPoint.ToString();
            newClientObject.ipAddress = (newClientConnection.Client.RemoteEndPoint as IPEndPoint).Address;
            //Keep the connection reference
            newClientObject.connection = newClientConnection;
            StartReceivingIncomingMessages(newClientObject);
            StartSendingOutgoingMessages(newClientObject);
            DMPPluginHandler.FireOnClientConnect(newClientObject);
            addClients.Enqueue(newClientObject);
        }

        private static void SaveAdmins()
        {
            DarkLog.Debug("Saving admin list");
            try
            {
                if (File.Exists(adminListFile))
                {
                    File.SetAttributes(adminListFile, FileAttributes.Normal);
                }

                using (StreamWriter sw = new StreamWriter(adminListFile))
                {
                    foreach (string user in serverAdmins)
                    {
                        sw.WriteLine(user);
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Error saving admin list!, Exception: " + e);
            }
        }

        private static void SaveWhitelist()
        {
            DarkLog.Debug("Saving whitelist");
            try
            {
                if (File.Exists(whitelistFile))
                {
                    File.SetAttributes(whitelistFile, FileAttributes.Normal);
                }

                using (StreamWriter sw = new StreamWriter(whitelistFile))
                {
                    foreach (string user in serverWhitelist)
                    {
                        sw.WriteLine(user);
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Error saving whitelist!, Exception: " + e);
            }
        }

        private static void SaveBans()
        {
            DarkLog.Debug("Saving bans");
            try
            {
                if (File.Exists(banlistFile))
                {
                    File.SetAttributes(banlistFile, FileAttributes.Normal);
                }

                using (StreamWriter sw = new StreamWriter(banlistFile))
                {

                    foreach (string name in bannedNames)
                    {
                        sw.WriteLine("{0}", name);
                    }
                }

                using (StreamWriter sw = new StreamWriter(ipBanlistFile))
                {
                    foreach (IPAddress ip in bannedIPs)
                    {
                        sw.WriteLine("{0}", ip);
                    }
                }

                using (StreamWriter sw = new StreamWriter(guidBanlistFile))
                {
                    foreach (Guid guid in bannedGUIDs)
                    {
                        sw.WriteLine("{0}", guid);
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Error saving bans!, Exception: " + e);
            }
        }

        private static void LoadAdmins()
        {
            DarkLog.Debug("Loading admin list");

            serverAdmins.Clear();

            if (File.Exists(adminListFile))
            {
                serverAdmins.AddRange(File.ReadAllLines(adminListFile));
            }
            else
            {
                SaveAdmins();
            }
        }

        private static void LoadWhitelist()
        {
            DarkLog.Debug("Loading whitelist");

            serverWhitelist.Clear();

            if (File.Exists(whitelistFile))
            {
                serverWhitelist.AddRange(File.ReadAllLines(whitelistFile));
            }
            else
            {
                SaveWhitelist();
            }
        }

        private static void LoadBans()
        {
            DarkLog.Debug("Loading bans");

            bannedNames.Clear();
            bannedIPs.Clear();
            bannedGUIDs.Clear();
            banReasons.Clear();

            if (File.Exists(banlistFile))
            {
                foreach (string line in File.ReadAllLines(banlistFile))
                {
                    if (!bannedNames.Contains(line))
                    {
                        bannedNames.Add(line);
                    }
                }
            }
            else
            {
                File.Create(banlistFile);
            }

            if (File.Exists(ipBanlistFile))
            {
                foreach (string line in File.ReadAllLines(ipBanlistFile))
                {
                    IPAddress banIPAddr = null;
                    if (IPAddress.TryParse(line, out banIPAddr))
                    {
                        if (!bannedIPs.Contains(banIPAddr))
                        {
                            bannedIPs.Add(banIPAddr);
                        }
                    }
                    else
                    {
                        DarkLog.Error("Error in IP ban list file, " + line + " is not an IP address");
                    }
                }
            }
            else
            {
                File.Create(ipBanlistFile);
            }

            if (File.Exists(guidBanlistFile))
            {
                foreach (string line in File.ReadAllLines(guidBanlistFile))
                {
                    Guid bannedGUID = Guid.Empty;
                    if (Guid.TryParse(line, out bannedGUID))
                    {
                        if (!bannedGUIDs.Contains(bannedGUID))
                        {
                            bannedGUIDs.Add(bannedGUID);
                        }
                    }
                    else
                    {
                        DarkLog.Error("Error in GUID ban list file, " + line + " is not an valid player token");
                    }
                }
            }
            else
            {
                File.Create(guidBanlistFile);
            }
        }

        private static int GetActiveClientCount()
        {
            int authenticatedCount = 0;
            foreach (ClientObject client in clients)
            {
                if (client.authenticated)
                {
                    authenticatedCount++;
                }
            }
            return authenticatedCount;
        }

        private static string GetActivePlayerNames()
        {
            string playerString = "";
            foreach (ClientObject client in clients)
            {
                if (client.authenticated)
                {
                    if (playerString != "")
                    {
                        playerString += ", ";
                    }
                    playerString += client.playerName;
                }
            }
            return playerString;
        }
        #endregion
        #region Network related methods
        private static void CheckHeartBeat(ClientObject client)
        {
            long currentTime = Server.serverClock.ElapsedMilliseconds;
            if ((currentTime - client.lastReceiveTime) > Common.CONNECTION_TIMEOUT)
            {
                //Heartbeat timeout
                DarkLog.Normal("Disconnecting client " + client.playerName + ", endpoint " + client.endpoint + ", Connection timed out");
                DisconnectClient(client);
            }
            else
            {
                if (client.sendMessageQueueHigh.Count == 0 && client.sendMessageQueueSplit.Count == 0 && client.sendMessageQueueLow.Count == 0)
                {
                    if ((currentTime - client.lastSendTime) > Common.HEART_BEAT_INTERVAL)
                    {
                        SendHeartBeat(client);
                    }
                }
            }
        }

        private static void StartSendingOutgoingMessages(ClientObject client)
        {
            Thread clientSendThread = new Thread(new ParameterizedThreadStart(SendOutgoingMessages));
            clientSendThread.IsBackground = true;
            clientSendThread.Start(client);
        }
        //ParameterizedThreadStart takes an object
        private static void SendOutgoingMessages(object client)
        {
            SendOutgoingMessages((ClientObject)client);
        }

        private static void SendOutgoingMessages(ClientObject client)
        {
            while (client.connectionStatus == ConnectionStatus.CONNECTED)
            {
                ServerMessage message = null;
                if (message == null && client.sendMessageQueueHigh.Count > 0)
                {
                    client.sendMessageQueueHigh.TryDequeue(out message);
                }
                //Don't send low or split during server shutdown.
                if (Server.serverRunning)
                {
                    if (message == null && client.sendMessageQueueSplit.Count > 0)
                    {
                        client.sendMessageQueueSplit.TryDequeue(out message);
                    }
                    if (message == null && client.sendMessageQueueLow.Count > 0)
                    {
                        client.sendMessageQueueLow.TryDequeue(out message);
                        //Splits large messages to higher priority messages can get into the queue faster
                        SplitAndRewriteMessage(client, ref message);
                    }
                }
                if (message != null)
                {
                    SendNetworkMessage(client, message);
                }
                else
                {
                    //Give the chance for the thread to terminate
                    client.sendEvent.WaitOne(1000);
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
                    //SPLIT_MESSAGE adds a 12 byte header.
                    client.bytesQueuedOut += 12;
                    client.sendMessageQueueSplit.Enqueue(newSplitMessage);
                }


                while (splitBytesLeft > 0)
                {
                    ServerMessage currentSplitMessage = new ServerMessage();
                    currentSplitMessage.type = ServerMessageType.SPLIT_MESSAGE;
                    currentSplitMessage.data = new byte[Math.Min(splitBytesLeft, Common.SPLIT_MESSAGE_LENGTH)];
                    Array.Copy(message.data, message.data.Length - splitBytesLeft, currentSplitMessage.data, 0, currentSplitMessage.data.Length);
                    splitBytesLeft -= currentSplitMessage.data.Length;
                    //Add the SPLIT_MESSAGE header to the out queue count.
                    client.bytesQueuedOut += 12;
                    client.sendMessageQueueSplit.Enqueue(currentSplitMessage);
                }
                client.sendMessageQueueSplit.TryDequeue(out message);
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
                            client.bytesQueuedOut += 8;
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
            client.lastSendTime = Server.serverClock.ElapsedMilliseconds;
            client.bytesQueuedOut -= messageBytes.Length;
            client.bytesSent += messageBytes.Length;
            if (client.connectionStatus == ConnectionStatus.CONNECTED)
            {
                try
                {
                    client.connection.GetStream().Write(messageBytes, 0, messageBytes.Length);
                }
                catch (Exception e)
                {
                    HandleDisconnectException("Send Network Message", client, e);
                    return;
                }
            }
            if (message.type == ServerMessageType.CONNECTION_END)
            {
                using (MessageReader mr = new MessageReader(message.data, false))
                {
                    string reason = mr.Read<string>();
                    DarkLog.Normal("Disconnecting client " + client.playerName + ", sent CONNECTION_END (" + reason + ") to endpoint " + client.endpoint);
                    client.disconnectClient = true;
                    DisconnectClient(client);
                }
            }
            if (message.type == ServerMessageType.HANDSHAKE_REPLY)
            {
                using (MessageReader mr = new MessageReader(message.data, false))
                {
                    int response = mr.Read<int>();
                    string reason = mr.Read<string>();
                    if (response != 0)
                    {
                        DarkLog.Normal("Disconnecting client " + client.playerName + ", sent HANDSHAKE_REPLY (" + reason + ") to endpoint " + client.endpoint);
                        client.disconnectClient = true;
                        DisconnectClient(client);
                    }
                }
            }
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
                HandleDisconnectException("Start Receive", client, e);
            }
        }

        private static void ReceiveCallback(IAsyncResult ar)
        {
            ClientObject client = (ClientObject)ar.AsyncState;
            try
            {
                int bytesRead = client.connection.GetStream().EndRead(ar);
                client.bytesReceived += bytesRead;
                client.receiveMessageBytesLeft -= bytesRead;
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
                HandleDisconnectException("ReceiveCallback", client, e);
            }
        }

        private static void HandleDisconnectException(string location, ClientObject client, Exception e)
        {
            lock (client.disconnectLock)
            {
                if (!client.disconnectClient && client.connectionStatus != ConnectionStatus.DISCONNECTED)
                {
                    if (e.InnerException != null)
                    {
                        DarkLog.Normal("Client " + client.playerName + " disconnected in " + location + ", endpoint " + client.endpoint + ", error: " + e.Message + " (" + e.InnerException.Message + ")");
                    }
                    else
                    {
                        DarkLog.Normal("Client " + client.playerName + " disconnected in " + location + ", endpoint " + client.endpoint + ", error: " + e.Message);
                    }
                }
                DisconnectClient(client);
            }
        }

        private static void DisconnectClient(ClientObject client)
        {
            lock (client.disconnectLock)
            {
                if (client.connectionStatus != ConnectionStatus.DISCONNECTED)
                {
                    DMPPluginHandler.FireOnClientDisconnect(client);
                    if (client.playerName != null)
                    {
                        if (playerChatChannels.ContainsKey(client.playerName))
                        {
                            playerChatChannels.Remove(client.playerName);
                        }
                        if (playerDownloadedScreenshotIndex.ContainsKey(client.playerName))
                        {
                            playerDownloadedScreenshotIndex.Remove(client.playerName);
                        }
                        if (playerUploadedScreenshotIndex.ContainsKey(client.playerName))
                        {
                            playerUploadedScreenshotIndex.Remove(client.playerName);
                        }
                        if (playerWatchScreenshot.ContainsKey(client.playerName))
                        {
                            playerWatchScreenshot.Remove(client.playerName);
                        }
                    }
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
                        lockSystem.ReleasePlayerLocks(client.playerName);
                    }
                    deleteClients.Enqueue(client);
                    try
                    {
                        if (client.connection != null)
                        {
                            client.connection.GetStream().Close();
                            client.connection.Close();
                        }
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("Error closing client connection: " + e.Message);
                    }
                    Server.lastPlayerActivity = Server.serverClock.ElapsedTicks;
                }
            }
        }
        #endregion
        #region Message handling
        private static void HandleMessage(ClientObject client, ClientMessage message)
        {
            //Prevent plugins from dodging SPLIT_MESSAGE. If they are modified, every split message will be broken.
            if (message.type != ClientMessageType.SPLIT_MESSAGE)
            {
                DMPPluginHandler.FireOnMessageReceived(client, message);

                if (message.handled)
                {
                    //a plugin has handled this message and requested suppression of the default DMP behavior
                    return;
                }
            }

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
                    case ClientMessageType.PLAYER_COLOR:
                        HandlePlayerColor(client, message.data);
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
                        HandleVesselsRequest(client, message.data);
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
                    case ClientMessageType.CRAFT_LIBRARY:
                        HandleCraftLibrary(client, message.data);
                        break;
                    case ClientMessageType.SCREENSHOT_LIBRARY:
                        HandleScreenshotLibrary(client, message.data);
                        break;
                    case ClientMessageType.FLAG_SYNC:
                        HandleFlagSync(client, message.data);
                        break;
                    case ClientMessageType.PING_REQUEST:
                        HandlePingRequest(client, message.data);
                        break;
                    case ClientMessageType.MOTD_REQUEST:
                        HandleMotdRequest(client);  
                        break;
                    case ClientMessageType.WARP_CONTROL:
                        HandleWarpControl(client, message.data);
                        break;
                    case ClientMessageType.LOCK_SYSTEM:
                        HandleLockSystemMessage(client, message.data);
                        break;
                    case ClientMessageType.MOD_DATA:
                        HandleModDataMessage(client, message.data);
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
            string clientVersion = "";
            string reason = "";
            Regex regex = new Regex(@"[\""<>|$]"); // Regex to detect quotation marks, and other illegal characters
            //0 - Success
            HandshakeReply handshakeReponse = HandshakeReply.HANDSHOOK_SUCCESSFULLY;
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    protocolVersion = mr.Read<int>();
                    playerName = mr.Read<string>();
                    playerGuid = mr.Read<string>();
                    clientVersion = mr.Read<string>();
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error in HANDSHAKE_REQUEST from " + client.playerName + ": " + e);
                SendHandshakeReply(client, HandshakeReply.MALFORMED_HANDSHAKE, "Malformed handshake");
                return;
            }
            if (regex.IsMatch(playerName))
            {
                // Invalid username
                handshakeReponse = HandshakeReply.INVALID_PLAYERNAME;
                reason = "Invalid username";
            }
            if (playerName.Contains("/") || playerName.Contains(@"\") || playerName.Contains("\n") || playerName.Contains("\r"))
            {
                handshakeReponse = HandshakeReply.INVALID_PLAYERNAME;
                reason = "Invalid username";
            }
            if (protocolVersion != Common.PROTOCOL_VERSION)
            {
                //Protocol mismatch
                handshakeReponse = HandshakeReply.PROTOCOL_MISMATCH;
                reason = "Protocol mismatch";
            }
            if (handshakeReponse == HandshakeReply.HANDSHOOK_SUCCESSFULLY)
            {
                //Check client isn't already connected
                ClientObject testClient = GetClientByName(playerName);
                if (testClient != null)
                {
                    SendHeartBeat(testClient);
                    Thread.Sleep(1000);
                }
                if (clients.Contains(testClient))
                {
                    handshakeReponse = HandshakeReply.ALREADY_CONNECTED;
                    reason = "Client already connected";
                }
            }
            if (handshakeReponse == HandshakeReply.HANDSHOOK_SUCCESSFULLY)
            {
                bool reserveKick = false;
                //Check the client isn't using a reserved name
                if (playerName == "Initial")
                {
                    reserveKick = true;
                }
                if (playerName == Settings.settingsStore.consoleIdentifier)
                {
                    reserveKick = true;
                }
                if (reserveKick)
                {
                    handshakeReponse = HandshakeReply.RESERVED_NAME;
                    reason = "Kicked for using a reserved name";
                }
            }
            if (handshakeReponse == HandshakeReply.HANDSHOOK_SUCCESSFULLY)
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
                        handshakeReponse = HandshakeReply.INVALID_TOKEN;
                        reason = "Invalid player token for user";
                    }
                }
                else
                {
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(storedPlayerFile))
                        {
                            sw.WriteLine(playerGuid);
                        }
                        
                        DarkLog.Debug("Client " + playerName + " registered!");
                    }
                    catch
                    {
                        handshakeReponse = HandshakeReply.INVALID_PLAYERNAME;
                        reason = "Invalid username";
                    }
                }
            }

            client.playerName = playerName;
            client.GUID = Guid.Parse(playerGuid);
            client.clientVersion = clientVersion;

            if (handshakeReponse == HandshakeReply.HANDSHOOK_SUCCESSFULLY)
            {
                if (bannedNames.Contains(client.playerName) || bannedIPs.Contains(client.ipAddress) || bannedGUIDs.Contains(client.GUID))
                {
                    handshakeReponse = HandshakeReply.PLAYER_BANNED;
                    reason = "You were banned from the server!";
                }
            }

            if (handshakeReponse == HandshakeReply.HANDSHOOK_SUCCESSFULLY)
            {
                if (GetActiveClientCount() >= Settings.settingsStore.maxPlayers)
                {
                    handshakeReponse = HandshakeReply.SERVER_FULL;
                    reason = "Server is full";
                }
            }

            if (handshakeReponse == HandshakeReply.HANDSHOOK_SUCCESSFULLY)
            {
                if (Settings.settingsStore.whitelisted && !serverWhitelist.Contains(client.playerName))
                {
                    handshakeReponse = HandshakeReply.NOT_WHITELISTED;
                    reason = "You are not on the whitelist";
                }
            }

            if (handshakeReponse == HandshakeReply.HANDSHOOK_SUCCESSFULLY)
            {
                client.authenticated = true;
                string devClientVersion = "";
                DMPPluginHandler.FireOnClientAuthenticated(client);

                if (client.clientVersion.Length == 40)
                {
                    devClientVersion = client.clientVersion.Substring(0, 7);
                }
                else
                {
                    devClientVersion = client.clientVersion;
                }
                DarkLog.Normal("Client " + playerName + " handshook successfully, version: " + devClientVersion);

                if (!Directory.Exists(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)))
                {
                    Directory.CreateDirectory(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName));
                    foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", "Initial")))
                    {
                        File.Copy(file, Path.Combine(Server.universeDirectory, "Scenarios", playerName, Path.GetFileName(file)));
                    }
                }
                SendHandshakeReply(client, handshakeReponse, "success");
                Server.playerCount = GetActiveClientCount();
                Server.players = GetActivePlayerNames();
                DarkLog.Debug("Online players is now: " + Server.playerCount + ", connected: " + clients.Count);

            }
            else
            {
                DarkLog.Normal("Client " + playerName + " failed to handshake: " + reason);
                SendHandshakeReply(client, handshakeReponse, reason);
            }


        }

        private static void HandleChatMessage(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            newMessage.data = messageData;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                ChatMessageType messageType = (ChatMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                if (fromPlayer != client.playerName)
                {
                    SendConnectionEnd(client, "Kicked for sending a chat message for another player");
                    return;
                }
                switch (messageType)
                {
                    case ChatMessageType.JOIN:
                        {
                            string joinChannel = mr.Read<string>();
                            if (!playerChatChannels.ContainsKey(fromPlayer))
                            {
                                playerChatChannels.Add(fromPlayer, new List<string>());
                            }
                            if (!playerChatChannels[fromPlayer].Contains(joinChannel))
                            {
                                playerChatChannels[fromPlayer].Add(joinChannel);
                            }
                            DarkLog.Debug(fromPlayer + " joined channel: " + joinChannel);
                        }
                        SendToAll(client, newMessage, true);
                        break;
                    case ChatMessageType.LEAVE:
                        {
                            string leaveChannel = mr.Read<string>();
                            if (playerChatChannels.ContainsKey(fromPlayer))
                            {
                                if (playerChatChannels[fromPlayer].Contains(leaveChannel))
                                {
                                    playerChatChannels[fromPlayer].Remove(leaveChannel);
                                }
                                if (playerChatChannels[fromPlayer].Count == 0)
                                {
                                    playerChatChannels.Remove(fromPlayer);
                                }
                            }
                            DarkLog.Debug(fromPlayer + " left channel: " + leaveChannel);
                        }
                        SendToAll(client, newMessage, true);
                        break;
                    case ChatMessageType.CHANNEL_MESSAGE:
                        {
                            string channel = mr.Read<string>();
                            string message = mr.Read<string>();
                            if (channel != "")
                            {
                                foreach (KeyValuePair<string, List<string>> playerEntry in playerChatChannels)
                                {
                                    if (playerEntry.Value.Contains(channel))
                                    {
                                        ClientObject findClient = GetClientByName(playerEntry.Key);
                                        if (findClient != null)
                                        {
                                            SendToClient(findClient, newMessage, true);
                                        }
                                    }
                                }
                                DarkLog.ChatMessage(fromPlayer + " -> #" + channel + ": " + message);
                            }
                            else
                            {
                                SendToClient(client, newMessage, true);
                                SendToAll(client, newMessage, true);
                                DarkLog.ChatMessage(fromPlayer + " -> #Global: " + message);
                            }
                        }
                        break;
                    case ChatMessageType.PRIVATE_MESSAGE:
                        {
                            string toPlayer = mr.Read<string>();
                            string message = mr.Read<string>();
                            if (toPlayer != Settings.settingsStore.consoleIdentifier)
                            {
                                ClientObject findClient = GetClientByName(toPlayer);
                                if (findClient != null)
                                {
                                    SendToClient(client, newMessage, true);
                                    SendToClient(findClient, newMessage, true);
                                    DarkLog.ChatMessage(fromPlayer + " -> @" + toPlayer + ": " + message);
                                }
                                {
                                    DarkLog.ChatMessage(fromPlayer + " -X-> @" + toPlayer + ": " + message);
                                }
                            }
                            else
                            {
                                SendToClient(client, newMessage, true);
                                DarkLog.ChatMessage(fromPlayer + " -> @" + toPlayer + ": " + message);
                            }
                        }
                        break;
                    case ChatMessageType.CONSOLE_MESSAGE:
                        {
                            string message = mr.Read<string>();
                            if (client.authenticated && serverAdmins.Contains(client.playerName))
                            {
                                CommandHandler.HandleServerInput(message);
                            }
                            else
                            {
                                SendConnectionEnd(client, "Kicked for sending a console command as a non-admin player.");
                            }
                        }
                        break;
                }
            }
        }

        private static void HandleSyncTimeRequest(ClientObject client, byte[] messageData)
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

        private static void HandlePlayerColor(ClientObject client, byte[] messageData)
        {

            using (MessageReader mr = new MessageReader(messageData, false))
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
                                SendConnectionEnd(client, "Kicked for sending a color update for another player");
                                return;
                            }
                            client.playerColor = mr.Read<float[]>();
                            //Relay the message
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.PLAYER_COLOR;
                            newMessage.data = messageData;
                            SendToAll(client, newMessage, true);
                        }
                        break;
                }
            }
        }

        private static void HandleScenarioModuleData(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //Don't care about subspace / send time.
                string[] scenarioName = mr.Read<string[]>();
                DarkLog.Debug("Saving " + scenarioName.Length + " scenario modules from " + client.playerName);

                for (int i = 0; i < scenarioName.Length; i++)
                {
                    byte[] scenarioData = mr.Read<byte[]>();
                    File.WriteAllBytes(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName, scenarioName[i] + ".txt"), scenarioData);
                }
            }
        }

        private static void HandleKerbalsRequest(ClientObject client)
        {
            //The time sensitive SYNC_TIME is over by this point.
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(client.playerName);
                ServerMessage joinMessage = new ServerMessage();
                joinMessage.type = ServerMessageType.PLAYER_JOIN;
                joinMessage.data = mw.GetMessageBytes();
                SendToAll(client, joinMessage, true);
            }
            SendServerSettings(client);
            SendSetSubspace(client);
            SendAllSubspaces(client);
            SendAllPlayerColors(client);
            SendAllPlayerStatus(client);
            SendScenarioModules(client);
            SendAllReportedSkewRates(client);
            SendCraftList(client);
            SendPlayerChatChannels(client);
            SendAllLocks(client);
            SendAllAdmins(client);
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

        private static void HandleKerbalProto(ClientObject client, byte[] messageData)
        {
            //Send kerbal
            using (MessageReader mr = new MessageReader(messageData, false))
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
            SendToAll(client, newMessage, false);
        }

        private static void HandleVesselsRequest(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int sendVesselCount = 0;
                int cachedVesselCount = 0;
                List<string> clientRequested = new List<string>(mr.Read<string[]>());
                lock (Server.universeSizeLock)
                {
                    foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels")))
                    {
                        byte[] vesselData = File.ReadAllBytes(file);
                        string vesselObject = Common.CalculateSHA256Hash(vesselData);
                        if (clientRequested.Contains(vesselObject))
                        {
                            sendVesselCount++;
                            SendVessel(client, vesselData);
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

        private static void HandleVesselProto(ClientObject client, byte[] messageData)
        {
            //TODO: Relay the message as is so we can optimize it
            //Send vessel
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                double planetTime = mr.Read<double>();
                string vesselGuid = mr.Read<string>();
                bool isDockingUpdate = mr.Read<bool>();
                bool isFlyingUpdate = mr.Read<bool>();
                byte[] vesselData = mr.Read<byte[]>();
                if (isFlyingUpdate)
                {
                    DarkLog.Debug("Relaying FLYING vessel " + vesselGuid + " from " + client.playerName);
                }
                else
                {
                    if (!isDockingUpdate)
                    {
                        DarkLog.Debug("Saving vessel " + vesselGuid + " from " + client.playerName);
                    }
                    else
                    {
                        DarkLog.Debug("Saving DOCKED vessel " + vesselGuid + " from " + client.playerName);
                    }
                    lock (Server.universeSizeLock)
                    {
                        File.WriteAllBytes(Path.Combine(Server.universeDirectory, "Vessels", vesselGuid + ".txt"), vesselData);
                    }
                }
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<double>(planetTime);
                    mw.Write<byte[]>(vesselData);
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
                bool isDockingUpdate = mr.Read<bool>();
                if (!isDockingUpdate)
                {
                    DarkLog.Debug("Removing vessel " + vesselID + " from " + client.playerName);
                }
                else
                {
                    DarkLog.Debug("Removing DOCKED vessel " + vesselID + " from " + client.playerName);
                }
                if (File.Exists(Path.Combine(Server.universeDirectory, "Vessels", vesselID + ".txt")))
                {
                    lock (Server.universeSizeLock)
                    {
                        File.Delete(Path.Combine(Server.universeDirectory, "Vessels", vesselID + ".txt"));
                    }
                }
                //Relay the message.
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.VESSEL_REMOVE;
                newMessage.data = messageData;
                SendToAll(client, newMessage, false);
            }
        }

        private static void HandleCraftLibrary(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                CraftMessageType craftMessageType = (CraftMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                if (fromPlayer != client.playerName)
                {
                    SendConnectionEnd(client, "Kicked for sending an craft library message for another player");
                    return;
                }
                switch (craftMessageType)
                {

                    case CraftMessageType.UPLOAD_FILE:
                        {
                            CraftType uploadType = (CraftType)mr.Read<int>();
                            string uploadName = mr.Read<string>();
                            byte[] uploadData = mr.Read<byte[]>();
                            string playerPath = Path.Combine(Path.Combine(Server.universeDirectory, "Crafts"), fromPlayer);
                            if (!Directory.Exists(playerPath))
                            {
                                Directory.CreateDirectory(playerPath);
                            }
                            string typePath = Path.Combine(playerPath, uploadType.ToString());
                            if (!Directory.Exists(typePath))
                            {
                                Directory.CreateDirectory(typePath);
                            }
                            string craftFile = Path.Combine(typePath, uploadName + ".craft");
                            File.WriteAllBytes(craftFile, uploadData);
                            DarkLog.Debug("Saving " + uploadName + ", type: " + uploadType.ToString() + " from " + fromPlayer);
                            using (MessageWriter mw = new MessageWriter())
                            {
                                ServerMessage newMessage = new ServerMessage();
                                newMessage.type = ServerMessageType.CRAFT_LIBRARY;
                                mw.Write<int>((int)CraftMessageType.ADD_FILE);
                                mw.Write<string>(fromPlayer);
                                mw.Write<int>((int)uploadType);
                                mw.Write<string>(uploadName);
                                newMessage.data = mw.GetMessageBytes();
                                SendToAll(client, newMessage, false);
                            }
                        }
                        break;
                    case CraftMessageType.REQUEST_FILE:
                        {
                            string craftOwner = mr.Read<string>();
                            CraftType requestedType = (CraftType)mr.Read<int>();
                            bool hasCraft = false;
                            string requestedName = mr.Read<string>();
                            string playerPath = Path.Combine(Path.Combine(Server.universeDirectory, "Crafts"), craftOwner);
                            string typePath = Path.Combine(playerPath, requestedType.ToString());
                            string craftFile = Path.Combine(typePath, requestedName + ".craft");
                            if (Directory.Exists(playerPath))
                            {
                                if (Directory.Exists(typePath))
                                {
                                    if (File.Exists(craftFile))
                                    {
                                        hasCraft = true;
                                    }
                                }
                            }
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.CRAFT_LIBRARY;
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write<int>((int)CraftMessageType.RESPOND_FILE);
                                mw.Write<string>(craftOwner);
                                mw.Write<int>((int)requestedType);
                                mw.Write<string>(requestedName);
                                mw.Write<bool>(hasCraft);
                                if (hasCraft)
                                {
                                    mw.Write<byte[]>(File.ReadAllBytes(craftFile));
                                    DarkLog.Debug("Sending " + fromPlayer + " " + requestedName + " from " + craftOwner);
                                }
                                newMessage.data = mw.GetMessageBytes();
                            }
                            SendToClient(client, newMessage, false);
                        }
                        break;
                    case CraftMessageType.DELETE_FILE:
                        {
                            CraftType craftType = (CraftType)mr.Read<int>();
                            string craftName = mr.Read<string>();
                            string playerPath = Path.Combine(Path.Combine(Server.universeDirectory, "Crafts"), fromPlayer);
                            string typePath = Path.Combine(playerPath, craftType.ToString());
                            string craftFile = Path.Combine(typePath, craftName + ".craft");
                            if (Directory.Exists(playerPath))
                            {
                                if (Directory.Exists(typePath))
                                {
                                    if (File.Exists(craftFile))
                                    {
                                        File.Delete(craftFile);
                                        DarkLog.Debug("Removing " + craftName + ", type: " + craftType.ToString() + " from " + fromPlayer);
                                    }
                                }
                            }
                            if (Directory.Exists(playerPath))
                            {
                                if (Directory.GetFiles(typePath).Length == 0)
                                {
                                    Directory.Delete(typePath);
                                }
                            }
                            if (Directory.GetDirectories(playerPath).Length == 0)
                            {
                                Directory.Delete(playerPath);
                            }
                            //Relay the delete message to other clients
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.CRAFT_LIBRARY;
                            newMessage.data = messageData;
                            SendToAll(client, newMessage, false);
                        }
                        break;
                }
            }
        }

        private static void HandleScreenshotLibrary(ClientObject client, byte[] messageData)
        {
            string screenshotDirectory = Path.Combine(Server.universeDirectory, "Screenshots");
            if (Settings.settingsStore.screenshotDirectory != "")
            {
                if (Directory.Exists(Settings.settingsStore.screenshotDirectory))
                {
                    screenshotDirectory = Settings.settingsStore.screenshotDirectory;
                }
            }
            if (!Directory.Exists(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
            }
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                ScreenshotMessageType messageType = (ScreenshotMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                switch (messageType)
                {
                    case ScreenshotMessageType.SCREENSHOT:
                        {
                            if (Settings.settingsStore.screenshotsPerPlayer > -1)
                            {
                                string playerScreenshotDirectory = Path.Combine(screenshotDirectory, fromPlayer);
                                if (!Directory.Exists(playerScreenshotDirectory))
                                {
                                    Directory.CreateDirectory(playerScreenshotDirectory);
                                }
                                string screenshotFile = Path.Combine(playerScreenshotDirectory, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
                                DarkLog.Debug("Saving screenshot from " + fromPlayer);

                                byte[] screenshotData = mr.Read<byte[]>();

                                File.WriteAllBytes(screenshotFile, screenshotData);
                                if (Settings.settingsStore.screenshotsPerPlayer != 0)
                                {
                                    while (Directory.GetFiles(playerScreenshotDirectory).Length > Settings.settingsStore.screenshotsPerPlayer)
                                    {
                                        string[] currentFiles = Directory.GetFiles(playerScreenshotDirectory);
                                        string deleteFile = currentFiles[0];
                                        //Find oldest file
                                        foreach (string testFile in currentFiles)
                                        {
                                            if (File.GetCreationTime(testFile) < File.GetCreationTime(deleteFile))
                                            {
                                                deleteFile = testFile;
                                            }
                                        }
                                        File.Delete(deleteFile);
                                        DarkLog.Debug("Removing old screenshot " + Path.GetFileName(deleteFile));
                                    }
                                }

                                //Notify players that aren't watching that there's a new screenshot availabe. This only works if there's a file available on the server.
                                //The server does not keep the screenshots in memory.
                                ServerMessage notifyMessage = new ServerMessage();
                                notifyMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write<int>((int)ScreenshotMessageType.NOTIFY);
                                    mw.Write(fromPlayer);
                                    notifyMessage.data = mw.GetMessageBytes();
                                    SendToAll(client, notifyMessage, false);
                                }
                            }
                            if (!playerUploadedScreenshotIndex.ContainsKey(fromPlayer))
                            {
                                playerUploadedScreenshotIndex.Add(fromPlayer, 0);
                            }
                            else
                            {
                                playerUploadedScreenshotIndex[fromPlayer]++;
                            }
                            if (!playerDownloadedScreenshotIndex.ContainsKey(fromPlayer))
                            {
                                playerDownloadedScreenshotIndex.Add(fromPlayer, new Dictionary<string, int>());
                            }
                            if (!playerDownloadedScreenshotIndex[fromPlayer].ContainsKey(fromPlayer))
                            {
                                playerDownloadedScreenshotIndex[fromPlayer].Add(fromPlayer, playerUploadedScreenshotIndex[fromPlayer]);
                            }
                            else
                            {
                                playerDownloadedScreenshotIndex[fromPlayer][fromPlayer] = playerUploadedScreenshotIndex[fromPlayer];
                            }
                            newMessage.data = messageData;
                            foreach (KeyValuePair<string, string> entry in playerWatchScreenshot)
                            {
                                if (entry.Key != fromPlayer)
                                {
                                    if (entry.Value == fromPlayer && entry.Key != client.playerName)
                                    {
                                        ClientObject toClient = GetClientByName(entry.Key);
                                        if (toClient != null && toClient != client)
                                        {
                                            if (!playerDownloadedScreenshotIndex.ContainsKey(entry.Key))
                                            {
                                                playerDownloadedScreenshotIndex.Add(entry.Key, new Dictionary<string, int>());
                                            }
                                            if (!playerDownloadedScreenshotIndex[entry.Key].ContainsKey(fromPlayer))
                                            {
                                                playerDownloadedScreenshotIndex[entry.Key].Add(fromPlayer, 0);
                                            }
                                            playerDownloadedScreenshotIndex[entry.Key][fromPlayer] = playerUploadedScreenshotIndex[fromPlayer];
                                            DarkLog.Debug("Sending screenshot from " + fromPlayer + " to " + entry.Key);
                                            using (MessageWriter mw = new MessageWriter())
                                            {
                                                ServerMessage sendStartMessage = new ServerMessage();
                                                sendStartMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
                                                mw.Write<int>((int)ScreenshotMessageType.SEND_START_NOTIFY);
                                                mw.Write<string>(fromPlayer);
                                                sendStartMessage.data = mw.GetMessageBytes();
                                                SendToClient(toClient, sendStartMessage, true);
                                            }
                                            SendToClient(toClient, newMessage, false);
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    case ScreenshotMessageType.WATCH:
                        {
                            newMessage.data = messageData;
                            string watchPlayer = mr.Read<string>();
                            if (watchPlayer == "")
                            {
                                if (playerWatchScreenshot.ContainsKey(fromPlayer))
                                {
                                    DarkLog.Debug(fromPlayer + " is no longer watching screenshots from " + playerWatchScreenshot[fromPlayer]);
                                    playerWatchScreenshot.Remove(fromPlayer);
                                }
                            }
                            else
                            {
                                DarkLog.Debug(fromPlayer + " is watching screenshots from " + watchPlayer);
                                playerWatchScreenshot[fromPlayer] = watchPlayer;
                                if (!playerDownloadedScreenshotIndex.ContainsKey(fromPlayer))
                                {
                                    playerDownloadedScreenshotIndex.Add(fromPlayer, new Dictionary<string, int>());
                                }
                                string watchPlayerScreenshotDirectory = Path.Combine(screenshotDirectory, watchPlayer);
                                //Find latest screenshot
                                string sendFile = null;
                                if (Directory.Exists(watchPlayerScreenshotDirectory))
                                {
                                    string[] playerScreenshots = Directory.GetFiles(watchPlayerScreenshotDirectory);
                                    if (playerScreenshots.Length > 0)
                                    {
                                        sendFile = playerScreenshots[0];
                                        foreach (string testFile in playerScreenshots)
                                        {
                                            if (File.GetCreationTime(testFile) > File.GetCreationTime(sendFile))
                                            {
                                                sendFile = testFile;
                                            }
                                        }
                                        if (!playerUploadedScreenshotIndex.ContainsKey(watchPlayer))
                                        {
                                            playerUploadedScreenshotIndex.Add(watchPlayer, 0);
                                        }
                                    }
                                }
                                //Send screenshot if needed
                                if (sendFile != null)
                                {
                                    bool sendScreenshot = false;
                                    if (!playerDownloadedScreenshotIndex[fromPlayer].ContainsKey(watchPlayer))
                                    {
                                        playerDownloadedScreenshotIndex[fromPlayer].Add(watchPlayer, playerUploadedScreenshotIndex[watchPlayer]);
                                        sendScreenshot = true;
                                    }
                                    else
                                    {
                                        if (playerDownloadedScreenshotIndex[fromPlayer][watchPlayer] != playerUploadedScreenshotIndex[watchPlayer])
                                        {
                                            sendScreenshot = true;
                                            playerDownloadedScreenshotIndex[fromPlayer][watchPlayer] = playerUploadedScreenshotIndex[watchPlayer];
                                        }
                                    }
                                    if (sendScreenshot)
                                    {
                                        ServerMessage sendStartMessage = new ServerMessage();
                                        sendStartMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
                                        using (MessageWriter mw = new MessageWriter())
                                        {
                                            mw.Write<int>((int)ScreenshotMessageType.SEND_START_NOTIFY);
                                            mw.Write<string>(fromPlayer);
                                            sendStartMessage.data = mw.GetMessageBytes();
                                        }
                                        ServerMessage screenshotMessage = new ServerMessage();
                                        screenshotMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
                                        using (MessageWriter mw = new MessageWriter())
                                        {
                                            mw.Write<int>((int)ScreenshotMessageType.SCREENSHOT);
                                            mw.Write<string>(watchPlayer);
                                            mw.Write<byte[]>(File.ReadAllBytes(sendFile));
                                            screenshotMessage.data = mw.GetMessageBytes();
                                        }
                                        ClientObject toClient = GetClientByName(fromPlayer);
                                        if (toClient != null)
                                        {
                                            DarkLog.Debug("Sending saved screenshot from " + watchPlayer + " to " + fromPlayer);
                                            SendToClient(toClient, sendStartMessage, false);
                                            SendToClient(toClient, screenshotMessage, false);
                                        }
                                    }
                                }
                            }
                            //Relay the message
                            SendToAll(client, newMessage, false);
                        }
                        break;
                }
            }
        }

        private static void HandleFlagSync(ClientObject client, byte[] messageData)
        {
            string flagPath = Path.Combine(Server.universeDirectory, "Flags");
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                FlagMessageType messageType = (FlagMessageType)mr.Read<int>();
                string playerName = mr.Read<string>();
                if (playerName != client.playerName)
                {
                    SendConnectionEnd(client, "Kicked for sending a flag for another player");
                    return;
                }
                switch (messageType)
                {
                    case FlagMessageType.LIST:
                        {
                            //Send the list back
                            List<string> serverFlagFileNames = new List<string>();
                            List<string> serverFlagOwners = new List<string>();
                            List<string> serverFlagShaSums = new List<string>();

                            string[] clientFlags = mr.Read<string[]>();
                            string[] clientFlagShas = mr.Read<string[]>();
                            string[] serverFlags = Directory.GetFiles(flagPath, "*", SearchOption.AllDirectories);
                            foreach (string serverFlag in serverFlags)
                            {
                                string trimmedName = Path.GetFileName(serverFlag);
                                string flagOwnerPath = Path.GetDirectoryName(serverFlag);
                                string flagOwner = flagOwnerPath.Substring(Path.GetDirectoryName(flagOwnerPath).Length + 1);
                                bool isMatched = false;
                                bool shaDifferent = false;
                                for (int i = 0; i < clientFlags.Length; i++)
                                {
                                    if (clientFlags[i].ToLower() == trimmedName.ToLower())
                                    {
                                        isMatched = true;
                                        shaDifferent = (Common.CalculateSHA256Hash(serverFlag) != clientFlagShas[i]);
                                    }
                                }
                                if (!isMatched || shaDifferent)
                                {
                                    if (flagOwner == client.playerName)
                                    {
                                        DarkLog.Debug("Deleting flag " + trimmedName);
                                        File.Delete(serverFlag);
                                        ServerMessage newMessage = new ServerMessage();
                                        newMessage.type = ServerMessageType.FLAG_SYNC;
                                        using (MessageWriter mw = new MessageWriter())
                                        {
                                            mw.Write<int>((int)FlagMessageType.DELETE_FILE);
                                            mw.Write<string>(trimmedName);
                                            newMessage.data = mw.GetMessageBytes();
                                            SendToAll(client, newMessage, false);
                                        }
                                        if (Directory.GetFiles(flagOwnerPath).Length == 0)
                                        {
                                            Directory.Delete(flagOwnerPath);
                                        }
                                    }
                                    else
                                    {
                                        DarkLog.Debug("Sending flag " + serverFlag + " from " + flagOwner + " to " + client.playerName);
                                        ServerMessage newMessage = new ServerMessage();
                                        newMessage.type = ServerMessageType.FLAG_SYNC;
                                        using (MessageWriter mw = new MessageWriter())
                                        {
                                            mw.Write<int>((int)FlagMessageType.FLAG_DATA);
                                            mw.Write<string>(flagOwner);
                                            mw.Write<string>(trimmedName);
                                            mw.Write<byte[]>(File.ReadAllBytes(serverFlag));
                                            newMessage.data = mw.GetMessageBytes();
                                            SendToClient(client, newMessage, false);
                                        }
                                    }
                                }
                                //Don't tell the client we have a different copy of the flag so it is reuploaded
                                if (File.Exists(serverFlag))
                                {
                                    serverFlagFileNames.Add(trimmedName);
                                    serverFlagOwners.Add(flagOwner);
                                    serverFlagShaSums.Add(Common.CalculateSHA256Hash(serverFlag));
                                }
                            }
                            ServerMessage listMessage = new ServerMessage();
                            listMessage.type = ServerMessageType.FLAG_SYNC;
                            using (MessageWriter mw2 = new MessageWriter())
                            {
                                mw2.Write<int>((int)FlagMessageType.LIST);
                                mw2.Write<string[]>(serverFlagFileNames.ToArray());
                                mw2.Write<string[]>(serverFlagOwners.ToArray());
                                mw2.Write<string[]>(serverFlagShaSums.ToArray());
                                listMessage.data = mw2.GetMessageBytes();
                            }
                            SendToClient(client, listMessage, false);
                        }
                        break;
                    case FlagMessageType.DELETE_FILE:
                        {
                            string flagName = mr.Read<string>();
                            string playerFlagPath = Path.Combine(flagPath, client.playerName);
                            if (Directory.Exists(playerFlagPath))
                            {
                                string flagFile = Path.Combine(playerFlagPath, flagName);
                                if (File.Exists(flagFile))
                                {
                                    File.Delete(flagFile);
                                }
                                if (Directory.GetFiles(playerFlagPath).Length == 0)
                                {
                                    Directory.Delete(playerFlagPath);
                                }
                            }
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.FLAG_SYNC;
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write<int>((int)FlagMessageType.DELETE_FILE);
                                mw.Write<string>(flagName);
                                newMessage.data = mw.GetMessageBytes();
                            }
                            SendToAll(client, newMessage, false);
                        }
                        break;
                    case FlagMessageType.UPLOAD_FILE:
                        {
                            string flagName = mr.Read<string>();
                            byte[] flagData = mr.Read<byte[]>();
                            string playerFlagPath = Path.Combine(flagPath, client.playerName);
                            if (!Directory.Exists(playerFlagPath))
                            {
                                Directory.CreateDirectory(playerFlagPath);
                            }
                            DarkLog.Debug("Saving flag " + flagName + " from " + client.playerName);
                            File.WriteAllBytes(Path.Combine(playerFlagPath, flagName), flagData);
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.FLAG_SYNC;
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write<int>((int)FlagMessageType.FLAG_DATA);
                                mw.Write<string>(client.playerName);
                                mw.Write<string>(flagName);
                                mw.Write<byte[]>(flagData);
                            }
                            SendToAll(client, newMessage, false);
                        }
                        break;
                }
            }
        }

        private static void HandlePingRequest(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.PING_REPLY;
            newMessage.data = messageData;
            SendToClient(client, newMessage, true);
        }

        private static void HandleMotdRequest(ClientObject client)
        {
            SendMotdReply(client);
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
                        if (client.subspace != reportedSubspace)
                        {
                            DarkLog.Debug("Warning, setting client " + client.playerName + " to subspace " + client.subspace);
                            client.subspace = reportedSubspace;
                        }
                        float newSubspaceRate = mr.Read<float>();
                        client.subspaceRate = newSubspaceRate;
                        foreach (ClientObject otherClient in clients)
                        {
                            if (otherClient.authenticated && otherClient.subspace == reportedSubspace)
                            {
                                if (newSubspaceRate > otherClient.subspaceRate)
                                {
                                    newSubspaceRate = otherClient.subspaceRate;
                                }
                            }
                        }
                        if (newSubspaceRate < 0.3f)
                        {
                            newSubspaceRate = 0.3f;
                        }
                        if (newSubspaceRate > 1f)
                        {
                            newSubspaceRate = 1f;
                        }
                        //Relock the subspace if the rate is more than 3% out of the average
                        if (Math.Abs(subspaces[reportedSubspace].subspaceSpeed - newSubspaceRate) > 0.03f)
                        {
                            UpdateSubspace(reportedSubspace);
                            subspaces[reportedSubspace].subspaceSpeed = newSubspaceRate;
                            ServerMessage relockMessage = new ServerMessage();
                            relockMessage.type = ServerMessageType.WARP_CONTROL;
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write<int>((int)WarpMessageType.RELOCK_SUBSPACE);
                                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                                mw.Write<int>(reportedSubspace);
                                mw.Write<long>(subspaces[reportedSubspace].serverClock);
                                mw.Write<double>(subspaces[reportedSubspace].planetTime);
                                mw.Write<float>(subspaces[reportedSubspace].subspaceSpeed);
                                relockMessage.data = mw.GetMessageBytes();
                            }
                            SaveLatestSubspace();
                            //DarkLog.Debug("Subspace " + client.subspace + " locked to " + newSubspaceRate + "x speed.");
                            SendToClient(client, relockMessage, true);
                            SendToAll(client, relockMessage, true);
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

        private static void HandleLockSystemMessage(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //All of the messages need replies, let's create a message for it.
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.LOCK_SYSTEM;
                //Read the lock-system message type
                LockMessageType lockMessageType = (LockMessageType)mr.Read<int>();
                switch (lockMessageType)
                {
                    case LockMessageType.ACQUIRE:
                        {
                            string playerName = mr.Read<string>();
                            string lockName = mr.Read<string>();
                            bool force = mr.Read<bool>();
                            if (playerName != client.playerName)
                            {
                                SendConnectionEnd(client, "Kicked for sending a lock message for another player");
                            }
                            bool lockResult = lockSystem.AcquireLock(lockName, playerName, force);
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write((int)LockMessageType.ACQUIRE);
                                mw.Write(playerName);
                                mw.Write(lockName);
                                mw.Write(lockResult);
                                newMessage.data = mw.GetMessageBytes();
                            }
                            //Send to all clients
                            SendToAll(null, newMessage, true);
                            if (lockResult)
                            {
                                DarkLog.Debug(playerName + " acquired lock " + lockName);
                            }
                            else
                            {
                                DarkLog.Debug(playerName + " failed to acquire lock " + lockName);
                            }
                        }
                        break;
                    case LockMessageType.RELEASE:
                        {
                            string playerName = mr.Read<string>();
                            string lockName = mr.Read<string>();
                            if (playerName != client.playerName)
                            {
                                SendConnectionEnd(client, "Kicked for sending a lock message for another player");
                            }
                            bool lockResult = lockSystem.ReleaseLock(lockName, playerName);
                            if (!lockResult)
                            {
                                SendConnectionEnd(client, "Kicked for releasing a lock you do not own");
                            }
                            else
                            {
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write((int)LockMessageType.RELEASE);
                                    mw.Write(playerName);
                                    mw.Write(lockName);
                                    mw.Write(lockResult);
                                    newMessage.data = mw.GetMessageBytes();
                                }
                                //Send to all clients
                                SendToAll(null, newMessage, true);
                            }
                            if (lockResult)
                            {
                                DarkLog.Debug(playerName + " released lock " + lockName);
                            }
                            else
                            {
                                DarkLog.Debug(playerName + " failed to release lock " + lockName);
                            }
                        }
                        break;
                }
            }
        }

        private static void HandleModDataMessage(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string modName = mr.Read<string>();
                bool relay = mr.Read<bool>();
                bool highPriority = mr.Read<bool>();
                byte[] modData = mr.Read<byte[]>();
                if (relay)
                {
                    DMPModInterface.SendDMPModMessageToAll(client, modName, modData, highPriority);
                }
                DMPModInterface.OnModMessageReceived(client, modName, modData);
            }
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
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                reason = mr.Read<string>();
            }
            DarkLog.Debug(client.playerName + " sent connection end message, reason: " + reason);
            DisconnectClient(client);
        }
        #endregion
        #region Message sending
        //Call with null client to send to all clients. Also called from Dekessler and NukeKSC.
        public static void SendToAll(ClientObject ourClient, ServerMessage message, bool highPriority)
        {
            foreach (ClientObject otherClient in clients)
            {
                if (ourClient != otherClient)
                {
                    SendToClient(otherClient, message, highPriority);
                }
            }
        }

        public static void SendToClient(ClientObject client, ServerMessage message, bool highPriority)
        {
            //Because we dodge the queue, we need to lock it up again...
            lock (client.sendLock)
            {
                if (message == null)
                {
                    return;
                }
                //All messages have an 8 byte header
                client.bytesQueuedOut += 8;
                if (message.data != null)
                {
                    //Count the payload if we have one. Byte[] serialisation has a further 4 byte header
                    client.bytesQueuedOut += 4 + message.data.Length;
                }
                if (highPriority)
                {
                    client.sendMessageQueueHigh.Enqueue(message);
                }
                else
                {
                    client.sendMessageQueueLow.Enqueue(message);
                    //If we need to optimize
                    if (client.bytesQueuedOut > OPTIMIZE_QUEUE_LIMIT)
                    {
                        //And we haven't optimized in the last 5 seconds
                        long currentTime = DateTime.UtcNow.Ticks;
                        long optimizedBytes = 0;
                        if ((currentTime - client.lastQueueOptimizeTime) > 50000000)
                        {
                            DarkLog.Debug("Optimizing " + client.playerName + " (" + client.bytesQueuedOut + " bytes queued)");
                            client.lastQueueOptimizeTime = currentTime;
                            //Create a temporary filter list
                            List<ServerMessage> oldClientMessagesToSend = new List<ServerMessage>();
                            List<ServerMessage> newClientMessagesToSend = new List<ServerMessage>();
                            //Steal all the messages from the queue and put them into a list
                            ServerMessage stealMessage = null;
                            while (client.sendMessageQueueLow.TryDequeue(out stealMessage))
                            {
                                oldClientMessagesToSend.Add(stealMessage);
                            }
                            //Clear the client send queue
                            //List<string> seenProtovesselUpdates = new List<string>();
                            List<string> seenPositionUpdates = new List<string>();
                            //Iterate backwards over the list
                            oldClientMessagesToSend.Reverse();
                            foreach (ServerMessage currentMessage in oldClientMessagesToSend)
                            {
                                //TODO: Relay VESSEL_PROTO as is so we can filter them.
                                if (currentMessage.type != ServerMessageType.VESSEL_UPDATE)
                                //if (currentMessage.type != ServerMessageType.VESSEL_PROTO && currentMessage.type != ServerMessageType.VESSEL_UPDATE)
                                {
                                    //Message isn't proto or position, don't skip it.
                                    newClientMessagesToSend.Add(currentMessage);
                                }
                                else
                                {
                                    //Message is proto or position
                                    //TODO: Uncomment and double check this block!
                                    /*
                                    if (currentMessage.type == ServerMessageType.VESSEL_PROTO)
                                    {
                                        using (MessageReader mr = new MessageReader(currentMessage.data, false))
                                        {
                                            //Don't care about the send time, it's already the latest in the queue.
                                            mr.Read<double>();
                                            string vesselID = mr.Read<string>();
                                            if (!seenProtovesselUpdates.Contains(vesselID))
                                            {
                                                seenProtovesselUpdates.Add(vesselID);
                                                newClientMessagesToSend.Add(currentMessage);
                                            }
                                            else
                                            {
                                                optimizedBytes += 8 + currentMessage.data.Length;
                                            }
                                        }
                                    }
                                    */

                                    if (currentMessage.type == ServerMessageType.VESSEL_UPDATE)
                                    {
                                        using (MessageReader mr = new MessageReader(currentMessage.data, false))
                                        {
                                            //Don't care about the send time, it's already the latest in the queue.
                                            mr.Read<double>();
                                            string vesselID = mr.Read<string>();
                                            if (!seenPositionUpdates.Contains(vesselID))
                                            {
                                                seenPositionUpdates.Add(vesselID);
                                                newClientMessagesToSend.Add(currentMessage);
                                            }
                                            else
                                            {
                                                //8 byte message header plus 4 byte payload header
                                                optimizedBytes += 12 + currentMessage.data.Length;
                                            }
                                        }
                                    }
                                }
                            }
                            //Flip it back to the right order
                            newClientMessagesToSend.Reverse();
                            foreach (ServerMessage putBackMessage in newClientMessagesToSend)
                            {
                                client.sendMessageQueueLow.Enqueue(putBackMessage);
                            }
                            float optimizeTime = (DateTime.UtcNow.Ticks - currentTime) / 10000f;
                            client.bytesQueuedOut -= optimizedBytes;
                            DarkLog.Debug("Optimized " + optimizedBytes + " bytes in " + Math.Round(optimizeTime, 3) + " ms.");
                        }
                    }
                }
                client.sendEvent.Set();
            }
        }

        public static ClientObject[] GetClients()
        {
            List<ClientObject> returnArray = new List<ClientObject>(clients);
            return returnArray.ToArray();
        }

        public static ClientObject GetClientByName(string playerName)
        {
            ClientObject findClient = null;
            foreach (ClientObject testClient in clients)
            {
                if (testClient.authenticated && testClient.playerName == playerName)
                {
                    findClient = testClient;
                    break;
                }
            }
            return findClient;
        }

        public static ClientObject GetClientByIP(IPAddress ipAddress)
        {
            ClientObject findClient = null;
            foreach (ClientObject testClient in clients)
            {
                if (testClient.authenticated && testClient.ipAddress == ipAddress)
                {
                    findClient = testClient;
                    break;
                }
            }
            return findClient;
        }

        public static ClientObject GetClientByGuid(Guid guid)
        {
            ClientObject findClient = null;
            foreach (ClientObject testClient in clients)
            {
                if (testClient.authenticated && testClient.GUID == guid)
                {
                    findClient = testClient;
                    break;
                }
            }
            return findClient;
        }

        private static void SendHeartBeat(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.HEARTBEAT;
            SendToClient(client, newMessage, true);
        }

        private static void SendHandshakeReply(ClientObject client, HandshakeReply enumResponse, string reason)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.HANDSHAKE_REPLY;
            int response = (int)enumResponse;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(response);
                mw.Write<string>(reason);
                mw.Write<int>(Common.PROTOCOL_VERSION);
                mw.Write<string>(Common.PROGRAM_VERSION);
                if (response == 0)
                {
                    mw.Write<int>((int)Settings.settingsStore.modControl);
                    if (Settings.settingsStore.modControl != ModControlMode.DISABLED)
                    {
                        if (!File.Exists(Server.modFile))
                        {
                            Server.GenerateNewModFile();
                        }
                        string modFileData = File.ReadAllText(Server.modFile);
                        mw.Write<string>(modFileData);
                    }
                }
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        public static void SendConsoleMessageToClient(ClientObject client, string message)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.CONSOLE_MESSAGE);
                mw.Write<string>(message);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, false);
        }

        public static void SendConsoleMessageToAdmins(string message)
        {
            if (clients == null)
            {
                //Early return to avoid throw while starting up.
                return;
            }
            foreach (ClientObject client in clients)
            {
                if (client.authenticated && serverAdmins.Contains(client.playerName))
                {
                    SendConsoleMessageToClient(client, message);
                }
            }
        }

        public static void SendConsoleMessageToAll(string message)
        {
            foreach (ClientObject client in clients)
            {
                SendConsoleMessageToClient(client, message);
            }
        }

        private static void SendChatMessageToClient(ClientObject client, string messageText)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.PRIVATE_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                mw.Write<string>(client.playerName);
                mw.Write(messageText);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        public static void SendChatMessageToAll(string messageText)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                //Global channel
                mw.Write<string>("");
                mw.Write(messageText);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToAll(null, newMessage, true);
        }

        public static void SendChatMessageToChannel(string channel, string messageText)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                // Channel
                mw.Write<string>(channel);
                mw.Write(messageText);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToAll(null, newMessage, true);
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
                mw.Write<int>((int)Settings.settingsStore.warpMode);
                mw.Write<int>((int)Settings.settingsStore.gameMode);
                mw.Write<bool>(Settings.settingsStore.cheats);
                //Tack the amount of kerbals, vessels and scenario modules onto this message
                mw.Write<int>(numberOfKerbals);
                mw.Write<int>(numberOfVessels);
                //mw.Write<int>(numberOfScenarioModules);
                mw.Write<int>(Settings.settingsStore.screenshotHeight);
                mw.Write<int>(Settings.settingsStore.numberOfAsteroids);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendCraftList(ClientObject client)
        {
            int numberOfCrafts = 0;
            string craftDirectory = Path.Combine(Server.universeDirectory, "Crafts");
            if (!Directory.Exists(craftDirectory))
            {
                Directory.CreateDirectory(craftDirectory);
            }
            string[] players = Directory.GetDirectories(craftDirectory);
            for (int i = 0; i < players.Length; i++)
            {
                players[i] = players[i].Substring(players[i].LastIndexOf(Path.DirectorySeparatorChar) + 1);
            }
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CRAFT_LIBRARY;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)CraftMessageType.LIST);
                mw.Write<string[]>(players);
                foreach (string player in players)
                {
                    string playerPath = Path.Combine(craftDirectory, player);
                    string vabPath = Path.Combine(playerPath, "VAB");
                    string sphPath = Path.Combine(playerPath, "SPH");
                    string subassemblyPath = Path.Combine(playerPath, "SUBASSEMBLY");
                    bool vabExists = Directory.Exists(vabPath);
                    bool sphExists = Directory.Exists(sphPath);
                    bool subassemblyExists = Directory.Exists(subassemblyPath);
                    mw.Write<bool>(vabExists);
                    mw.Write<bool>(sphExists);
                    mw.Write<bool>(subassemblyExists);
                    if (vabExists)
                    {
                        string[] vabCraftNames = Directory.GetFiles(vabPath);
                        for (int i = 0; i < vabCraftNames.Length; i++)
                        {
                            //We only want the craft names
                            vabCraftNames[i] = Path.GetFileNameWithoutExtension(vabCraftNames[i]);
                            numberOfCrafts++;
                        }
                        mw.Write<string[]>(vabCraftNames);
                    }

                    if (sphExists)
                    {
                        string[] sphCraftNames = Directory.GetFiles(sphPath);
                        for (int i = 0; i < sphCraftNames.Length; i++)
                        {
                            //We only want the craft names
                            sphCraftNames[i] = Path.GetFileNameWithoutExtension(sphCraftNames[i]);
                            numberOfCrafts++;
                        }
                        mw.Write<string[]>(sphCraftNames);
                    }

                    if (subassemblyExists)
                    {
                        string[] subassemblyCraftNames = Directory.GetFiles(subassemblyPath);
                        for (int i = 0; i < subassemblyCraftNames.Length; i++)
                        {
                            //We only want the craft names
                            subassemblyCraftNames[i] = Path.GetFileNameWithoutExtension(subassemblyCraftNames[i]);
                            numberOfCrafts++;
                        }
                        mw.Write<string[]>(subassemblyCraftNames);
                    }
                }
                newMessage.data = mw.GetMessageBytes();
                SendToClient(client, newMessage, true);
                DarkLog.Debug("Sending " + client.playerName + " " + numberOfCrafts + " craft library entries");
            }
        }

        private static void SendPlayerChatChannels(ClientObject client)
        {
            List<string> playerList = new List<string>();
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.LIST);
                foreach (KeyValuePair<string, List<string>> playerEntry in playerChatChannels)
                {
                    playerList.Add(playerEntry.Key);
                }
                mw.Write<string[]>(playerList.ToArray());
                foreach (string player in playerList)
                {
                    mw.Write<string[]>(playerChatChannels[player].ToArray());
                }
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.CHAT_MESSAGE;
                newMessage.data = mw.GetMessageBytes();
                SendToClient(client, newMessage, true);
            }
        }

        private static void SendAllLocks(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.LOCK_SYSTEM;
            //Send the dictionary as 2 string[]'s.
            Dictionary<string,string> lockList = lockSystem.GetLockList();
            List<string> lockKeys = new List<string>(lockList.Keys);
            List<string> lockValues = new List<string>(lockList.Values);
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write((int)LockMessageType.LIST);
                mw.Write<string[]>(lockKeys.ToArray());
                mw.Write<string[]>(lockValues.ToArray());
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendAllAdmins(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.ADMIN_SYSTEM;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write((int)AdminMessageType.LIST);
                mw.Write<string[]>(serverAdmins.ToArray());
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendAllPlayerColors(ClientObject client)
        {
            Dictionary<string,float[]> sendColors = new Dictionary<string, float[]>();
            foreach (ClientObject otherClient in clients)
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
            string[] scenarioNames = new string[numberOfScenarioModules];
            byte[][] scenarioDataArray = new byte[numberOfScenarioModules][];
            foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)))
            {
                //Remove the .txt part for the name
                scenarioNames[currentScenarioModule] = Path.GetFileNameWithoutExtension(file);
                scenarioDataArray[currentScenarioModule] = File.ReadAllBytes(file);
                currentScenarioModule++;
            }
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SCENARIO_DATA;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string[]>(scenarioNames);
                foreach (byte[] scenarioData in scenarioDataArray)
                {
                    mw.Write<byte[]>(scenarioData);
                }
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendSetSubspace(ClientObject client)
        {
            if (!Settings.settingsStore.keepTickingWhileOffline && clients.Count == 1)
            {
                DarkLog.Debug("Reverting server time to last player connection");
                long currentTime = DateTime.UtcNow.Ticks;
                foreach (KeyValuePair<int, Subspace> subspace in subspaces)
                {
                    subspace.Value.serverClock = currentTime;
                    subspace.Value.subspaceSpeed = 1f;
                    SaveLatestSubspace();
                }
            }
            int targetSubspace = -1;
            if (Settings.settingsStore.sendPlayerToLatestSubspace || !playerSubspace.ContainsKey(client.playerName))
            {
                DarkLog.Debug("Sending " + client.playerName + " to the latest subspace " + targetSubspace);
                targetSubspace = GetLatestSubspace();
            }
            else
            {
                DarkLog.Debug("Sending " + client.playerName + " to the previous subspace " + targetSubspace);
                targetSubspace = playerSubspace[client.playerName];
            }
            client.subspace = targetSubspace;
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SET_SUBSPACE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(targetSubspace);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
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

        private static void SendVesselList(ClientObject client)
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
            SendToClient(client, newMessage, false);
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
            SendToClient(client, newMessage, false);
        }

        private static void SendKerbalsComplete(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.KERBAL_COMPLETE;
            SendToClient(client, newMessage, false);
            //Send vessel list needed for sync to the client
            SendVesselList(client);
        }

        private static void SendVessel(ClientObject client, byte[] vesselData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_PROTO;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<double>(0);
                mw.Write<byte[]>(vesselData);
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

        private static void SendMotdReply(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.MOTD_REPLY;

            string newMotd = Settings.settingsStore.serverMotd;
            newMotd = newMotd.Replace("%name%", client.playerName);
            newMotd = newMotd.Replace(@"\n", Environment.NewLine);

            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(newMotd);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
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

        public static void SendConnectionEndToAll(string reason)
        {
            foreach (ClientObject client in clients)
            {
                if (client.authenticated)
                {
                    SendConnectionEnd(client, reason);
                }
            }
        }
        #endregion
        #region Server commands
        public static void KickPlayer(string commandArgs)
        {
            string playerName = commandArgs;
            string reason = "";
            if (commandArgs.Contains(" "))
            {
                playerName = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" ") + 1);
            }
            ClientObject player = null;

            if (playerName != "")
            {
                player = GetClientByName(playerName);
                if (player != null)
                {
                    DarkLog.Normal("Kicking " + playerName + " from the server");
                    if (reason != "")
                    {
                        SendConnectionEnd(player, "Kicked from the server, " + reason);
                    }
                    else
                    {
                        SendConnectionEnd(player, "Kicked from the server");
                    }
                }
            }
            else
            {
                DarkLog.Error("Syntax error. Usage: /kick playername [reason]");
            }
        }

        public static void BanPlayer(string commandArgs)
        {
            string playerName = commandArgs;
            string reason = "";

            if (commandArgs.Contains(" "))
            {
                playerName = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" ") + 1);
            }

            if (playerName != "")
            {

                ClientObject player = GetClientByName(playerName);

                if (reason == "")
                {
                    reason = "no reason specified";
                }

                if (player != null)
                {
                    SendConnectionEnd(player, "You were banned from the server!");
                }

                DarkLog.Normal("Player '" + playerName + "' was banned from the server: " + reason);
                bannedNames.Add(playerName);
                banReasons.Add(reason);
                SaveBans();
            }

        }

        public static void BanIP(string commandArgs)
        {
            string ip = commandArgs;
            string reason = "";

            if (commandArgs.Contains(" "))
            {
                ip = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" ") + 1);
            }

            IPAddress ipAddress;
            if (IPAddress.TryParse(ip, out ipAddress))
            {

                ClientObject player = GetClientByIP(ipAddress);

                if (reason == "")
                {
                    reason = "no reason specified";
                }

                if (player != null)
                {
                    SendConnectionEnd(player, "You were banned from the server!");
                }
                bannedIPs.Add(ipAddress);
                banReasons.Add(reason);
                SaveBans();

                DarkLog.Normal("IP Address '" + ip + "' was banned from the server: " + reason);
            }
            else
            {
                DarkLog.Normal(ip + " is not a valid IP address");
            }

        }

        public static void BanGuid(string commandArgs)
        {
            string guid = commandArgs;
            string reason = "";

            if (commandArgs.Contains(" "))
            {
                guid = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" ") + 1);
            }

            Guid bguid;
            if (Guid.TryParse(guid, out bguid))
            {

                ClientObject player = GetClientByGuid(bguid);

                if (reason == "")
                {
                    reason = "no reason specified";
                }

                if (player != null)
                {
                    SendConnectionEnd(player, "You were banned from the server!");
                }
                bannedGUIDs.Add(bguid);
                banReasons.Add(reason);
                SaveBans();

                DarkLog.Normal("GUID '" + guid + "' was banned from the server: " + reason);
            }
            else
            {
                DarkLog.Normal(guid + " is not a valid player token");
            }
        }

        public static void PMCommand(string commandArgs)
        {
            ClientObject pmPlayer = null;
            int matchedLength = 0;
            foreach (ClientObject testPlayer in clients)
            {
                //Only search authenticated players
                if (testPlayer.authenticated)
                {
                    //Try to match the longest player name
                    if (commandArgs.StartsWith(testPlayer.playerName) && testPlayer.playerName.Length > matchedLength)
                    {
                        //Double check there is a space after the player name
                        if ((commandArgs.Length > (testPlayer.playerName.Length + 1)) ? commandArgs[testPlayer.playerName.Length] == ' ' : false)
                        {
                            pmPlayer = testPlayer;
                            matchedLength = testPlayer.playerName.Length;
                        }
                    }
                }
            }
            if (pmPlayer != null)
            {
                string messageText = commandArgs.Substring(pmPlayer.playerName.Length + 1);
                SendChatMessageToClient(pmPlayer, messageText);
            }
            else
            {
                DarkLog.Normal("Player not found!");
            }
        }

        public static void AdminCommand(string commandArgs)
        {
            string func = "";
            string playerName = "";

            func = commandArgs;
            if (commandArgs.Contains(" "))
            {
                func = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                if (commandArgs.Substring(func.Length).Contains(" "))
                {
                    playerName = commandArgs.Substring(func.Length + 1);
                }
            }

            switch (func)
            {
                default:
                    DarkLog.Normal("Undefined function. Usage: /admin [add|del] playername or /admin show");
                    break;
                case "add":
                    if (File.Exists(Path.Combine(Server.universeDirectory, "Players", playerName + ".txt")))
                    {
                        if (!serverAdmins.Contains(playerName))
                        {
                            DarkLog.Debug("Added '" + playerName + "' to admin list.");
                            serverAdmins.Add(playerName);
                            SaveAdmins();
                            //Notify all players an admin has been added
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.ADMIN_SYSTEM;
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write<int>((int)AdminMessageType.ADD);
                                mw.Write<string>(playerName);
                                newMessage.data = mw.GetMessageBytes();
                            }
                            SendToAll(null, newMessage, true);
                        }
                        else
                        {
                            DarkLog.Normal("'" + playerName + "' is already an admin.");
                        }

                    }
                    else
                    {
                        DarkLog.Normal("'" + playerName + "' does not exist.");
                    }
                    break;
                case "del":
                    if (serverAdmins.Contains(playerName))
                    {
                        DarkLog.Normal("Removed '" + playerName + "' from the admin list.");
                        serverAdmins.Remove(playerName);
                        SaveAdmins();
                        //Notify all players an admin has been removed
                        ServerMessage newMessage = new ServerMessage();
                        newMessage.type = ServerMessageType.ADMIN_SYSTEM;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<int>((int)AdminMessageType.REMOVE);
                            mw.Write<string>(playerName);
                            newMessage.data = mw.GetMessageBytes();
                        }
                        SendToAll(null, newMessage, true);

                    }
                    else
                    {
                        DarkLog.Normal("'" + playerName + "' is not an admin.");
                    }
                    break;
                case "show":
                    foreach (string player in serverWhitelist)
                    {
                        DarkLog.Normal(player);
                    }
                    break;
            }
        }

        public static void WhitelistCommand(string commandArgs)
        {
            string func = "";
            string playerName = "";

            func = commandArgs;
            if (commandArgs.Contains(" "))
            {
                func = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                if (commandArgs.Substring(func.Length).Contains(" "))
                {
                    playerName = commandArgs.Substring(func.Length + 1);
                }
            }

            switch (func)
            {
                default:
                    DarkLog.Debug("Undefined function. Usage: /whitelist [add|del] playername or /whitelist show");
                    break;
                case "add":
                    if (!serverWhitelist.Contains(playerName))
                    {
                        DarkLog.Normal("Added '" + playerName + "' to whitelist.");
                        serverWhitelist.Add(playerName);
                        SaveWhitelist();
                    }
                    else
                    {
                        DarkLog.Normal("'" + playerName + "' is already on the whitelist.");
                    }
                    break;
                case "del":
                    if (serverWhitelist.Contains(playerName))
                    {
                        DarkLog.Normal("Removed '" + playerName + "' from the whitelist.");
                        serverWhitelist.Remove(playerName);
                        SaveWhitelist();
                    }
                    else
                    {
                        DarkLog.Normal("'" + playerName + "' is not on the whitelist.");
                    }
                    break;
                case "show":
                    foreach (string player in serverWhitelist)
                    {
                        DarkLog.Normal(player);
                    }
                    break;
            }
        }
        #endregion
    }

    public class ClientObject
    {
        public bool authenticated;
        public string playerName = "Unknown";
        public string clientVersion;
        public bool isBanned;
        public IPAddress ipAddress;
        public Guid GUID = Guid.Empty;
        //subspace tracking
        public int subspace = -1;
        public float subspaceRate = 1f;
        //vessel tracking
        public string activeVessel = "";
        //connection
        public string endpoint;
        public TcpClient connection;
        //Send buffer
        public long lastSendTime;
        public ConcurrentQueue<ServerMessage> sendMessageQueueHigh = new ConcurrentQueue<ServerMessage>();
        public ConcurrentQueue<ServerMessage> sendMessageQueueSplit = new ConcurrentQueue<ServerMessage>();
        public ConcurrentQueue<ServerMessage> sendMessageQueueLow = new ConcurrentQueue<ServerMessage>();
        public long lastReceiveTime;
        public bool disconnectClient;
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
        public float[] playerColor;
        //Network traffic tracking
        public long bytesQueuedOut = 0;
        public long bytesSent = 0;
        public long bytesReceived = 0;
        public long lastQueueOptimizeTime = 0;
        //Send lock
        public AutoResetEvent sendEvent = new AutoResetEvent(false);
        public object sendLock = new object();
        public object disconnectLock = new object();
    }
}

