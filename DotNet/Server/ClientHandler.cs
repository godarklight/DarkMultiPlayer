using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using MessageStream2;
using System.IO;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;

namespace DarkMultiPlayerServer
{
    public class ClientHandler
    {
        private static NetworkHandler<ClientObject> darkNetworkHandler;
        private static DarkNetwork<ClientObject> darkNetwork;
        //private static TcpListener TCPServer;
        private static List<ClientObject> clients = new List<ClientObject>();
        private static List<ClientObject> sendToAllClients = new List<ClientObject>();

        //When a client hits 100kb on the send queue, DMPServer will throw out old duplicate messages
        private const int OPTIMIZE_QUEUE_LIMIT = 100 * 1024;

        public static void ThreadMain()
        {
            try
            {
                Messages.WarpControl.Reset();
                Messages.Chat.Reset();
                Messages.ScreenshotLibrary.Reset();

                SetupDarkNetwork();

                while (Server.serverRunning)
                {
                    ModpackSystem.fetch.SendFilesToClients();
                    //Check timers
                    NukeKSC.CheckTimer();
                    Dekessler.CheckTimer();
                    Messages.WarpControl.CheckTimer();
                    //Run plugin update
                    DMPPluginHandler.FireOnUpdate();
                    Thread.Sleep(10);
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Fatal error thrown, exception: " + e);
                Server.ShutDown("Crashed!");
            }
            ShutdownDarkNetwork();
        }

        private static void SetupDarkNetwork()
        {
            try
            {
                ByteRecycler.AddPoolSize(16 * 1024);
                ByteRecycler.AddPoolSize(512 * 1024);
                ByteRecycler.AddPoolSize(6 * 1024 * 1024);
                IPAddress bindAddress = IPAddress.Parse(Settings.settingsStore.address);
                darkNetworkHandler = new NetworkHandler<ClientObject>(false);
                darkNetworkHandler.RegisterConnectCallback(SetupClient);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.HANDSHAKE_RESPONSE, Messages.Handshake.HandleHandshakeResponse);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.CHAT_MESSAGE, Messages.Chat.HandleChatMessage);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.PLAYER_STATUS, Messages.PlayerStatus.HandlePlayerStatus);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.PLAYER_COLOR, Messages.PlayerColor.HandlePlayerColor);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.GROUP, Messages.GroupMessage.HandleMessage);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.SCENARIO_DATA, Messages.ScenarioData.HandleScenarioModuleData);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.SYNC_TIME_REQUEST, Messages.SyncTimeRequest.HandleSyncTimeRequest);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.KERBALS_REQUEST, Messages.KerbalsRequest.HandleKerbalsRequest);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.KERBAL_PROTO, Messages.KerbalProto.HandleKerbalProto);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.VESSELS_REQUEST, Messages.VesselRequest.HandleVesselsRequest);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.VESSEL_PROTO, Messages.VesselProto.HandleVesselProto);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.VESSEL_UPDATE, Messages.VesselUpdate.HandleVesselUpdate);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.VESSEL_REMOVE, Messages.VesselRemove.HandleVesselRemoval);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.PERMISSION, Messages.PermissionMessage.HandleMessage);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.CRAFT_LIBRARY, Messages.CraftLibrary.HandleCraftLibrary);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.SCREENSHOT_LIBRARY, Messages.ScreenshotLibrary.HandleScreenshotLibrary);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.FLAG_SYNC, Messages.FlagSync.HandleFlagSync);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.PING_REQUEST, Messages.PingRequest.HandlePingRequest);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.MOTD_REQUEST, Messages.MotdRequest.HandleMotdRequest);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.WARP_CONTROL, Messages.WarpControl.HandleWarpControl);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.LOCK_SYSTEM, Messages.LockSystem.HandleLockSystemMessage);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.MOD_DATA, Messages.ModData.HandleModDataMessage);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.KERBAL_REMOVE, Messages.VesselRemove.HandleKerbalRemoval);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.CONNECTION_END, Messages.ConnectionEnd.HandleConnectionEnd);
                darkNetworkHandler.RegisterCallback((int)ClientMessageType.MODPACK_DATA, Messages.Modpack.HandleModpackMessage);
                darkNetworkHandler.RegisterDisconnectCallback(DisconnectCallback);
                darkNetwork = new DarkNetwork<ClientObject>();
                darkNetwork.SetupServer(new IPEndPoint(bindAddress, Settings.settingsStore.port), darkNetworkHandler);
            }
            catch (Exception e)
            {
                DarkLog.Normal("Error setting up server, Exception: " + e);
                Server.serverRunning = false;
            }
            Server.serverStarting = false;
        }

        private static void ShutdownDarkNetwork()
        {
            darkNetwork.Shutdown();
        }

        private static ClientObject SetupClient(Connection<ClientObject> newClientConnection)
        {
            DarkLog.Normal("New client connection from " + newClientConnection.remoteEndpoint);
            ClientObject newClientObject = new ClientObject();
            newClientObject.subspace = Messages.WarpControl.GetLatestSubspace();
            newClientObject.playerStatus = new PlayerStatus();
            newClientObject.connectionStatus = ConnectionStatus.CONNECTED;
            newClientObject.endpoint = newClientConnection.remoteEndpoint.ToString();
            newClientObject.ipAddress = (newClientConnection.remoteEndpoint).Address;
            //Keep the connection reference
            newClientObject.connection = newClientConnection;
            DMPPluginHandler.FireOnClientConnect(newClientObject);
            Messages.Handshake.SendHandshakeChallange(newClientObject);
            lock (clients)
            {
                clients.Add(newClientObject);
                Server.playerCount = GetActiveClientCount();
                Server.players = GetActivePlayerNames();
                DarkLog.Debug("Online players is now: " + Server.playerCount + ", connected: " + clients.Count);
            }
            return newClientObject;
        }

        private static void DisconnectCallback(Connection<ClientObject> connection)
        {
            DisconnectClient(connection.state);
        }

        public static int GetActiveClientCount()
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

        public static string GetActivePlayerNames()
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

        private static void SendNetworkMessage(ClientObject client, NetworkMessage message)
        {
            client.connection.handler.SendMessage(message);
            DMPPluginHandler.FireOnMessageSent(client, message);
            if (message.type == (int)ServerMessageType.CONNECTION_END)
            {
                using (MessageReader mr = new MessageReader(message.data.data))
                {
                    string reason = mr.Read<string>();
                    DarkLog.Normal("Disconnecting client " + client.playerName + ", sent CONNECTION_END (" + reason + ") to endpoint " + client.endpoint);
                    DisconnectClient(client);
                }
            }
            if (message.type == (int)ServerMessageType.HANDSHAKE_REPLY)
            {
                using (MessageReader mr = new MessageReader(message.data.data))
                {
                    int response = mr.Read<int>();
                    string reason = mr.Read<string>();
                    if (response != 0)
                    {
                        DarkLog.Normal("Disconnecting client " + client.playerName + ", sent HANDSHAKE_REPLY (" + reason + ") to endpoint " + client.endpoint);
                        DisconnectClient(client);
                    }
                }
            }
        }

        internal static void DisconnectClient(ClientObject client)
        {
            //Remove clients from list
            lock (clients)
            {
                if (clients.Contains(client))
                {
                    clients.Remove(client);
                    Server.playerCount = GetActiveClientCount();
                    Server.players = GetActivePlayerNames();
                    DarkLog.Debug("Online players is now: " + Server.playerCount + ", connected: " + clients.Count);
                    Messages.WarpControl.DisconnectPlayer(client);
                }
            }
            //Disconnect
            if (client.connectionStatus != ConnectionStatus.DISCONNECTED)
            {
                DMPPluginHandler.FireOnClientDisconnect(client);
                if (client.playerName != null)
                {
                    Messages.Chat.RemovePlayer(client.playerName);
                }
                client.connectionStatus = ConnectionStatus.DISCONNECTED;
                if (client.authenticated)
                {
                    NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.PLAYER_DISCONNECT, 2048, NetworkMessageType.ORDERED_RELIABLE);
                    using (MessageWriter mw = new MessageWriter(newMessage.data.data))
                    {
                        mw.Write<string>(client.playerName);
                        newMessage.data.size = (int)mw.GetMessageLength();
                    }
                    SendToAll(client, newMessage, true);
                    LockSystem.fetch.ReleasePlayerLocks(client.playerName);
                }
                Server.lastPlayerActivity = Server.serverClock.ElapsedMilliseconds;
            }
        }

        //Call with null client to send to all clients. Also called from Dekessler and NukeKSC.
        public static void SendToAll(ClientObject ourClient, NetworkMessage message, bool highPriority)
        {
            lock (clients)
            {
                foreach (ClientObject otherClient in clients)
                {
                    if (ourClient != otherClient)
                    {
                        sendToAllClients.Add(otherClient);
                    }
                }
                message.usageCount = sendToAllClients.Count;
                foreach (ClientObject sendClient in clients)
                {
                    sendClient.connection.handler.SendMessage(message);
                }
            }
        }

        public static void SendToClient(ClientObject client, NetworkMessage message, bool highPriority)
        {
            client.connection.handler.SendMessage(message, client.connection);
        }

        public static ClientObject[] GetClients()
        {
            List<ClientObject> returnArray = new List<ClientObject>(clients);
            return returnArray.ToArray();
        }

        public static bool ClientConnected(ClientObject client)
        {
            return clients.Contains(client);
        }

        public static ClientObject GetClientByName(string playerName)
        {
            ClientObject findClient = null;
            lock (clients)
            {
                foreach (ClientObject testClient in clients)
                {
                    if (testClient.authenticated && testClient.playerName == playerName)
                    {
                        findClient = testClient;
                        break;
                    }
                }
            }
            return findClient;
        }

        public static ClientObject GetClientByIP(IPAddress ipAddress)
        {
            ClientObject findClient = null;
            lock (clients)
            {
                foreach (ClientObject testClient in clients)
                {
                    if (testClient.authenticated && testClient.ipAddress == ipAddress)
                    {
                        findClient = testClient;
                        break;
                    }
                }
            }
            return findClient;
        }

        public static ClientObject GetClientByPublicKey(string publicKey)
        {
            ClientObject findClient = null;
            foreach (ClientObject testClient in clients)
            {
                if (testClient.authenticated && testClient.publicKey == publicKey)
                {
                    findClient = testClient;
                    break;
                }
            }
            return findClient;
        }
    }

    public class ClientObject
    {
        public bool authenticated;
        public bool modsSyncing;
        public byte[] challange;
        public string playerName = "Unknown";
        public string clientVersion;
        public bool isBanned;
        public IPAddress ipAddress;
        public string publicKey;
        //subspace tracking
        public int subspace = -1;
        public float subspaceRate = 1f;
        //vessel tracking
        public string activeVessel = "";
        //connection
        public string endpoint;
        public Connection<ClientObject> connection;
        //State tracking
        public ConnectionStatus connectionStatus;
        public PlayerStatus playerStatus;
        public float[] playerColor;
    }
}

