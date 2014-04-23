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
        private ClientState state;
        private Client parent;
        private TcpClient clientConnection;
        private float lastSendTime;
        private bool isSendingMessage;
        private Queue<ClientMessage> sendMessageQueueHigh;
        private Queue<ClientMessage> sendMessageQueueSplit;
        private Queue<ClientMessage> sendMessageQueueLow;
        private float lastReceiveTime;
        private bool isReceivingMessage;
        private int receiveMessageBytesLeft;
        private ServerMessage receiveMessage;
        //Used for the initial sync
        private int numberOfKerbals;
        private int numberOfKerbalsReceived;
        private int numberOfVessels;
        private int numberOfVesselsReceived;

        public NetworkWorker(Client parent)
        {
            this.parent = parent;
            if (this.parent != null)
            {
                //Shutup compiler
            }
        }
        //Called from main
        public void Update()
        {
            CheckDisconnection();
            SendHeartBeat();
            SendOutgoingMessages();
            if (state == ClientState.CONNECTED)
            {
                DarkLog.Debug("Sending handshake!");
                state = ClientState.HANDSHAKING;
                parent.status = "Handshaking";
                SendHandshakeRequest();
            }
            if (state == ClientState.AUTHENTICATED)
            {
                DarkLog.Debug("Sending time sync!");
                state = ClientState.TIME_SYNCING;
                parent.status = "Syncing server clock";
                SendTimeSync();
            }
            if (parent.timeSyncer.synced && state == ClientState.TIME_SYNCING)
            {
                DarkLog.Debug("Time Synced!");
                state = ClientState.TIME_SYNCED;
            }
            if (state == ClientState.TIME_SYNCED)
            {
                DarkLog.Debug("Requesting kerbals!");
                parent.status = "Syncing kerbals";
                state = ClientState.SYNCING_KERBALS;
                SendKerbalsRequest();
            }
            if (state == ClientState.KERBALS_SYNCED)
            {
                DarkLog.Debug("Kerbals Synced!");
                DarkLog.Debug("Requesting vessels!");
                parent.status = "Syncing vessels";
                state = ClientState.SYNCING_VESSELS;
                SendVesselsRequest();
            }
            if (state == ClientState.VESSELS_SYNCED)
            {
                DarkLog.Debug("Vessels Synced!");
                parent.status = "Syncing universe time";
                state = ClientState.TIME_LOCKING;
                parent.timeSyncer.enabled = true;
                parent.warpWorker.enabled = true;
            }
            if (state == ClientState.TIME_LOCKING)
            {
                if (parent.timeSyncer.locked)
                {
                    DarkLog.Debug("Time Locked!");
                    DarkLog.Debug("Starting Game!");
                    parent.status = "Starting game";
                    state = ClientState.STARTING;
                    parent.StartGame();
                }
            }
            if ((state == ClientState.STARTING) && (HighLogic.LoadedScene == GameScenes.SPACECENTER))
            {
                state = ClientState.RUNNING;
                parent.status = "Running";
                parent.gameRunning = true;
                parent.vesselWorker.enabled = true;
                parent.playerStatusWorker.enabled = true;
            }
        }
        #region Connecting to server
        //Called from main
        public void ConnectToServer(string address, int port)
        {
            if (state == ClientState.DISCONNECTED)
            {
                DarkLog.Debug("Trying to connect to " + address + ", port " + port);
                parent.status = "Connecting to " + address + " port " + port;
                sendMessageQueueHigh = new Queue<ClientMessage>();
                sendMessageQueueSplit = new Queue<ClientMessage>();
                sendMessageQueueLow = new Queue<ClientMessage>();
                isSendingMessage = false;
                numberOfKerbals = 0;
                numberOfKerbalsReceived = 0;
                numberOfVessels = 0;
                numberOfVesselsReceived = 0;
                IPAddress destinationAddress;
                if (!IPAddress.TryParse(address, out destinationAddress))
                {
                    IPHostEntry dnsResult = Dns.GetHostEntry(address);
                    if (dnsResult.AddressList.Length > 0)
                    {
                        destinationAddress = dnsResult.AddressList[0];
                    }
                    else
                    {
                        DarkLog.Debug("Address is not a IP or DNS name");
                        parent.status = "Address is not a IP or DNS name";
                        return;
                    }

                }
                IPEndPoint destination = new IPEndPoint(destinationAddress, port);
                clientConnection = new TcpClient(destination.AddressFamily);
                clientConnection.NoDelay = true;
                try
                {
                    DarkLog.Debug("Connecting to " + destinationAddress + " port " + port + "...");
                    parent.status = "Connecting to " + destinationAddress + " port " + port;
                    lastSendTime = UnityEngine.Time.realtimeSinceStartup;
                    lastReceiveTime = UnityEngine.Time.realtimeSinceStartup;
                    state = ClientState.CONNECTING;
                    clientConnection.BeginConnect(destination.Address, destination.Port, new AsyncCallback(ConnectionCallback), null);
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Connection error: " + e);
                    parent.status = "Connection error: " + e.Message;
                    Disconnect();
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
                    parent.status = "Connected";
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
                DarkLog.Debug("Connection error: " + e);
                Disconnect();
                parent.status = "Connection error: " + e.Message;
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
                    DarkLog.Debug("Failed to connect!");
                    Disconnect();
                    parent.status = "Failed to connect - no reply";
                }
            }
            if (state >= ClientState.CONNECTED)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastReceiveTime) > CONNECTION_TIMEOUT)
                {
                    DarkLog.Debug("Connection lost!");
                    SendDisconnect("Connection timeout");
                }
            }
        }

        private void Disconnect()
        {
            if (state != ClientState.DISCONNECTED)
            {
                DarkLog.Debug("Disconnecting...");
                if (parent.status == "Running")
                {
                    parent.status = "Disconnected";
                }
                parent.displayDisconnectMessage = true;
                state = ClientState.DISCONNECTED;
                if (clientConnection != null)
                {
                    clientConnection.Close();
                }
                clientConnection = null;
                DarkLog.Debug("Disconnected");
                if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                {
                    parent.forceQuit = true;
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
                DarkLog.Debug("Connection error: " + e.Message);
                Disconnect();
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
                                Disconnect();
                                parent.status = "Disconnected from non-DMP server";
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
                                    Disconnect();
                                    parent.status = "Disconnected from non-DMP server";
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
                DarkLog.Debug("Connection error: " + e.Message);
                Disconnect();
                parent.status = "Connection error: " + e.Message;
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
                    SendNetworkMessage(message);
                    return;
                }
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                clientConnection.GetStream().EndWrite(ar);
                isSendingMessage = false;
            }
            catch (Exception e)
            {
                DarkLog.Debug("Connection error: " + e.Message);
                Disconnect();
                parent.status = "Connection error: " + e.Message;
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
                    Disconnect();
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Connection error: " + e.Message);
                Disconnect();
                parent.status = "Connection error: " + e.Message;
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
                        HandleHanshakeReply(message.data);
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
                    case ServerMessageType.KERBAL_REPLY:
                        HandleKerbalReply(message.data);
                        break;
                    case ServerMessageType.KERBAL_COMPLETE:
                        HandleKerbalComplete();
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
            }
        }

        private void HandleHanshakeReply(byte[] messageData)
        {

            int reply = 0;
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    reply = mr.Read<int>();
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
                    DarkLog.Debug("Handshake successful");
                    state = ClientState.AUTHENTICATED;
                    break;
                default:
                    DarkLog.Debug("Handshake failed, reason " + reply);
                    //Server disconnects us.
                    break;
            }
        }

        private void HandleServerSettings(byte[] messageData)
        {
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    parent.warpWorker.warpMode = (WarpMode)mr.Read<int>();
                    numberOfKerbals = mr.Read<int>();
                    numberOfVessels = mr.Read<int>();
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling SERVER_SETTINGS message, exception: " + e);
            }
        }

        private void HandlePlayerStatus(byte[] messageData)
        {
            try
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
                    parent.playerStatusWorker.AddPlayerStatus(newStatus);
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling PLAYER_STATUS message, exception: " + e);
            }
        }

        private void HandlePlayerDisconnect(byte[] messageData)
        {
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    string playerName = mr.Read<string>();
                    parent.playerStatusWorker.RemovePlayerStatus(playerName);
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling PLAYER_STATUS message, exception: " + e);
            }
        }

        private void HandleSyncTimeReply(byte[] messageData)
        {
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    long clientSend = mr.Read<long>();
                    long serverReceive = mr.Read<long>();
                    long serverSend = mr.Read<long>();
                    parent.timeSyncer.HandleSyncTime(clientSend, serverReceive, serverSend);
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling SYNC_TIME_REPLY message, exception: " + e);
                SendDisconnect("Error handling sync time reply message");
            }
        }

        private void HandleKerbalReply(byte[] messageData)
        {
            numberOfKerbalsReceived++;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int kerbalID = mr.Read<int>();
                string kerbalData = mr.Read<string>();
                string tempFile = Path.GetTempFileName();
                using (StreamWriter sw = new StreamWriter(tempFile))
                {
                    sw.Write(kerbalData);
                }
                ConfigNode vesselNode = ConfigNode.Load(tempFile);
                File.Delete(tempFile);
                if (vesselNode != null)
                {
                    parent.vesselWorker.QueueKerbal(kerbalID, vesselNode);
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
                    parent.status = "Syncing kerbals " + numberOfKerbalsReceived + "/" + numberOfKerbals + " (" + (int)((numberOfKerbalsReceived / (float)numberOfKerbals) * 100) + "%)";
                }
            }
        }

        private void HandleKerbalComplete()
        {
            state = ClientState.KERBALS_SYNCED;
        }

        private void HandleVesselProto(byte[] messageData)
        {
            numberOfVesselsReceived++;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string vesselData = mr.Read<string>();
                string tempFile = Path.GetTempFileName();
                using (StreamWriter sw = new StreamWriter(tempFile))
                {
                    sw.Write(vesselData);
                }
                ConfigNode vesselNode = ConfigNode.Load(tempFile);
                File.Delete(tempFile);
                if (vesselNode != null)
                {
                    parent.vesselWorker.QueueVesselProto(vesselNode);
                }
                else
                {
                    DarkLog.Debug("Failed to load vessel!");
                }
            }
            if (state == ClientState.SYNCING_VESSELS)
            {
                if (numberOfKerbals != 0)
                {
                    parent.status = "Syncing vessels " + numberOfVesselsReceived + "/" + numberOfVessels + " (" + (int)((numberOfVesselsReceived / (float)numberOfVessels) * 100) + "%)";
                }
            }
        }

        private void HandleVesselUpdate(byte[] messageData)
        {
            VesselUpdate update = new VesselUpdate();
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                update.vesselID = mr.Read<string>();
                update.bodyName = mr.Read<string>();
                update.rotation = mr.Read<float[]>();
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
            }
            parent.vesselWorker.QueueVesselUpdate(update);
        }

        private void HandleSetActiveVessel(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string player = mr.Read<string>();
                string vesselID = mr.Read<string>();
                parent.vesselWorker.QueueActiveVessel(player, vesselID);
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
                string vesselID = mr.Read<string>();
                parent.vesselWorker.QueueVesselRemove(vesselID);
            }
        }

        private void HandleSetSubspace(byte[] messageData)
        {
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    int subspaceID = mr.Read<int>();
                    parent.timeSyncer.LockSubspace(subspaceID);
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling TIME_LOCK_REPLY message, exception: " + e);
                SendDisconnect("Error handling time lock reply message");
            }
        }

        private void HandleWarpControl(byte[] messageData)
        {
            parent.warpWorker.QueueWarpMessage(messageData);
        }

        private void HandleConnectionEnd(byte[] messageData)
        {
            string reason = "";
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                reason = mr.Read<string>();
            }
            Disconnect();
            ScreenMessages.PostScreenMessage("Server closed the connection: " + reason, 10f, ScreenMessageStyle.UPPER_CENTER);
            parent.status = "Server closed connection: " + reason;
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
                mw.Write<string>(parent.settings.playerName);
                mw.Write<string>(parent.settings.playerGuid.ToString());
                messageBytes = mw.GetMessageBytes();
            }
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.HANDSHAKE_REQUEST;
            newMessage.data = messageBytes;
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

        private void SendVesselsRequest()
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSELS_REQUEST;
            sendMessageQueueHigh.Enqueue(newMessage);
        }
        //Called from vesselWorker
        public void SendVesselProtoMessage(ProtoVessel vessel)
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
                    mw.Write<string>(vessel.vesselID.ToString());
                    mw.Write<string>(sr.ReadToEnd());
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
                mw.Write<string>(update.vesselID);
                mw.Write<string>(update.bodyName);
                mw.Write<float[]>(update.rotation);
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
        public void SendVesselRemove(string vesselID)
        {
            DarkLog.Debug("Removing " + vesselID + " from the server");
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSEL_REMOVE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(vesselID);
                newMessage.data = mw.GetMessageBytes();
            }
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
                mw.Write<string>(parent.settings.playerName);
                mw.Write<string>(activeVessel);
                newMessage.data = mw.GetMessageBytes();
            }
            sendMessageQueueHigh.Enqueue(newMessage);
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
                    mw.Write<int>(kerbalID);
                    mw.Write<string>(sr.ReadToEnd());
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
                parent.status = "Disconnected: " + disconnectReason;
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
        #endregion
    }
}

