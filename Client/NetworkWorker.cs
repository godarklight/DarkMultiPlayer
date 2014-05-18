using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using DarkMultiPlayerCommon;
using MessageStream;

namespace DarkMultiPlayer
{
    public class NetworkWorker
    {
        private const float CONNECTION_TIMEOUT = 10;
        private const float HEARTBEAT_INTERVAL = 5;
        //Read from ConnectionWindow
        public ClientState state
        {
            private set;
            get;
        }

        private static NetworkWorker singleton = new NetworkWorker();
        private TcpClient clientConnection = null;
        private float lastSendTime = 0f;
        private bool isSendingMessage = false;
        private Queue<ClientMessage> sendMessageQueueHigh = new Queue<ClientMessage>();
        private Queue<ClientMessage> sendMessageQueueSplit = new Queue<ClientMessage>();
        private Queue<ClientMessage> sendMessageQueueLow = new Queue<ClientMessage>();
        //Receive buffer
        private float lastReceiveTime = 0f;
        private bool isReceivingMessage = false;
        private int receiveMessageBytesLeft = 0;
        private ServerMessage receiveMessage = null;
        //Receive split buffer
        private bool isReceivingSplitMessage = false;
        private int receiveSplitMessageBytesLeft = 0;
        private ServerMessage receiveSplitMessage = null;
        //Used for the initial sync
        private int numberOfKerbals = 0;
        private int numberOfKerbalsReceived = 0;
        private int numberOfVessels = 0;
        private int numberOfVesselsReceived = 0;
        private object disconnectLock = new object();

        public NetworkWorker()
        {
            lock (Client.eventLock)
            {
                Client.updateEvent.Add(this.Update);
            }
        }

        public static NetworkWorker fetch
        {
            get
            {
                return singleton;
            }
        }
        //Called from main
        private void Update()
        {
            CheckDisconnection();
            SendHeartBeat();
            if (!isSendingMessage)
            {
                SendOutgoingMessages();
            }
            if (state == ClientState.CONNECTED)
            {
                DarkLog.Debug("Sending handshake!");
                state = ClientState.HANDSHAKING;
                Client.fetch.status = "Handshaking";
                SendHandshakeRequest();
            }
            if (state == ClientState.AUTHENTICATED)
            {
                NetworkWorker.fetch.SendPlayerStatus(PlayerStatusWorker.fetch.myPlayerStatus);
                DarkLog.Debug("Sending time sync!");
                state = ClientState.TIME_SYNCING;
                Client.fetch.status = "Syncing server clock";
                SendTimeSync();
            }
            if (TimeSyncer.fetch.synced && state == ClientState.TIME_SYNCING)
            {
                DarkLog.Debug("Time Synced!");
                state = ClientState.TIME_SYNCED;
            }
            if (state == ClientState.TIME_SYNCED)
            {
                DarkLog.Debug("Requesting kerbals!");
                Client.fetch.status = "Syncing kerbals";
                state = ClientState.SYNCING_KERBALS;
                SendKerbalsRequest();
            }
            if (state == ClientState.VESSELS_SYNCED)
            {
                DarkLog.Debug("Vessels Synced!");
                Client.fetch.status = "Syncing universe time";
                state = ClientState.TIME_LOCKING;
                //The subspaces are held in the wrap control messages, but the warp worker will create a new subspace if we aren't locked.
                //Process the messages so we get the subspaces, but don't enable the worker until the game is started.
                WarpWorker.fetch.ProcessWarpMessages();
                TimeSyncer.fetch.workerEnabled = true;
                ChatWorker.fetch.workerEnabled = true;
            }
            if (state == ClientState.TIME_LOCKING)
            {
                if (TimeSyncer.fetch.locked)
                {
                    DarkLog.Debug("Time Locked!");
                    DarkLog.Debug("Starting Game!");
                    Client.fetch.status = "Starting game";
                    state = ClientState.STARTING;
                    Client.fetch.StartGame();
                }
            }
            if ((state == ClientState.STARTING) && (HighLogic.LoadedScene == GameScenes.SPACECENTER))
            {
                state = ClientState.RUNNING;
                Client.fetch.status = "Running";
                Client.fetch.gameRunning = true;
                VesselWorker.fetch.workerEnabled = true;
                PlayerStatusWorker.fetch.workerEnabled = true;
                ScenarioWorker.fetch.workerEnabled = true;
                DynamicTickWorker.fetch.workerEnabled = true;
                WarpWorker.fetch.workerEnabled = true;
                CraftLibraryWorker.fetch.workerEnabled = true;
                ScreenshotWorker.fetch.workerEnabled = true;
            }
        }
        #region Connecting to server
        //Called from main
        public void ConnectToServer(string address, int port)
        {
            if (state == ClientState.DISCONNECTED)
            {
                DarkLog.Debug("Trying to connect to " + address + ", port " + port);
                Client.fetch.status = "Connecting to " + address + " port " + port;
                sendMessageQueueHigh = new Queue<ClientMessage>();
                sendMessageQueueSplit = new Queue<ClientMessage>();
                sendMessageQueueLow = new Queue<ClientMessage>();
                isSendingMessage = false;
                numberOfKerbals = 0;
                numberOfKerbalsReceived = 0;
                numberOfVessels = 0;
                numberOfVesselsReceived = 0;
                receiveSplitMessage = null;
                receiveSplitMessageBytesLeft = 0;
                isReceivingSplitMessage = false;
                IPAddress destinationAddress;
                if (!IPAddress.TryParse(address, out destinationAddress))
                {
                    try
                    {
                        IPHostEntry dnsResult = Dns.GetHostEntry(address);
                        if (dnsResult.AddressList.Length > 0)
                        {
                            foreach (IPAddress testAddress in dnsResult.AddressList)
                            {
                                if (testAddress.AddressFamily == AddressFamily.InterNetwork)
                                {
                                    destinationAddress = testAddress;
                                    break;
                                }
                            }
                            if (destinationAddress == null)
                            {
                                DarkLog.Debug("DNS does not contain a valid address entry");
                                Client.fetch.status = "DNS does not contain a valid address entry";
                                return;
                            }
                        }
                        else
                        {
                            DarkLog.Debug("Address is not a IP or DNS name");
                            Client.fetch.status = "Address is not a IP or DNS name";
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("DNS Error: " + e.Message);
                        Client.fetch.status = "DNS Error: " + e.Message;
                        return;
                    }

                }
                IPEndPoint destination = new IPEndPoint(destinationAddress, port);
                clientConnection = new TcpClient(destination.AddressFamily);
                clientConnection.NoDelay = true;
                try
                {
                    DarkLog.Debug("Connecting to " + destinationAddress + " port " + port + "...");
                    Client.fetch.status = "Connecting to " + destinationAddress + " port " + port;
                    lastSendTime = UnityEngine.Time.realtimeSinceStartup;
                    lastReceiveTime = UnityEngine.Time.realtimeSinceStartup;
                    state = ClientState.CONNECTING;
                    clientConnection.BeginConnect(destination.Address, destination.Port, new AsyncCallback(ConnectionCallback), null);
                }
                catch (Exception e)
                {
                    if (e.InnerException != null)
                    {
                        Disconnect("Connection error: " + e.Message + ", " + e.InnerException.Message);
                    }
                    else
                    {
                        Disconnect("Connection error: " + e.Message);
                    }
                }
            }
        }

        private void ConnectionCallback(IAsyncResult ar)
        {
            try
            {
                clientConnection.EndConnect(ar);
                if ((UnityEngine.Time.realtimeSinceStartup - lastSendTime) < CONNECTION_TIMEOUT)
                {
                    //Timeout didn't expire.
                    DarkLog.Debug("Connected!");
                    Client.fetch.status = "Connected";
                    state = ClientState.CONNECTED;
                    StartReceivingIncomingMessages();
                }
                else
                {
                    //The connection actually comes good, but after the timeout, so we can send the disconnect message.
                    DarkLog.Debug("Failed to connect within the timeout!");
                    SendDisconnect("Initial connection timeout");
                }
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    Disconnect("Connection error: " + e.Message + ", " + e.InnerException.Message);
                }
                else
                {
                    Disconnect("Connection error: " + e.Message);
                }
            }
        }
        #endregion
        #region Connection housekeeping
        private void CheckDisconnection()
        {
            if (state == ClientState.CONNECTING)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastReceiveTime) > CONNECTION_TIMEOUT)
                {
                    Disconnect("Failed to connect!");
                    Client.fetch.status = "Failed to connect - no reply";
                }
            }
            if (state >= ClientState.CONNECTED)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastReceiveTime) > CONNECTION_TIMEOUT)
                {
                    SendDisconnect("Connection timeout");
                }
            }
        }

        private void Disconnect(string reason)
        {
            lock (disconnectLock)
            {
                if (state != ClientState.DISCONNECTED)
                {
                    DarkLog.Debug("Disconnecting, reason: " + reason);
                    Client.fetch.status = reason;
                    Client.fetch.displayDisconnectMessage = true;
                    state = ClientState.DISCONNECTED;
                    if (clientConnection != null)
                    {
                        clientConnection.Close();
                    }
                    DarkLog.Debug("Disconnected");
                    if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                    {
                        Client.fetch.forceQuit = true;
                    }
                }
            }
        }
        #endregion
        #region Network writers/readers
        private void StartReceivingIncomingMessages()
        {
            lastReceiveTime = UnityEngine.Time.realtimeSinceStartup;
            //Allocate byte for header
            isReceivingMessage = false;
            receiveMessage = new ServerMessage();
            receiveMessage.data = new byte[8];
            receiveMessageBytesLeft = receiveMessage.data.Length;
            try
            {
                clientConnection.GetStream().BeginRead(receiveMessage.data, receiveMessage.data.Length - receiveMessageBytesLeft, receiveMessageBytesLeft, new AsyncCallback(ReceiveCallback), null);
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    Disconnect("Connection error: " + e.Message + ", " + e.InnerException.Message);
                }
                else
                {
                    Disconnect("Connection error: " + e.Message);
                }
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                receiveMessageBytesLeft -= clientConnection.GetStream().EndRead(ar);
                if (receiveMessageBytesLeft == 0)
                {
                    //We either have the header or the message data, let's do something
                    if (!isReceivingMessage)
                    {
                        //We have the header
                        using (MessageReader mr = new MessageReader(receiveMessage.data, true))
                        {
                            if (mr.GetMessageType() > (Enum.GetNames(typeof(ServerMessageType)).Length - 1))
                            {
                                //Malformed message, most likely from a non DMP-server.
                                Disconnect("Disconnected from non-DMP server");
                                //Returning from ReceiveCallback will break the receive loop and stop processing any further messages.
                                return;
                            }
                            receiveMessage.type = (ServerMessageType)mr.GetMessageType();
                            int length = mr.GetMessageLength();
                            if (length == 0)
                            {
                                //Null message, handle it.
                                receiveMessage.data = null;
                                HandleMessage(receiveMessage);
                                receiveMessage.type = 0;
                                receiveMessage.data = new byte[8];
                                receiveMessageBytesLeft = receiveMessage.data.Length;
                            }
                            else
                            {
                                if (length < Common.MAX_MESSAGE_SIZE)
                                {
                                    isReceivingMessage = true;
                                    receiveMessage.data = new byte[length];
                                    receiveMessageBytesLeft = receiveMessage.data.Length;
                                }
                                else
                                {
                                    //Malformed message, most likely from a non DMP-server.
                                    Disconnect("Disconnected from non-DMP server");
                                    //Returning from ReceiveCallback will break the receive loop and stop processing any further messages.
                                    return;
                                }
                            }
                        }
                    }
                    else
                    {
                        //We have the message data to a non-null message, handle it
                        isReceivingMessage = false;
                        using (MessageReader mr = new MessageReader(receiveMessage.data, false))
                        {
                            receiveMessage.data = mr.Read<byte[]>();
                        }
                        HandleMessage(receiveMessage);
                        receiveMessage.type = 0;
                        receiveMessage.data = new byte[8];
                        receiveMessageBytesLeft = receiveMessage.data.Length;
                    }
                }
                if (state >= ClientState.CONNECTED && state != ClientState.DISCONNECTING)
                {
                    lastReceiveTime = UnityEngine.Time.realtimeSinceStartup;
                    clientConnection.GetStream().BeginRead(receiveMessage.data, receiveMessage.data.Length - receiveMessageBytesLeft, receiveMessageBytesLeft, new AsyncCallback(ReceiveCallback), null);
                }
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    Disconnect("Connection error: " + e.Message + ", " + e.InnerException.Message);
                }
                else
                {
                    Disconnect("Connection error: " + e.Message);
                }
            }
        }

        private void SendOutgoingMessages()
        {
            if (!isSendingMessage && state >= ClientState.CONNECTED)
            {
                if (sendMessageQueueHigh.Count > 0)
                {
                    ClientMessage message = sendMessageQueueHigh.Dequeue();
                    SendNetworkMessage(message);
                    return;
                }
                if (sendMessageQueueSplit.Count > 0)
                {
                    ClientMessage message = sendMessageQueueSplit.Dequeue();
                    SendNetworkMessage(message);
                    return;
                }
                if (sendMessageQueueLow.Count > 0)
                {
                    ClientMessage message = sendMessageQueueLow.Dequeue();
                    //Splits large messages to higher priority messages can get into the queue faster
                    SplitAndRewriteMessage(ref message);
                    SendNetworkMessage(message);
                    return;
                }
            }
        }

        private void SplitAndRewriteMessage(ref ClientMessage message)
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
                ClientMessage newSplitMessage = new ClientMessage();
                newSplitMessage.type = ClientMessageType.SPLIT_MESSAGE;
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
                    sendMessageQueueSplit.Enqueue(newSplitMessage);
                }

                while (splitBytesLeft > 0)
                {
                    ClientMessage currentSplitMessage = new ClientMessage();
                    currentSplitMessage.type = ClientMessageType.SPLIT_MESSAGE;
                    currentSplitMessage.data = new byte[Math.Min(splitBytesLeft, Common.SPLIT_MESSAGE_LENGTH)];
                    Array.Copy(message.data, message.data.Length - splitBytesLeft, currentSplitMessage.data, 0, currentSplitMessage.data.Length);
                    splitBytesLeft -= currentSplitMessage.data.Length;
                    sendMessageQueueSplit.Enqueue(currentSplitMessage);
                }
                message = sendMessageQueueSplit.Dequeue();
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                clientConnection.GetStream().EndWrite(ar);
                isSendingMessage = false;
                SendOutgoingMessages();
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    Disconnect("Connection error: " + e.Message + ", " + e.InnerException.Message);
                }
                else
                {
                    Disconnect("Connection error: " + e.Message);
                }
            }
        }

        private void SendNetworkMessage(ClientMessage message)
        {
            byte[] messageBytes;
            using (MessageWriter mw = new MessageWriter((int)message.type))
            {
                if (message.data != null)
                {
                    mw.Write<byte[]>(message.data);
                }
                messageBytes = mw.GetMessageBytes();
            }
            isSendingMessage = true;
            lastSendTime = UnityEngine.Time.realtimeSinceStartup;
            try
            {
                clientConnection.GetStream().BeginWrite(messageBytes, 0, messageBytes.Length, new AsyncCallback(SendCallback), null);
                if (message.type == ClientMessageType.CONNECTION_END)
                {
                    using (MessageReader mr = new MessageReader(message.data, false))
                    {
                        Disconnect("Connection ended: " + mr.Read<string>());
                    }
                }
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    Disconnect("Connection error: " + e.Message + ", " + e.InnerException.Message);
                }
                else
                {
                    Disconnect("Connection error: " + e.Message);
                }
            }
        }
        #endregion
        #region Message Handling
        private void HandleMessage(ServerMessage message)
        {
            try
            {
                switch (message.type)
                {
                    case ServerMessageType.HEARTBEAT:
                        break;
                    case ServerMessageType.HANDSHAKE_REPLY:
                        HandleHandshakeReply(message.data);
                        break;
                    case ServerMessageType.CHAT_MESSAGE:
                        HandleChatMessage(message.data);
                        break;
                    case ServerMessageType.SERVER_SETTINGS:
                        HandleServerSettings(message.data);
                        break;
                    case ServerMessageType.PLAYER_STATUS:
                        HandlePlayerStatus(message.data);
                        break;
                    case ServerMessageType.PLAYER_DISCONNECT:
                        HandlePlayerDisconnect(message.data);
                        break;
                    case ServerMessageType.SCENARIO_DATA:
                        HandleScenarioModuleData(message.data);
                        break;
                    case ServerMessageType.KERBAL_REPLY:
                        HandleKerbalReply(message.data);
                        break;
                    case ServerMessageType.KERBAL_COMPLETE:
                        HandleKerbalComplete();
                        break;
                    case ServerMessageType.VESSEL_LIST:
                        HandleVesselList(message.data);
                        break;
                    case ServerMessageType.VESSEL_PROTO:
                        HandleVesselProto(message.data);
                        break;
                    case ServerMessageType.VESSEL_UPDATE:
                        HandleVesselUpdate(message.data);
                        break;
                    case ServerMessageType.VESSEL_COMPLETE:
                        HandleVesselComplete();
                        break;
                    case ServerMessageType.VESSEL_REMOVE:
                        HandleVesselRemove(message.data);
                        break;
                    case ServerMessageType.CRAFT_LIBRARY:
                        HandleCraftLibrary(message.data);
                        break;
                    case ServerMessageType.SCREENSHOT_LIBRARY:
                        HandleScreenshotLibrary(message.data);
                        break;
                    case ServerMessageType.SET_SUBSPACE:
                        HandleSetSubspace(message.data);
                        break;
                    case ServerMessageType.SET_ACTIVE_VESSEL:
                        HandleSetActiveVessel(message.data);
                        break;
                    case ServerMessageType.SYNC_TIME_REPLY:
                        HandleSyncTimeReply(message.data);
                        break;
                    case ServerMessageType.WARP_CONTROL:
                        HandleWarpControl(message.data);
                        break;
                    case ServerMessageType.SPLIT_MESSAGE:
                        HandleSplitMessage(message.data);
                        break;
                    case ServerMessageType.CONNECTION_END:
                        HandleConnectionEnd(message.data);
                        break;
                    default:
                        DarkLog.Debug("Unhandled message type " + message.type);
                        break;
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling message type " + message.type + ", exception: " + e);
                SendDisconnect("Error handling " + message.type + " message");
            }
        }

        private void HandleHandshakeReply(byte[] messageData)
        {

            int reply = 0;
            string modFileData = "";
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    reply = mr.Read<int>();
                    ModWorker.fetch.modControl = mr.Read<bool>();
                    if (ModWorker.fetch.modControl)
                    {
                        modFileData = mr.Read<string>();
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling HANDSHAKE_REPLY message, exception: " + e);
                reply = 99;
            }
            switch (reply)
            {
                case 0:
                    {
                        if (ModWorker.fetch.ParseModFile(modFileData))
                        {
                            DarkLog.Debug("Handshake successful");
                            state = ClientState.AUTHENTICATED;
                        }
                        else
                        {
                            DarkLog.Debug("Failed to pass mod validation");
                            SendDisconnect("Failed mod validation");
                        }
                    }
                    break;
                default:
                    DarkLog.Debug("Handshake failed, reason " + reply);
                    //Server disconnects us.
                    break;
            }
        }

        private void HandleChatMessage(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                ChatMessageType chatMessageType = (ChatMessageType)mr.Read<int>();

                switch (chatMessageType)
                {
                    case ChatMessageType.LIST:
                        {
                            string[] playerList = mr.Read<string[]>();
                            foreach (string playerName in playerList)
                            {
                                string[] channelList = mr.Read<string[]>();
                                foreach (string channelName in channelList)
                                {
                                    ChatWorker.fetch.QueueChatJoin(playerName, channelName);
                                }
                            }
                        }
                        break;
                    case ChatMessageType.JOIN:
                        {
                            string playerName = mr.Read<string>();
                            string channelName = mr.Read<string>();
                            ChatWorker.fetch.QueueChatJoin(playerName, channelName);
                        }
                        break;
                    case ChatMessageType.LEAVE:
                        {
                            string playerName = mr.Read<string>();
                            string channelName = mr.Read<string>();
                            ChatWorker.fetch.QueueChatLeave(playerName, channelName);
                        }
                        break;
                    case ChatMessageType.CHANNEL_MESSAGE:
                        {
                            string playerName = mr.Read<string>();
                            string channelName = mr.Read<string>();
                            string channelMessage = mr.Read<string>();
                            ChatWorker.fetch.QueueChannelMessage(playerName, channelName, channelMessage);
                        }
                        break;
                    case ChatMessageType.PRIVATE_MESSAGE:
                        {
                            string fromPlayer = mr.Read<string>();
                            string toPlayer = mr.Read<string>();
                            string privateMessage = mr.Read<string>();
                            if (toPlayer == Settings.fetch.playerName || fromPlayer == Settings.fetch.playerName)
                            {
                                ChatWorker.fetch.QueuePrivateMessage(fromPlayer, toPlayer, privateMessage);
                            }
                        }
                        break;
                }
            }
        }

        private void HandleServerSettings(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                WarpWorker.fetch.warpMode = (WarpMode)mr.Read<int>();
                Client.fetch.gameMode = (GameMode)mr.Read<int>();
                numberOfKerbals = mr.Read<int>();
                numberOfVessels = mr.Read<int>();
                ScreenshotWorker.fetch.screenshotHeight = mr.Read<int>();
            }
        }

        private void HandlePlayerStatus(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string playerName = mr.Read<string>();
                string vesselText = mr.Read<string>();
                string statusText = mr.Read<string>();
                PlayerStatus newStatus = new PlayerStatus();
                newStatus.playerName = playerName;
                newStatus.vesselText = vesselText;
                newStatus.statusText = statusText;
                PlayerStatusWorker.fetch.AddPlayerStatus(newStatus);
            }
        }

        private void HandlePlayerDisconnect(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string playerName = mr.Read<string>();
                WarpWorker.fetch.RemovePlayer(playerName);
                PlayerStatusWorker.fetch.RemovePlayerStatus(playerName);
                VesselWorker.fetch.SetNotInUse(playerName);
                ChatWorker.fetch.QueueRemovePlayer(playerName);
            }
        }

        private void HandleSyncTimeReply(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                long clientSend = mr.Read<long>();
                long serverReceive = mr.Read<long>();
                long serverSend = mr.Read<long>();
                TimeSyncer.fetch.HandleSyncTime(clientSend, serverReceive, serverSend);
            }
        }

        private void HandleScenarioModuleData(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string[] scenarioName = mr.Read<string[]>();
                string[] scenarioData = mr.Read<string[]>();
                for (int i = 0; i < scenarioName.Length; i++)
                {
                    ScenarioWorker.fetch.QueueScenarioData(scenarioName[i], scenarioData[i]);
                }
            }
        }

        private void HandleKerbalReply(byte[] messageData)
        {
            numberOfKerbalsReceived++;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int subspaceID = mr.Read<int>();
                double planetTime = mr.Read<double>();
                int kerbalID = mr.Read<int>();
                byte[] kerbalData = mr.Read<byte[]>();
                string tempFile = Path.GetTempFileName();
                File.WriteAllBytes(tempFile, kerbalData);
                ConfigNode vesselNode = ConfigNode.Load(tempFile);
                File.Delete(tempFile);
                if (vesselNode != null)
                {
                    VesselWorker.fetch.QueueKerbal(subspaceID, planetTime, kerbalID, vesselNode);
                }
                else
                {
                    DarkLog.Debug("Failed to load kerbal!");
                }
            }
            if (state == ClientState.SYNCING_KERBALS)
            {
                if (numberOfKerbals != 0)
                {
                    Client.fetch.status = "Syncing kerbals " + numberOfKerbalsReceived + "/" + numberOfKerbals + " (" + (int)((numberOfKerbalsReceived / (float)numberOfKerbals) * 100) + "%)";
                }
            }
        }

        private void HandleKerbalComplete()
        {
            state = ClientState.KERBALS_SYNCED;
            DarkLog.Debug("Kerbals Synced!");
            Client.fetch.status = "Kerbals synced";
        }

        private void HandleVesselList(byte[] messageData)
        {
            state = ClientState.SYNCING_VESSELS;
            Client.fetch.status = "Syncing vessels";
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                List<string> serverVessels = new List<string>(mr.Read<string[]>());
                List<string> cacheObjects = new List<string>(UniverseSyncCache.fetch.GetCachedObjects());
                List<string> requestedObjects = new List<string>();
                foreach (string serverVessel in serverVessels)
                {
                    if (!cacheObjects.Contains(serverVessel))
                    {
                        requestedObjects.Add(serverVessel);
                    }
                    else
                    {
                        numberOfVesselsReceived++;
                        ConfigNode vesselNode = ConfigNode.Load(Path.Combine(UniverseSyncCache.fetch.cacheDirectory, serverVessel + ".txt"));
                        VesselWorker.fetch.QueueVesselProto(0, 0, vesselNode);
                    }
                }
                if (numberOfVessels != 0)
                {
                    Client.fetch.status = "Syncing vessels " + numberOfVesselsReceived + "/" + numberOfVessels + " (" + (int)((numberOfVesselsReceived / (float)numberOfVessels) * 100) + "%)";
                }
                SendVesselsRequest(requestedObjects.ToArray());
            }
        }

        private void HandleVesselProto(byte[] messageData)
        {
            numberOfVesselsReceived++;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int subspaceID = mr.Read<int>();
                double planetTime = mr.Read<double>();
                bool isDockingUpdate = mr.Read<bool>();
                if (isDockingUpdate)
                {
                    DarkLog.Debug("Got a docking update!");
                }
                byte[] vesselData = mr.Read<byte[]>();
                UniverseSyncCache.fetch.SaveToCache(vesselData);
                string tempFile = Path.GetTempFileName();
                File.WriteAllBytes(tempFile, vesselData);
                ConfigNode vesselNode = ConfigNode.Load(tempFile);
                File.Delete(tempFile);
                if (vesselNode != null)
                {
                    VesselWorker.fetch.QueueVesselProto(subspaceID, planetTime, vesselNode);
                }
                else
                {
                    DarkLog.Debug("Failed to load vessel!");
                }
            }
            if (state == ClientState.SYNCING_VESSELS)
            {
                if (numberOfVessels != 0)
                {
                    Client.fetch.status = "Syncing vessels " + numberOfVesselsReceived + "/" + numberOfVessels + " (" + (int)((numberOfVesselsReceived / (float)numberOfVessels) * 100) + "%)";
                }
            }
        }

        private void HandleVesselUpdate(byte[] messageData)
        {
            VesselUpdate update = new VesselUpdate();
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int subspaceID = mr.Read<int>();
                update.planetTime = mr.Read<double>();
                update.vesselID = mr.Read<string>();
                update.bodyName = mr.Read<string>();
                //update.rotation = mr.Read<float[]>();
                update.vesselForward = mr.Read<float[]>();
                update.vesselUp = mr.Read<float[]>();
                update.angularVelocity = mr.Read<float[]>();
                update.flightState = new FlightCtrlState();
                byte[] flightData = mr.Read<byte[]>();
                using (MemoryStream ms = new MemoryStream(flightData))
                {
                    ConfigNode flightNode;
                    BinaryFormatter bf = new BinaryFormatter();
                    flightNode = (ConfigNode)bf.Deserialize(ms);
                    update.flightState.Load(flightNode);
                }
                update.actiongroupControls = mr.Read<bool[]>();
                update.isSurfaceUpdate = mr.Read<bool>();
                if (update.isSurfaceUpdate)
                {
                    update.position = mr.Read<double[]>();
                    update.velocity = mr.Read<double[]>();
                }
                else
                {
                    update.orbit = mr.Read<double[]>();
                }
                VesselWorker.fetch.QueueVesselUpdate(subspaceID, update);
            }
        }

        private void HandleSetActiveVessel(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string player = mr.Read<string>();
                string vesselID = mr.Read<string>();
                VesselWorker.fetch.QueueActiveVessel(player, vesselID);
            }
        }

        private void HandleVesselComplete()
        {
            state = ClientState.VESSELS_SYNCED;
        }

        private void HandleVesselRemove(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int subspaceID = mr.Read<int>();
                double planetTime = mr.Read<double>();
                string vesselID = mr.Read<string>();
                bool isDockingUpdate = mr.Read<bool>();
                string dockingPlayer = null;
                if (isDockingUpdate)
                {
                    DarkLog.Debug("Got a docking update!");
                    dockingPlayer = mr.Read<string>();
                }
                VesselWorker.fetch.QueueVesselRemove(subspaceID, planetTime, vesselID, isDockingUpdate, dockingPlayer);
            }
        }

        private void HandleCraftLibrary(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                CraftMessageType messageType = (CraftMessageType)mr.Read<int>();
                switch (messageType)
                {
                    case CraftMessageType.LIST:
                        {
                            string[] playerList = mr.Read<string[]>();
                            foreach (string player in playerList)
                            {
                                bool vabExists = mr.Read<bool>();
                                bool sphExists = mr.Read<bool>();
                                bool subassemblyExists = mr.Read<bool>();
                                DarkLog.Debug("Player: " + player + ", VAB: " + vabExists + ", SPH: " + sphExists + ", SUBASSEMBLY" + subassemblyExists);
                                if (vabExists)
                                {
                                    string[] vabCrafts = mr.Read<string[]>();
                                    foreach (string vabCraft in vabCrafts)
                                    {
                                        CraftAddEntry cae = new CraftAddEntry();
                                        cae.playerName = player;
                                        cae.craftType = CraftType.VAB;
                                        cae.craftName = vabCraft;
                                        CraftLibraryWorker.fetch.QueueCraftAdd(cae);
                                    }
                                }
                                if (sphExists)
                                {
                                    string[] sphCrafts = mr.Read<string[]>();
                                    foreach (string sphCraft in sphCrafts)
                                    {
                                        CraftAddEntry cae = new CraftAddEntry();
                                        cae.playerName = player;
                                        cae.craftType = CraftType.SPH;
                                        cae.craftName = sphCraft;
                                        CraftLibraryWorker.fetch.QueueCraftAdd(cae);
                                    }
                                }
                                if (subassemblyExists)
                                {
                                    string[] subassemblyCrafts = mr.Read<string[]>();
                                    foreach (string subassemblyCraft in subassemblyCrafts)
                                    {
                                        CraftAddEntry cae = new CraftAddEntry();
                                        cae.playerName = player;
                                        cae.craftType = CraftType.SUBASSEMBLY;
                                        cae.craftName = subassemblyCraft;
                                        CraftLibraryWorker.fetch.QueueCraftAdd(cae);
                                    }
                                }
                            }
                        }
                        break;
                    case CraftMessageType.ADD_FILE:
                        {
                            CraftAddEntry cae = new CraftAddEntry();
                            cae.playerName = mr.Read<string>();
                            cae.craftType = (CraftType)mr.Read<int>();
                            cae.craftName = mr.Read<string>();
                            CraftLibraryWorker.fetch.QueueCraftAdd(cae);
                        }
                        break;
                    case CraftMessageType.DELETE_FILE:
                        {
                            CraftDeleteEntry cde = new CraftDeleteEntry();
                            cde.playerName = mr.Read<string>();
                            cde.craftType = (CraftType)mr.Read<int>();
                            cde.craftName = mr.Read<string>();
                            CraftLibraryWorker.fetch.QueueCraftDelete(cde);
                        }
                        break;
                    case CraftMessageType.RESPOND_FILE:
                        {
                            CraftResponseEntry cre = new CraftResponseEntry();
                            cre.playerName = mr.Read<string>();
                            cre.craftType = (CraftType)mr.Read<int>();
                            cre.craftName = mr.Read<string>();
                            bool hasCraft = mr.Read<bool>();
                            if (hasCraft)
                            {
                                cre.craftData = mr.Read<byte[]>();
                                CraftLibraryWorker.fetch.QueueCraftResponse(cre);
                            }
                            else
                            {
                                ScreenMessages.PostScreenMessage("Craft " + cre.craftName + " from " + cre.playerName + " not available", 5f, ScreenMessageStyle.UPPER_CENTER);
                            }
                        }
                        break;
                }
            }
        }

        private void HandleScreenshotLibrary(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                ScreenshotMessageType messageType = (ScreenshotMessageType)mr.Read<int>();
                switch (messageType)
                {
                    case ScreenshotMessageType.LIST:
                        {
                            DarkLog.Debug("TODO: Handle screenshot message list");
                        }
                        break;
                    case ScreenshotMessageType.SCREENSHOT:
                        {
                            string fromPlayer = mr.Read<string>();
                            byte[] screenshotData = mr.Read<byte[]>();
                            ScreenshotWorker.fetch.QueueNewScreenshot(fromPlayer, screenshotData);
                        }
                        break;
                    case ScreenshotMessageType.WATCH:
                        {
                            string fromPlayer = mr.Read<string>();
                            string watchPlayer = mr.Read<string>();
                            ScreenshotWorker.fetch.QueueNewScreenshotWatch(fromPlayer, watchPlayer);
                        }
                        break;

                }
            }
        }

        private void HandleSetSubspace(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int subspaceID = mr.Read<int>();
                TimeSyncer.fetch.LockSubspace(subspaceID);
            }
        }

        private void HandleWarpControl(byte[] messageData)
        {
            WarpWorker.fetch.QueueWarpMessage(messageData);
        }

        private void HandleSplitMessage(byte[] messageData)
        {
            if (!isReceivingSplitMessage)
            {
                //New split message
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    receiveSplitMessage = new ServerMessage();
                    receiveSplitMessage.type = (ServerMessageType)mr.Read<int>();
                    receiveSplitMessage.data = new byte[mr.Read<int>()];
                    receiveSplitMessageBytesLeft = receiveSplitMessage.data.Length;
                    byte[] firstSplitData = mr.Read<byte[]>();
                    firstSplitData.CopyTo(receiveSplitMessage.data, 0);
                    receiveSplitMessageBytesLeft -= firstSplitData.Length;
                }
                isReceivingSplitMessage = true;
            }
            else
            {
                //Continued split message
                messageData.CopyTo(receiveSplitMessage.data, receiveSplitMessage.data.Length - receiveSplitMessageBytesLeft);
                receiveSplitMessageBytesLeft -= messageData.Length;
            }
            if (receiveSplitMessageBytesLeft == 0)
            {
                HandleMessage(receiveSplitMessage);
                receiveSplitMessage = null;
                isReceivingSplitMessage = false;
            }
        }

        private void HandleConnectionEnd(byte[] messageData)
        {
            string reason = "";
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                reason = mr.Read<string>();
            }
            Disconnect("Server closed connection: " + reason);
        }
        #endregion
        #region Message Sending
        private void SendHeartBeat()
        {
            if (state >= ClientState.CONNECTED && sendMessageQueueHigh.Count == 0)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastSendTime) > HEARTBEAT_INTERVAL)
                {
                    lastSendTime = UnityEngine.Time.realtimeSinceStartup;
                    ClientMessage newMessage = new ClientMessage();
                    newMessage.type = ClientMessageType.HEARTBEAT;
                    sendMessageQueueHigh.Enqueue(newMessage);
                }
            }
        }

        private void SendHandshakeRequest()
        {
            byte[] messageBytes;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(Common.PROTOCOL_VERSION);
                mw.Write<string>(Settings.fetch.playerName);
                mw.Write<string>(Settings.fetch.playerGuid.ToString());
                messageBytes = mw.GetMessageBytes();
            }
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.HANDSHAKE_REQUEST;
            newMessage.data = messageBytes;
            sendMessageQueueHigh.Enqueue(newMessage);
        }
        //Called from ChatWindow
        public void SendChatMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.CHAT_MESSAGE;
            newMessage.data = messageData;
            sendMessageQueueHigh.Enqueue(newMessage);
        }
        //Called from PlayerStatusWorker
        public void SendPlayerStatus(PlayerStatus playerStatus)
        {
            byte[] messageBytes;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(playerStatus.playerName);
                mw.Write<string>(playerStatus.vesselText);
                mw.Write<string>(playerStatus.statusText);
                messageBytes = mw.GetMessageBytes();
            }
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.PLAYER_STATUS;
            newMessage.data = messageBytes;
            sendMessageQueueHigh.Enqueue(newMessage);
        }
        //Called from timeSyncer
        public void SendTimeSync()
        {
            byte[] messageBytes;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<long>(DateTime.UtcNow.Ticks);
                messageBytes = mw.GetMessageBytes();
            }
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SYNC_TIME_REQUEST;
            newMessage.data = messageBytes;
            sendMessageQueueHigh.Enqueue(newMessage);
        }

        private void SendKerbalsRequest()
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.KERBALS_REQUEST;
            sendMessageQueueHigh.Enqueue(newMessage);
        }

        private void SendVesselsRequest(string[] requestList)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSELS_REQUEST;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string[]>(requestList);
                newMessage.data = mw.GetMessageBytes();
            }
            sendMessageQueueHigh.Enqueue(newMessage);
        }
        //Called from vesselWorker
        public void SendVesselProtoMessage(ProtoVessel vessel, bool isDockingUpdate)
        {
            ConfigNode currentNode = new ConfigNode();
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSEL_PROTO;
            vessel.Save(currentNode);
            string tempFile = Path.GetTempFileName();
            currentNode.Save(tempFile);
            using (StreamReader sr = new StreamReader(tempFile))
            {
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>(TimeSyncer.fetch.currentSubspace);
                    mw.Write<double>(Planetarium.GetUniversalTime());
                    mw.Write<string>(vessel.vesselID.ToString());
                    mw.Write<bool>(isDockingUpdate);
                    mw.Write<byte[]>(File.ReadAllBytes(tempFile));
                    newMessage.data = mw.GetMessageBytes();
                }
            }
            File.Delete(tempFile);
            DarkLog.Debug("Sending vessel " + vessel.vesselID + ", name " + vessel.vesselName + ", type: " + vessel.vesselType + ", size: " + newMessage.data.Length);
            sendMessageQueueLow.Enqueue(newMessage);
        }
        //Called from vesselWorker
        public void SendVesselUpdate(VesselUpdate update)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSEL_UPDATE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(TimeSyncer.fetch.currentSubspace);
                mw.Write<double>(update.planetTime);
                mw.Write<string>(update.vesselID);
                mw.Write<string>(update.bodyName);
                //mw.Write<float[]>(update.rotation);
                mw.Write<float[]>(update.vesselForward);
                mw.Write<float[]>(update.vesselUp);
                mw.Write<float[]>(update.angularVelocity);
                using (MemoryStream ms = new MemoryStream())
                {
                    ConfigNode flightNode = new ConfigNode();
                    update.flightState.Save(flightNode);
                    BinaryFormatter bf = new BinaryFormatter();
                    bf.Serialize(ms, flightNode);
                    mw.Write<byte[]>(ms.ToArray());
                }
                mw.Write<bool[]>(update.actiongroupControls);
                mw.Write<bool>(update.isSurfaceUpdate);
                if (update.isSurfaceUpdate)
                {
                    mw.Write<double[]>(update.position);
                    mw.Write<double[]>(update.velocity);
                }
                else
                {
                    mw.Write<double[]>(update.orbit);
                }
                newMessage.data = mw.GetMessageBytes();
            }
            sendMessageQueueLow.Enqueue(newMessage);
        }
        //Called from vesselWorker
        public void SendVesselRemove(string vesselID, bool isDockingUpdate)
        {
            DarkLog.Debug("Removing " + vesselID + " from the server");
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSEL_REMOVE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(TimeSyncer.fetch.currentSubspace);
                mw.Write<double>(Planetarium.GetUniversalTime());
                mw.Write<string>(vesselID);
                mw.Write<bool>(isDockingUpdate);
                if (isDockingUpdate)
                {
                    mw.Write<string>(Settings.fetch.playerName);
                }
                newMessage.data = mw.GetMessageBytes();
            }
            sendMessageQueueLow.Enqueue(newMessage);
        }
        //Called fro craftLibraryWorker
        public void SendCraftLibraryMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.CRAFT_LIBRARY;
            newMessage.data = messageData;
            sendMessageQueueLow.Enqueue(newMessage);
        }
        //Called from ScreenshotWorker
        public void SendScreenshotMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SCREENSHOT_LIBRARY;
            newMessage.data = messageData;
            sendMessageQueueLow.Enqueue(newMessage);
        }
        //Called from vesselWorker
        public void SendActiveVessel(string activeVessel)
        {
            if (activeVessel != "")
            {
                DarkLog.Debug("Sending " + activeVessel + " as my active vessel");
            }
            else
            {
                DarkLog.Debug("Sending vessel release");
            }
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SEND_ACTIVE_VESSEL;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(Settings.fetch.playerName);
                mw.Write<string>(activeVessel);
                newMessage.data = mw.GetMessageBytes();
            }
            sendMessageQueueHigh.Enqueue(newMessage);
        }
        //Called from vesselWorker
        public void SendScenarioModuleData(string[] scenarioNames, string[] scenarioData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SCENARIO_DATA;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string[]>(scenarioNames);
                mw.Write<string[]>(scenarioData);
                newMessage.data = mw.GetMessageBytes();
            }
            DarkLog.Debug("Sending " + scenarioNames.Length + " scenario modules");
            sendMessageQueueLow.Enqueue(newMessage);
        }
        //Called from vesselWorker
        public void SendKerbalProtoMessage(int kerbalID, ProtoCrewMember kerbal)
        {
            ConfigNode currentNode = new ConfigNode();
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.KERBAL_PROTO;
            kerbal.Save(currentNode);
            string tempFile = Path.GetTempFileName();
            currentNode.Save(tempFile);
            using (StreamReader sr = new StreamReader(tempFile))
            {
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>(TimeSyncer.fetch.currentSubspace);
                    mw.Write<double>(Planetarium.GetUniversalTime());
                    mw.Write<int>(kerbalID);
                    mw.Write<byte[]>(File.ReadAllBytes(tempFile));
                    newMessage.data = mw.GetMessageBytes();
                }
            }
            File.Delete(tempFile);
            DarkLog.Debug("Sending kerbal " + kerbal.name + ", size: " + newMessage.data.Length);
            sendMessageQueueLow.Enqueue(newMessage);
        }
        //Called fro warpWorker
        public void SendWarpMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.WARP_CONTROL;
            newMessage.data = messageData;
            sendMessageQueueLow.Enqueue(newMessage);
        }
        //Called from main
        public void SendDisconnect(string disconnectReason = "Unknown")
        {
            if (state != ClientState.DISCONNECTING && state >= ClientState.CONNECTED)
            {
                DarkLog.Debug("Sending disconnect message, reason: " + disconnectReason);
                Client.fetch.status = "Disconnected: " + disconnectReason;
                state = ClientState.DISCONNECTING;
                byte[] messageBytes;
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<string>(disconnectReason);
                    messageBytes = mw.GetMessageBytes();
                }
                ClientMessage newMessage = new ClientMessage();
                newMessage.type = ClientMessageType.CONNECTION_END;
                newMessage.data = messageBytes;
                sendMessageQueueHigh.Enqueue(newMessage);
            }
        }

        public int GetStatistics(string statType)
        {
            switch (statType)
            {
                case "HighPriorityQueueLength":
                    return sendMessageQueueHigh.Count;
                case "SplitPriorityQueueLength":
                    return sendMessageQueueSplit.Count;
                case "LowPriorityQueueLength":
                    return sendMessageQueueLow.Count;
                case "LastReceiveTime":
                    return (int)((UnityEngine.Time.realtimeSinceStartup - lastReceiveTime) * 1000);
                case "LastSendTime":
                    return (int)((UnityEngine.Time.realtimeSinceStartup - lastSendTime) * 1000);
            }
            return 0;
        }
        #endregion
    }
}

