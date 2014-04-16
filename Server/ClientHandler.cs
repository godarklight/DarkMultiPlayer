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
        private static List<ClientObject> addClients;
        private static List<ClientObject> clients;
        private static List<ClientObject> deleteClients;
        #region Main loop
        public static void ThreadMain()
        {
            addClients = new List<ClientObject>();
            clients = new List<ClientObject>();
            deleteClients = new List<ClientObject>();
            SetupTCPServer();
            while (Server.serverRunning)
            {
                //Add new clients
                foreach (ClientObject client in addClients)
                {
                    clients.Add(client);
                }
                addClients.Clear();
                //Process current clients
                foreach (ClientObject client in clients)
                {
                    CheckHeartBeat(client);
                    //HandleClientMessages(client);
                    SendOutgoingMessages(client);
                }
                //Delete old clients
                foreach (ClientObject client in deleteClients)
                {
                    clients.Remove(client);
                }
                deleteClients.Clear();
                Thread.Sleep(10);
            }
            ShutdownTCPServer();
        }
        #endregion
        #region Server setup
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
                DarkLog.Debug("Error setting up server, Exception: " + e);
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
                    DarkLog.Debug("New client connection from " + newClient.Client.RemoteEndPoint);
                }
                catch
                {
                    DarkLog.Debug("Error accepting client!");
                }
                TCPServer.BeginAcceptTcpClient(new AsyncCallback(NewClientCallback), null);
            }
        }

        private static void SetupClient(TcpClient newClientConnection)
        {
            ClientObject newClientObject = new ClientObject();
            newClientObject.status = ConnectionStatus.CONNECTED;
            newClientObject.playerName = "Unknown";
            newClientObject.endpoint = newClientConnection.Client.RemoteEndPoint.ToString();
            //Keep the connection reference
            newClientObject.connection = newClientConnection;
            //Add the queues
            newClientObject.sendMessageQueueHigh = new Queue<ServerMessage>();
            newClientObject.sendMessageQueueSplit = new Queue<ServerMessage>();
            newClientObject.sendMessageQueueLow = new Queue<ServerMessage>();
            newClientObject.receiveMessageQueue = new Queue<ClientMessage>();
            StartReceivingIncomingMessages(newClientObject);
            clients.Add(newClientObject);
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

        private static void SendOutgoingMessages(ClientObject client) {
            if (!client.isSendingToClient)
            {
                if (client.sendMessageQueueHigh.Count > 0)
                {
                    ServerMessage message = client.sendMessageQueueHigh.Dequeue();
                    SendNetworkMessage(client, message);
                    return;
                }
                if (client.sendMessageQueueSplit.Count > 0)
                {
                    ServerMessage message = client.sendMessageQueueSplit.Dequeue();
                    SendNetworkMessage(client, message);
                    return;
                }
                if (client.sendMessageQueueLow.Count > 0)
                {
                    ServerMessage message = client.sendMessageQueueLow.Dequeue();
                    SendNetworkMessage(client, message);
                    return;
                }
            }
        }

        private static void SendNetworkMessage(ClientObject client, ServerMessage message)
        {
            //Write the send times down in SYNC_TIME_REPLY packets
            if (message.type == ServerMessageType.SYNC_TIME_REPLY)
            {
                try {
                    using (MessageWriter mw = new MessageWriter(0, false))
                    {
                        using (MessageReader mr = new MessageReader(message.data, false)) {
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
                catch (Exception e) {
                    DarkLog.Debug("Error rewriting SYNC_TIME packet, Exception " + e);
                }
            }
            //Continue sending
            byte[] messageBytes;
            using (MessageWriter mw = new MessageWriter((int)message.type, true))
            {
                if (message.data != null)
                {
                    mw.Write<byte[]>(message.data);
                }
                messageBytes = mw.GetMessageBytes();
            }
            client.isSendingToClient = true;
            client.lastSendTime = Server.serverClock.ElapsedMilliseconds;
            try
            {
                client.connection.GetStream().BeginWrite(messageBytes, 0, messageBytes.Length, new AsyncCallback(SendMessageCallback), client);
            }
            catch (Exception e)
            {
                DarkLog.Debug("Client " + client.playerName + " disconnected, endpoint " + client.endpoint + " error: " + e.Message);
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
                DarkLog.Debug("Client " + client.playerName + " disconnected, endpoint " + client.endpoint + ", error: " + e.Message);
                DisconnectClient(client);
            }
            client.isSendingToClient = false;
        }

        private static void StartReceivingIncomingMessages(ClientObject client) {
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
                DarkLog.Debug("Connection error: " + e.Message);
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
                                client.isReceivingMessage = true;
                                client.receiveMessage.data = new byte[length];
                                client.receiveMessageBytesLeft = client.receiveMessage.data.Length;
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
                if (client.status == ConnectionStatus.CONNECTED)
                {
                    client.lastReceiveTime = Server.serverClock.ElapsedMilliseconds;
                    client.connection.GetStream().BeginRead(client.receiveMessage.data, client.receiveMessage.data.Length - client.receiveMessageBytesLeft, client.receiveMessageBytesLeft, new AsyncCallback(ReceiveCallback), client);
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Connection error: " + e.Message);
                DisconnectClient(client);
            }
        }

        private static void DisconnectClient(ClientObject client)
        {
            client.status = ConnectionStatus.DISCONNECTED;
            deleteClients.Add(client);
        }


        #endregion
        #region Message handling
        private static void HandleMessage(ClientObject client, ClientMessage message)
        {
            switch (message.type)
            {
                case ClientMessageType.HEARTBEAT:
                    //Don't do anything for heartbeats, they just keep the connection alive
                    break;
                case ClientMessageType.HANDSHAKE_REQUEST:
                    HandleHandshakeRequest(client, message.data);
                    break;
                case ClientMessageType.SYNC_TIME_REQUEST:
                    HandleSyncTimeRequest(client, message.data);
                    break;
                case ClientMessageType.KERBALS_REQUEST:
                    HandleKerbalsRequest(client);
                    break;
                case ClientMessageType.SEND_KERBAL:
                    HandleSendKerbal(client, message.data);
                    break;
                case ClientMessageType.VESSELS_REQUEST:
                    HandleVesselsRequest(client);
                    break;
                case ClientMessageType.SEND_VESSEL:
                    HandleSendVessel(client, message.data);
                    break;
                case ClientMessageType.TIME_LOCK_REQUEST:
                    HandleTimeLockRequest(client);
                    break;
                case ClientMessageType.CONNECTION_END:
                    HandleConnectionEnd(client, message.data);
                    break;
                default:
                    DarkLog.Debug("Unhandled message type " + message.type);
                    break;
            }
        }

        private static void HandleHandshakeRequest(ClientObject client, byte[] messageData) {
            try {
                int protocolVersion;
                string playerName = "";
                string playerGuid = Guid.Empty.ToString();
                //0 - Success
                int handshakeReponse = 0;
                using (MessageReader mr = new MessageReader(messageData, false)) {
                    protocolVersion = mr.Read<int>();
                    playerName = mr.Read<string>();
                    playerGuid = mr.Read<string>();
                }
                if (protocolVersion != Common.PROTOCOL_VERSION) {
                    //Protocol mismatch
                    handshakeReponse = 1;
                }
                if (handshakeReponse == 0) {
                    //Check client isn't already connected
                    foreach (ClientObject testClient in clients) {
                        if (client != testClient && testClient.playerName == playerName) {
                            handshakeReponse = 2;
                        }
                    }
                }
                if (handshakeReponse == 0) {
                    //Check the client matches any database entry
                    if (playerGuid == "") {
                        handshakeReponse = 3;
                    }
                }
                client.playerName = playerName;
                if (handshakeReponse == 0) {
                    DarkLog.Debug("Client " + client.playerName + " handshook successfully!");
                } else {
                    DarkLog.Debug("Client " + client.playerName + " failed to handshake, reason " + handshakeReponse);
                }
                SendHandshakeReply(client, handshakeReponse);
            }
            catch (Exception e) {
                DarkLog.Debug("Error in HANDSHAKE_REQUEST from "+client.playerName+": " + e);
                SendHandshakeReply(client, 99);
                DisconnectClient(client);
            }
        }

        private static void HandleSyncTimeRequest(ClientObject client, byte[] messageData) {
            try {
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.SYNC_TIME_REPLY;
                using (MessageWriter mw = new MessageWriter(0, false)) {
                    using (MessageReader mr = new MessageReader(messageData, false)) {
                        //Client send time
                        mw.Write<long>(mr.Read<long>());
                        //Server receive time
                        mw.Write<long>(DateTime.UtcNow.Ticks);
                        newMessage.data = mw.GetMessageBytes();
                    }
                }
                SendToClient(client, newMessage, true);

            }
            catch (Exception e) {
                DarkLog.Debug("Error in SYNC_TIME_REQUEST from "+client.playerName+": " + e);
                DisconnectClient(client);
            }
        }

        private static void HandleKerbalsRequest(ClientObject client)
        {
            DarkLog.Debug("Sending " + client.playerName + " kerbals...");
            //Send vessels here
            foreach (string file in Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Universe", "Kerbals")))
            {
                using (StreamReader sr = new StreamReader(file))
                {
                    string kerbalData = sr.ReadToEnd();
                    SendKerbal(client, kerbalData);
                }
            }
            SendKerbalsComplete(client);
        }

        private static void HandleSendKerbal(ClientObject client, byte[] messageData)
        {
            //Send kerbal
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string kerbalName = mr.Read<string>();
                string kerbalData = mr.Read<string>();
                using (StreamWriter sw = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Universe", "Kerbals", kerbalName + ".txt")))
                {
                    sw.Write(kerbalData);
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.KERBAL_REPLY;
                    newMessage.data = messageData;
                    SendToAll(client, newMessage, false);
                }
            }
        }

        private static void HandleVesselsRequest(ClientObject client)
        {
            DarkLog.Debug("Sending " + client.playerName + " vessels...");
            //Send vessels here
            foreach (string file in Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Universe", "Vessels")))
            {
                using (StreamReader sr = new StreamReader(file))
                {
                    string vesselData = sr.ReadToEnd();
                    SendVessel(client, vesselData);
                }
            }
            SendVesselsComplete(client);
        }

        private static void HandleSendVessel(ClientObject client, byte[] messageData)
        {
            //Send kerbal
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string vesselGuid = mr.Read<string>();
                string vesselData = mr.Read<string>();
                using (StreamWriter sw = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Universe", "Vessels", vesselGuid + ".txt")))
                {
                    sw.Write(vesselData);
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.VESSEL_REPLY;
                    newMessage.data = messageData;
                    SendToAll(client, newMessage, false);
                }
            }
        }

        private static void HandleTimeLockRequest(ClientObject client)
        {
            DarkLog.Debug("Sending " + client.playerName + " time lock...");
            SendTimeLockReply(client);
        }

        private static void HandleConnectionEnd(ClientObject client, byte[] messageData)
        {
            string reason = "Unknown";
            try {
                using (MessageReader mr = new MessageReader(messageData, false)) {
                    reason = mr.Read<string>();
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling CONNECTION_END message from " + client.playerName + ":" + e);
            }
            DarkLog.Debug(client.playerName + " sent connection end message, reason: " + reason);
        }
        #endregion
        #region Message sending
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
            if (highPriority)
            {
                client.sendMessageQueueHigh.Enqueue(message);
            }
            else
            {
                client.sendMessageQueueLow.Enqueue(message);
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
            using (MessageWriter mw = new MessageWriter(0, false))
            {
                mw.Write<int>(response);
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

        private static void SendKerbal(ClientObject client, string kerbalData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.KERBAL_REPLY;
            using (MessageWriter mw = new MessageWriter(0, false)) {
                mw.Write<string>(kerbalData);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, false);
        }

        private static void SendVessel(ClientObject client, string vesselData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_REPLY;
            using (MessageWriter mw = new MessageWriter(0, false)) {
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

        private static void SendTimeLockReply(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.TIME_LOCK_REPLY;
            using (MessageWriter mw = new MessageWriter(0, false))
            {
                mw.Write<long>(DateTime.UtcNow.Ticks);
                mw.Write<double>(100d);
                mw.Write<float>(1f);
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
        public string endpoint;
        public TcpClient connection;
        public long lastSendTime;
        public bool isSendingToClient;
        public Queue<ServerMessage> sendMessageQueueHigh;
        public Queue<ServerMessage> sendMessageQueueSplit;
        public Queue<ServerMessage> sendMessageQueueLow;
        public Queue<ClientMessage> receiveMessageQueue;
        public long lastReceiveTime;
        public bool isReceivingMessage;
        public int receiveMessageBytesLeft;
        public ClientMessage receiveMessage;
        public ConnectionStatus status;
    }
}

