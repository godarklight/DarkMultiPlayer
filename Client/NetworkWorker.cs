using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;
using MessageStream;

namespace DarkMultiPlayer
{
    public class NetworkWorker
    {
        private const float CONNECTION_TIMEOUT = 10;
        private const float HEARTBEAT_INTERVAL = 1;
        private ClientState state;
        private Client parent;
        private TcpClient clientConnection;
        private float lastSendTime;
        private float connectionTime;
        private bool isSendingMessage;
        private Queue<ClientMessage> sendMessageQueueHigh;
        private Queue<ClientMessage> sendMessageQueueSplit;
        private Queue<ClientMessage> sendMessageQueueLow;
        private float lastReceiveTime;
        private bool isReceivingMessage;
        private int receiveMessageBytesLeft;
        private ServerMessage receiveMessage;

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
                SendHandshakeRequest();
            }
            if (state == ClientState.AUTHENTICATED)
            {
                DarkLog.Debug("Sending time sync!");
                state = ClientState.TIME_SYNCING;
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
                state = ClientState.SYNCING_KERBALS;
                SendKerbalsRequest();
            }
            if (state == ClientState.KERBALS_SYNCED)
            {
                DarkLog.Debug("Kerbals Synced!");
                DarkLog.Debug("Requesting vessels!");
                state = ClientState.SYNCING_VESSELS;
                SendVesselsRequest();
            }
            if (state == ClientState.VESSELS_SYNCED)
            {
                DarkLog.Debug("Vessels Synced!");
                DarkLog.Debug("Requesting time lock!");
                state = ClientState.TIME_LOCKING;
                SendTimeLockRequest();
            }
            if (state == ClientState.TIME_LOCKED)
            {
                DarkLog.Debug("Time Locked!");
                DarkLog.Debug("Starting Game!");
                connectionTime = UnityEngine.Time.realtimeSinceStartup;
                state = ClientState.STARTING;
                parent.StartGame();
            }
            if ((state == ClientState.STARTING) && (HighLogic.LoadedScene == GameScenes.SPACECENTER) && ((UnityEngine.Time.realtimeSinceStartup - connectionTime) > 5f))
            {
                state = ClientState.RUNNING;
                parent.gameRunning = true;
                parent.vesselWorker.enabled = true;
                parent.timeSyncer.enabled = true;
            }
            /*
            if (state == ClientState.RUNNING)
            {
                DarkLog.Debug("Game running, Disconnecting due to happyness!");
                SendDisconnect("Everything worked, test disconnections");
            }
            */
        }
        #region Connecting to server
        //Called from main
        public void ConnectToServer(string address, string port)
        {
            if (state == ClientState.DISCONNECTED)
            {
                DarkLog.Debug("Trying to connect to " + address + ", port " + port);
                sendMessageQueueHigh = new Queue<ClientMessage>();
                sendMessageQueueSplit = new Queue<ClientMessage>();
                sendMessageQueueLow = new Queue<ClientMessage>();
                parent.timeSyncer.Reset();
                int destinationPort;
                if (!Int32.TryParse(port, out destinationPort))
                {
                    return;
                }
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
                        return;
                    }

                }
                IPEndPoint destination = new IPEndPoint(destinationAddress, destinationPort);
                clientConnection = new TcpClient(destination.AddressFamily);
                clientConnection.NoDelay = true;
                try
                {
                    DarkLog.Debug("Connecting to " + destinationAddress + " port " + destinationPort + "...");
                    lastSendTime = UnityEngine.Time.realtimeSinceStartup;
                    lastReceiveTime = UnityEngine.Time.realtimeSinceStartup;
                    state = ClientState.CONNECTING;
                    clientConnection.BeginConnect(destination.Address, destination.Port, new AsyncCallback(ConnectionCallback), null);
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Connection error: " + e);
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
                                isReceivingMessage = true;
                                receiveMessage.data = new byte[length];
                                receiveMessageBytesLeft = receiveMessage.data.Length;
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
            }
        }

        private void SendNetworkMessage(ClientMessage message)
        {
            byte[] messageBytes;
            using (MessageWriter mw = new MessageWriter((int)message.type, true))
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
                    case ServerMessageType.SYNC_TIME_REPLY:
                        HandleSyncTimeReply(message.data);
                        break;
                    case ServerMessageType.KERBAL_REPLY:
                        HandleKerbalReply(message.data);
                        break;
                    case ServerMessageType.KERBAL_COMPLETE:
                        HandleKerbalComplete();
                        break;
                    case ServerMessageType.VESSEL_REPLY:
                        HandleVesselReply(message.data);
                        break;
                    case ServerMessageType.SET_ACTIVE_VESSEL:
                        HandleSetActiveVessel(message.data);
                        break;
                    case ServerMessageType.VESSEL_COMPLETE:
                        HandleVesselComplete();
                        break;
                    case ServerMessageType.TIME_LOCK_REPLY:
                        HandleTimeLockReply(message.data);
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
                    SendDisconnect("Failed to handshake, reason " + reply);
                    break;
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
            using (MessageReader mr = new MessageReader(messageData, false))
            {
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
                    parent.vesselWorker.QueueKerbal(vesselNode);
                }
                else
                {
                    DarkLog.Debug("Failed to load kerbal!");
                }
            }
        }

        private void HandleKerbalComplete()
        {
            state = ClientState.KERBALS_SYNCED;
        }

        private void HandleVesselReply(byte[] messageData)
        {
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
                    parent.vesselWorker.QueueVessel(vesselNode);
                }
                else
                {
                    DarkLog.Debug("Failed to load vessel!");
                }
            }
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

        private void HandleTimeLockReply(byte[] messageData)
        {
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    long serverTimeLock = mr.Read<long>();
                    double planetariumTimeLock = mr.Read<double>();
                    float gameSpeedLock = mr.Read<float>();
                    parent.timeSyncer.LockTime(serverTimeLock, planetariumTimeLock, gameSpeedLock);
                    state = ClientState.TIME_LOCKED;
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling TIME_LOCK_REPLY message, exception: " + e);
                SendDisconnect("Error handling time lock reply message");
            }

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
            using (MessageWriter mw = new MessageWriter(0, false))
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

        //Called from timeSyncer
        public void SendTimeSync()
        {
            byte[] messageBytes;
            using (MessageWriter mw = new MessageWriter(0, false))
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
            newMessage.type = ClientMessageType.SEND_VESSEL_PROTO;
            vessel.Save(currentNode);
            string tempFile = Path.GetTempFileName();
            currentNode.Save(tempFile);
            using (StreamReader sr = new StreamReader(tempFile))
            {
                using (MessageWriter mw = new MessageWriter(0, false))
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

        /*
        private void SendVesselUpdate(VesselUpdate update)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SEND_VESSEL_UPDATE;
            sendMessageQueueHigh.Enqueue(newMessage);
        }
        */

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
            using (MessageWriter mw = new MessageWriter(0, false)) {
                mw.Write<string>(parent.playerName);
                mw.Write<string>(activeVessel);
                newMessage.data = mw.GetMessageBytes();
            }
            sendMessageQueueHigh.Enqueue(newMessage);
        }

        private void SendTimeLockRequest()
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.TIME_LOCK_REQUEST;
            sendMessageQueueHigh.Enqueue(newMessage);
        }

        //Called from vesselWorker
        public void SendKerbalProtoMessage(ProtoCrewMember kerbal)
        {
            ConfigNode currentNode = new ConfigNode();
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SEND_KERBAL_PROTO;
            kerbal.Save(currentNode);
            string tempFile = Path.GetTempFileName();
            currentNode.Save(tempFile);
            using (StreamReader sr = new StreamReader(tempFile))
            {
                using (MessageWriter mw = new MessageWriter(0, false))
                {
                    mw.Write<string>(kerbal.name);
                    mw.Write<string>(sr.ReadToEnd());
                    newMessage.data = mw.GetMessageBytes();
                }
            }
            File.Delete(tempFile);
            DarkLog.Debug("Sending kerbal " + kerbal.name + ", size: " + newMessage.data.Length);
            sendMessageQueueLow.Enqueue(newMessage);
        }

        //Called from main
        public void SendDisconnect(string disconnectReason = "Unknown")
        {
            if (state != ClientState.DISCONNECTING && state >= ClientState.CONNECTED)
            {
                DarkLog.Debug("Sending disconnect message, reason: " + disconnectReason);
                state = ClientState.DISCONNECTING;
                byte[] messageBytes;
                using (MessageWriter mw = new MessageWriter(0, false))
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

