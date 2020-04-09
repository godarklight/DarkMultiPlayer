using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class NetworkWorker
    {
        //Read from ConnectionWindow
        public ClientState state
        {
            private set;
            get;
        }
        private byte[] messageWriterBuffer = new byte[Common.MAX_MESSAGE_SIZE];
        //Used for the initial sync
        private int numberOfKerbals = 0;
        private int numberOfKerbalsReceived = 0;
        private int numberOfVessels = 0;
        private int numberOfVesselsReceived = 0;
        //Connection tracking
        private Thread connectThread;
        private string connectionEndReason;
        //DarkNetwork Connection
        private NetworkHandler<ClientObject> handler;
        private DarkNetwork<ClientObject> darkNetwork;
        private Connection<ClientObject> serverConnection;
        private long lastConnectTime = 0;
        //Mesh stuff
        /*
        private UdpMeshClient meshClient;
        private Thread meshClientThread;
        private double lastSendSetPlayer;
        private Dictionary<string, Guid> meshPlayerGuids = new Dictionary<string, Guid>();
        */
        private string serverMotd;
        private bool displayMotd;
        private bool disconnectAfterDownloadingMods = false;
        //Services
        private DMPGame dmpGame;
        private Settings dmpSettings;
        private ConnectionWindow connectionWindow;
        private TimeSyncer timeSyncer;
        private Groups groups;
        private Permissions permissions;
        private WarpWorker warpWorker;
        private ChatWorker chatWorker;
        private PlayerColorWorker playerColorWorker;
        private FlagSyncer flagSyncer;
        private PartKiller partKiller;
        private KerbalReassigner kerbalReassigner;
        private AsteroidWorker asteroidWorker;
        private VesselWorker vesselWorker;
        private PlayerStatusWorker playerStatusWorker;
        private ScenarioWorker scenarioWorker;
        private CraftLibraryWorker craftLibraryWorker;
        private ScreenshotWorker screenshotWorker;
        private ToolbarSupport toolbarSupport;
        private AdminSystem adminSystem;
        private LockSystem lockSystem;
        private DMPModInterface dmpModInterface;
        private ModWorker modWorker;
        private ConfigNodeSerializer configNodeSerializer;
        private UniverseSyncCache universeSyncCache;
        private VesselRecorder vesselRecorder;
        private VesselRangeBumper vesselRangeBumper;
        private ModpackWorker modpackWorker;
        private NamedAction updateAction;
        private Profiler profiler;

        public NetworkWorker(DMPGame dmpGame, Settings dmpSettings, ConnectionWindow connectionWindow, ModWorker modWorker, ConfigNodeSerializer configNodeSerializer, Profiler profiler, VesselRangeBumper vesselRangeBumper)
        {
            this.dmpGame = dmpGame;
            this.dmpSettings = dmpSettings;
            this.connectionWindow = connectionWindow;
            this.modWorker = modWorker;
            this.configNodeSerializer = configNodeSerializer;
            this.profiler = profiler;
            this.vesselRangeBumper = vesselRangeBumper;
            updateAction = new NamedAction(Update);
            dmpGame.updateEvent.Add(updateAction);
        }

        public void SetDependencies(TimeSyncer timeSyncer, WarpWorker warpWorker, ChatWorker chatWorker, PlayerColorWorker playerColorWorker, FlagSyncer flagSyncer, PartKiller partKiller, KerbalReassigner kerbalReassigner, AsteroidWorker asteroidWorker, VesselWorker vesselWorker, PlayerStatusWorker playerStatusWorker, ScenarioWorker scenarioWorker, CraftLibraryWorker craftLibraryWorker, ScreenshotWorker screenshotWorker, ToolbarSupport toolbarSupport, AdminSystem adminSystem, LockSystem lockSystem, DMPModInterface dmpModInterface, UniverseSyncCache universeSyncCache, VesselRecorder vesselRecorder, Groups groups, Permissions permissions, ModpackWorker modpackWorker)
        {
            this.timeSyncer = timeSyncer;
            this.warpWorker = warpWorker;
            this.chatWorker = chatWorker;
            this.playerColorWorker = playerColorWorker;
            this.flagSyncer = flagSyncer;
            this.partKiller = partKiller;
            this.kerbalReassigner = kerbalReassigner;
            this.asteroidWorker = asteroidWorker;
            this.vesselWorker = vesselWorker;
            this.playerStatusWorker = playerStatusWorker;
            this.scenarioWorker = scenarioWorker;
            this.craftLibraryWorker = craftLibraryWorker;
            this.screenshotWorker = screenshotWorker;
            this.toolbarSupport = toolbarSupport;
            this.adminSystem = adminSystem;
            this.lockSystem = lockSystem;
            this.dmpModInterface = dmpModInterface;
            this.universeSyncCache = universeSyncCache;
            this.vesselRecorder = vesselRecorder;
            this.groups = groups;
            this.permissions = permissions;
            this.modpackWorker = modpackWorker;
            vesselRecorder.SetHandlers(HandleVesselProto, HandleVesselUpdate, HandleVesselRemove);
            handler = new NetworkHandler<ClientObject>(false);
            handler.RegisterConnectCallback(ConnectCallback);
            handler.RegisterDisconnectCallback(DisconnectCallback);
            handler.RegisterCallback((int)ServerMessageType.HANDSHAKE_CHALLANGE, HandleHandshakeChallange);
            handler.RegisterCallback((int)ServerMessageType.HANDSHAKE_REPLY, HandleHandshakeReply);
            handler.RegisterCallback((int)ServerMessageType.CHAT_MESSAGE, HandleChatMessage);
            handler.RegisterCallback((int)ServerMessageType.SERVER_SETTINGS, HandleServerSettings);
            handler.RegisterCallback((int)ServerMessageType.PLAYER_STATUS, HandlePlayerStatus);
            handler.RegisterCallback((int)ServerMessageType.PLAYER_COLOR, playerColorWorker.HandlePlayerColorMessage);
            handler.RegisterCallback((int)ServerMessageType.PLAYER_JOIN, HandlePlayerJoin);
            handler.RegisterCallback((int)ServerMessageType.PLAYER_DISCONNECT, HandlePlayerDisconnect);
            handler.RegisterCallback((int)ServerMessageType.GROUP, HandleGroupMessage);
            handler.RegisterCallback((int)ServerMessageType.PERMISSION, HandlePermissionMessage);
            handler.RegisterCallback((int)ServerMessageType.SCENARIO_DATA, HandleScenarioModuleData);
            handler.RegisterCallback((int)ServerMessageType.KERBAL_REPLY, HandleKerbalReply);
            handler.RegisterCallback((int)ServerMessageType.KERBAL_COMPLETE, HandleKerbalComplete);
            handler.RegisterCallback((int)ServerMessageType.KERBAL_REMOVE, HandleKerbalRemove);
            handler.RegisterCallback((int)ServerMessageType.VESSEL_LIST, HandleVesselList);
            handler.RegisterCallback((int)ServerMessageType.VESSEL_PROTO, HandleVesselProto);
            handler.RegisterCallback((int)ServerMessageType.VESSEL_UPDATE, HandleVesselUpdate);
            handler.RegisterCallback((int)ServerMessageType.VESSEL_COMPLETE, HandleVesselComplete);
            handler.RegisterCallback((int)ServerMessageType.VESSEL_REMOVE, HandleVesselRemove);
            handler.RegisterCallback((int)ServerMessageType.CRAFT_LIBRARY, HandleCraftLibrary);
            handler.RegisterCallback((int)ServerMessageType.SCREENSHOT_LIBRARY, HandleScreenshotLibrary);
            handler.RegisterCallback((int)ServerMessageType.FLAG_SYNC, flagSyncer.HandleMessage);
            handler.RegisterCallback((int)ServerMessageType.SET_SUBSPACE, warpWorker.HandleSetSubspace);
            handler.RegisterCallback((int)ServerMessageType.SYNC_TIME_REPLY, HandleSyncTimeReply);
            handler.RegisterCallback((int)ServerMessageType.PING_REPLY, HandlePingReply);
            handler.RegisterCallback((int)ServerMessageType.MOTD_REPLY, HandleMotdReply);
            handler.RegisterCallback((int)ServerMessageType.WARP_CONTROL, HandleWarpControl);
            handler.RegisterCallback((int)ServerMessageType.ADMIN_SYSTEM, adminSystem.HandleAdminMessage);
            handler.RegisterCallback((int)ServerMessageType.LOCK_SYSTEM, lockSystem.HandleLockMessage);
            handler.RegisterCallback((int)ServerMessageType.MOD_DATA, dmpModInterface.HandleModData);
            handler.RegisterCallback((int)ServerMessageType.CONNECTION_END, HandleConnectionEnd);
            handler.RegisterCallback((int)ServerMessageType.MODPACK_DATA, HandleModpackData);
            darkNetwork = new DarkNetwork<ClientObject>();
        }

        public ClientObject ConnectCallback(Connection<ClientObject> connection)
        {
            if (serverConnection == null)
            {
                serverConnection = connection;
            }
            ClientObject clientObject = new ClientObject();
            return clientObject;
        }

        public void DisconnectCallback(Connection<ClientObject> connection)
        {
            if (serverConnection == connection)
            {
                Disconnect("Connection timeout");
                serverConnection = null;
            }
        }


        //Called from main
        private void Update()
        {
            if (state == ClientState.CONNECTED)
            {
                connectionWindow.status = "Connected";
            }

            if (state == ClientState.HANDSHAKING)
            {
                connectionWindow.status = "Handshaking";
            }

            if (state == ClientState.AUTHENTICATED)
            {
                state = ClientState.MODPACK_SYNCING;
                connectionWindow.status = "Syncing modpack";
            }
            if (state == ClientState.MODPACK_SYNCING)
            {
                connectionWindow.status = modpackWorker.syncString;
                if (modpackWorker.synced)
                {
                    if (!disconnectAfterDownloadingMods)
                    {
                        state = ClientState.MODPACK_SYNCED;
                    }
                    else
                    {
                        Disconnect("Failed mod validation");
                    }
                }
            }
            if (state == ClientState.MODPACK_SYNCED)
            {
                ModpackWorker.secondModSync = false;
                SendPlayerStatus(playerStatusWorker.myPlayerStatus);
                DarkLog.Debug("Sending time sync!");
                state = ClientState.TIME_SYNCING;
                connectionWindow.status = "Syncing server clock";
                SendTimeSync();
            }
            if (timeSyncer.synced && state == ClientState.TIME_SYNCING)
            {
                DarkLog.Debug("Time Synced!");
                state = ClientState.TIME_SYNCED;
            }
            if (state == ClientState.TIME_SYNCED)
            {
                DarkLog.Debug("Requesting Groups!");
                connectionWindow.status = "Syncing groups";
                state = ClientState.GROUPS_SYNCING;
                SendGroupsRequest();
            }
            if (groups.synced && state == ClientState.GROUPS_SYNCING)
            {
                DarkLog.Debug("Groups Synced!");
                state = ClientState.GROUPS_SYNCED;
            }
            if (state == ClientState.GROUPS_SYNCED)
            {
                DarkLog.Debug("Requesting permissions!");
                connectionWindow.status = "Syncing permissions";
                state = ClientState.PERMISSIONS_SYNCING;
                SendPermissionsRequest();
            }
            if (permissions.synced && state == ClientState.PERMISSIONS_SYNCING)
            {
                DarkLog.Debug("Permissions Synced!");
                state = ClientState.PERMISSIONS_SYNCED;
            }
            if (state == ClientState.PERMISSIONS_SYNCED)
            {
                state = ClientState.SYNCING_KERBALS;
                DarkLog.Debug("Requesting kerbals!");
                SendKerbalsRequest();
            }
            if (state == ClientState.VESSELS_SYNCED)
            {
                DarkLog.Debug("Vessels Synced!");
                connectionWindow.status = "Syncing universe time";
                state = ClientState.TIME_LOCKING;
                //The subspaces are held in the warp control messages, but the warp worker will create a new subspace if we aren't locked.
                //Process the messages so we get the subspaces, but don't enable the worker until the game is started.
                warpWorker.ProcessWarpMessages();
                timeSyncer.workerEnabled = true;
                chatWorker.workerEnabled = true;
                playerColorWorker.workerEnabled = true;
                flagSyncer.workerEnabled = true;
                flagSyncer.SendFlagList();
                vesselRangeBumper.workerEnabled = true;
                playerColorWorker.SendPlayerColorToServer();
                partKiller.RegisterGameHooks();
                kerbalReassigner.RegisterGameHooks();
            }
            if (state == ClientState.TIME_LOCKING)
            {
                if (timeSyncer.locked)
                {
                    DarkLog.Debug("Time Locked!");
                    DarkLog.Debug("Starting Game!");
                    connectionWindow.status = "Starting game";
                    state = ClientState.STARTING;
                    dmpGame.startGame = true;
                }
            }
            if ((state == ClientState.STARTING) && (HighLogic.LoadedScene == GameScenes.SPACECENTER))
            {
                state = ClientState.RUNNING;
                connectionWindow.status = "Running";
                dmpGame.running = true;
                asteroidWorker.workerEnabled = true;
                vesselWorker.workerEnabled = true;
                playerStatusWorker.workerEnabled = true;
                scenarioWorker.workerEnabled = true;
                warpWorker.workerEnabled = true;
                craftLibraryWorker.workerEnabled = true;
                screenshotWorker.workerEnabled = true;
                SendMotdRequest();
                toolbarSupport.EnableToolbar();
            }
            if (displayMotd && (HighLogic.LoadedScene != GameScenes.LOADING) && (Time.timeSinceLevelLoad > 2f))
            {
                displayMotd = false;
                scenarioWorker.UpgradeTheAstronautComplexSoTheGameDoesntBugOut();
                ScreenMessages.PostScreenMessage(serverMotd, 10f, ScreenMessageStyle.UPPER_CENTER);
                //Control locks will bug out the space centre sceen, so remove them before starting.
                DeleteAllTheControlLocksSoTheSpaceCentreBugGoesAway();
            }
        }

        private void DeleteAllTheControlLocksSoTheSpaceCentreBugGoesAway()
        {
            DarkLog.Debug("Clearing " + InputLockManager.lockStack.Count + " control locks");
            InputLockManager.ClearControlLocks();
        }


        //Called from main
        public void ConnectToServer(string address, int port)
        {
            DMPServerAddress connectAddress = new DMPServerAddress();
            connectAddress.ip = address;
            connectAddress.port = port;
            connectThread = new Thread(new ParameterizedThreadStart(ConnectToServerMain));
            connectThread.IsBackground = true;
            connectThread.Start(connectAddress);
        }

        private void ConnectToServerMain(object connectAddress)
        {
            DMPServerAddress connectAddressCast = (DMPServerAddress)connectAddress;
            string address = connectAddressCast.ip;
            int port = connectAddressCast.port;
            if (state == ClientState.DISCONNECTED)
            {
                DarkLog.Debug("Trying to connect to " + address + ", port " + port);
                connectionWindow.status = "Connecting to " + address + " port " + port;
                numberOfKerbals = 0;
                numberOfKerbalsReceived = 0;
                numberOfVessels = 0;
                numberOfVesselsReceived = 0;
                lastConnectTime = DateTime.UtcNow.Ticks;
                IPAddress destinationAddress;
                if (!IPAddress.TryParse(address, out destinationAddress))
                {
                    try
                    {
                        IPHostEntry dnsResult = Dns.GetHostEntry(address);
                        if (dnsResult.AddressList.Length > 0)
                        {
                            List<IPEndPoint> addressToConnectTo = new List<IPEndPoint>();
                            foreach (IPAddress testAddress in dnsResult.AddressList)
                            {
                                if (testAddress.AddressFamily == AddressFamily.InterNetwork || testAddress.AddressFamily == AddressFamily.InterNetworkV6)
                                {
                                    connectionWindow.status = "Connecting";
                                    connectionWindow.networkWorkerDisconnected = false;
                                    state = ClientState.CONNECTING;
                                    addressToConnectTo.Add(new IPEndPoint(testAddress, port));
                                }
                            }
                            if (addressToConnectTo.Count > 0)
                            {
                                darkNetwork.SetupClient(addressToConnectTo.ToArray(), handler);
                            }
                            else
                            {
                                DarkLog.Debug("DNS does not contain a valid address entry");
                                connectionWindow.status = "DNS does not contain a valid address entry";
                                return;
                            }
                        }
                        else
                        {
                            DarkLog.Debug("Address is not a IP or DNS name");
                            connectionWindow.status = "Address is not a IP or DNS name";
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("DNS Error: " + e.ToString());
                        connectionWindow.status = "DNS Error: " + e.Message;
                        return;
                    }
                }
                else
                {
                    connectionWindow.status = "Connecting";
                    connectionWindow.networkWorkerDisconnected = false;
                    state = ClientState.CONNECTING;
                    darkNetwork.SetupClient(new IPEndPoint(destinationAddress, port), handler);
                }
            }

            while (state == ClientState.CONNECTING)
            {
                Thread.Sleep(500);
                CheckInitialDisconnection();
            }
        }

        private void CheckInitialDisconnection()
        {
            if (state == ClientState.CONNECTING)
            {
                if ((DateTime.UtcNow.Ticks - lastConnectTime) > (Common.INITIAL_CONNECTION_TIMEOUT * TimeSpan.TicksPerMillisecond))
                {
                    Disconnect("Failed to connect!");
                    connectionWindow.status = "Failed to connect - no reply";
                }
            }
        }

        public void Disconnect(string reason)
        {
            if (state != ClientState.DISCONNECTED)
            {
                state = ClientState.DISCONNECTED;

                DarkLog.Debug("Disconnected, reason: " + reason);
                if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                {
                    //SendDisconnect("Force quit to main menu");
                    dmpGame.forceQuit = true;
                }
                else
                {
                    Client.displayDisconnectMessage = true;
                }
                connectionWindow.status = reason;
                connectionWindow.networkWorkerDisconnected = true;
                state = ClientState.DISCONNECTED;
                darkNetwork.Shutdown();
                /*
                    if (meshClient != null)
                    {
                        meshClient.Shutdown();
                    }
                    terminateThreadsOnNextUpdate = true;
                    */
            }
        }

        /*
        private void HandleMeshSetPlayer(byte[] inputData, int inputDataLength, Guid clientGuid, IPEndPoint endPoint)
        {
            string fromPlayer = System.Text.Encoding.UTF8.GetString(inputData, 24, inputDataLength - 24);
            meshPlayerGuids[fromPlayer] = clientGuid;
        }

        private void HandleMeshVesselUpdate(byte[] inputData, int inputDataLength, Guid clientGuid, IPEndPoint endPoint)
        {
            ByteArray tempArray = ByteRecycler.GetObject(inputDataLength);
            Array.Copy(inputData, 0, tempArray.data, 0, inputDataLength);
            HandleVesselUpdate(tempArray, true);
            ByteRecycler.ReleaseObject(tempArray);
        }

        public string GetMeshPlayername(Guid peerGuid)
        {
            foreach (KeyValuePair<string, Guid> kvp in meshPlayerGuids)
            {
                if (kvp.Value == peerGuid)
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        public void SendMeshSetPlayer()
        {
            if (state >= ClientState.CONNECTED)
            {
                if ((Common.GetCurrentUnixTime() - lastSendSetPlayer) > 5)
                {
                    lastSendSetPlayer = Common.GetCurrentUnixTime();
                    foreach (UdpPeer peer in meshClient.GetPeers())
                    {
                        if (peer.guid != UdpMeshCommon.GetMeshAddress())
                        {
                            meshClient.SendMessageToClient(peer.guid, (int)MeshMessageType.SET_PLAYER, System.Text.Encoding.UTF8.GetBytes(dmpSettings.playerName));
                        }
                    }
                }
            }
        }
        */

        private void QueueOutgoingMessage(NetworkMessage message, bool highPriority)
        {
            handler.SendMessage(message);
        }

        private void HandleHandshakeChallange(ByteArray messageData, Connection<ClientObject> connection)
        {
            if (serverConnection == null)
            {
                serverConnection = connection;
            }
            if (serverConnection != connection)
            {
                return;
            }
            try
            {
                using (MessageReader mr = new MessageReader(messageData.data))
                {
                    byte[] challange = mr.Read<byte[]>();
                    using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(1024))
                    {
                        rsa.PersistKeyInCsp = false;
                        rsa.FromXmlString(dmpSettings.playerPrivateKey);
                        byte[] signature = rsa.SignData(challange, CryptoConfig.CreateFromName("SHA256"));
                        SendHandshakeResponse(signature);
                        state = ClientState.HANDSHAKING;
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling HANDSHAKE_CHALLANGE message, exception: " + e);
            }
        }

        private void HandleHandshakeReply(ByteArray messageData, Connection<ClientObject> connection)
        {
            if (serverConnection != connection)
            {
                return;
            }
            int reply = 0;
            string reason = "";
            string modFileData = "";
            int serverProtocolVersion = -1;
            string serverVersion = "Unknown";
            try
            {
                using (MessageReader mr = new MessageReader(messageData.data))
                {
                    reply = mr.Read<int>();
                    reason = mr.Read<string>();
                    try
                    {
                        serverProtocolVersion = mr.Read<int>();
                        serverVersion = mr.Read<string>();
                    }
                    catch
                    {
                        //We don't care about this throw on pre-protocol-9 servers.
                    }
                    //If we handshook successfully, the mod data will be available to read.
                    if (reply == 0)
                    {
                        modWorker.modControl = (ModControlMode)mr.Read<int>();
                        if (modWorker.modControl != ModControlMode.DISABLED)
                        {
                            modFileData = mr.Read<string>();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling HANDSHAKE_REPLY message, exception: " + e);
                reply = 99;
                reason = "Incompatible HANDSHAKE_REPLY message";
            }
            switch (reply)
            {
                case 0:
                    {
                        if (modWorker.ParseModFile(modFileData))
                        {
                            DarkLog.Debug("Handshake successful");
                            state = ClientState.AUTHENTICATED;
                        }
                        else
                        {
                            DarkLog.Debug("Failed to pass mod validation");
                            state = ClientState.AUTHENTICATED;
                            disconnectAfterDownloadingMods = true;
                        }
                    }
                    break;
                default:
                    string disconnectReason = "Handshake failure: " + reason;
                    //If it's a protocol mismatch, append the client/server version.
                    if (reply == 1)
                    {
                        string clientTrimmedVersion = Common.PROGRAM_VERSION;
                        //Trim git tags
                        if (Common.PROGRAM_VERSION.Length == 40)
                        {
                            clientTrimmedVersion = Common.PROGRAM_VERSION.Substring(0, 7);
                        }
                        string serverTrimmedVersion = serverVersion;
                        if (serverVersion.Length == 40)
                        {
                            serverTrimmedVersion = serverVersion.Substring(0, 7);
                        }
                        disconnectReason += "\nClient: " + clientTrimmedVersion + ", Server: " + serverTrimmedVersion;
                        //If they both aren't a release version, display the actual protocol version.
                        if (!serverVersion.Contains("v") || !Common.PROGRAM_VERSION.Contains("v"))
                        {
                            if (serverProtocolVersion != -1)
                            {
                                disconnectReason += "\nClient protocol: " + Common.PROTOCOL_VERSION + ", Server: " + serverProtocolVersion;
                            }
                            else
                            {
                                disconnectReason += "\nClient protocol: " + Common.PROTOCOL_VERSION + ", Server: 8-";
                            }
                        }
                    }
                    DarkLog.Debug(disconnectReason);
                    Disconnect(disconnectReason);
                    break;
            }
        }

        private void HandleChatMessage(ByteArray messageData, Connection<ClientObject> connection)
        {
            if (serverConnection != connection)
            {
                return;
            }
            using (MessageReader mr = new MessageReader(messageData.data))
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
                                    chatWorker.QueueChatJoin(playerName, channelName);
                                }
                            }
                        }
                        break;
                    case ChatMessageType.JOIN:
                        {
                            string playerName = mr.Read<string>();
                            string channelName = mr.Read<string>();
                            chatWorker.QueueChatJoin(playerName, channelName);
                        }
                        break;
                    case ChatMessageType.LEAVE:
                        {
                            string playerName = mr.Read<string>();
                            string channelName = mr.Read<string>();
                            chatWorker.QueueChatLeave(playerName, channelName);
                        }
                        break;
                    case ChatMessageType.CHANNEL_MESSAGE:
                        {
                            string playerName = mr.Read<string>();
                            string channelName = mr.Read<string>();
                            string channelMessage = mr.Read<string>();
                            chatWorker.QueueChannelMessage(playerName, channelName, channelMessage);
                        }
                        break;
                    case ChatMessageType.PRIVATE_MESSAGE:
                        {
                            string fromPlayer = mr.Read<string>();
                            string toPlayer = mr.Read<string>();
                            string privateMessage = mr.Read<string>();
                            if (toPlayer == dmpSettings.playerName || fromPlayer == dmpSettings.playerName)
                            {
                                chatWorker.QueuePrivateMessage(fromPlayer, toPlayer, privateMessage);
                            }
                        }
                        break;
                    case ChatMessageType.CONSOLE_MESSAGE:
                        {
                            string message = mr.Read<string>();
                            chatWorker.QueueSystemMessage(message);
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private void HandleServerSettings(ByteArray messageData, Connection<ClientObject> connection)
        {
            if (serverConnection != connection)
            {
                return;
            }
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                warpWorker.warpMode = (WarpMode)mr.Read<int>();
                timeSyncer.isSubspace = warpWorker.warpMode == WarpMode.SUBSPACE;
                dmpGame.gameMode = (GameMode)mr.Read<int>();
                dmpGame.serverAllowCheats = mr.Read<bool>();
                numberOfKerbals = mr.Read<int>();
                numberOfVessels = mr.Read<int>();
                screenshotWorker.screenshotHeight = mr.Read<int>();
                asteroidWorker.maxNumberOfUntrackedAsteroids = mr.Read<int>();
                chatWorker.consoleIdentifier = mr.Read<string>();
                dmpGame.serverDifficulty = (GameDifficulty)mr.Read<int>();
                vesselWorker.safetyBubbleDistance = mr.Read<float>();
                if (dmpGame.serverDifficulty != GameDifficulty.CUSTOM)
                {
                    dmpGame.serverParameters = GameParameters.GetDefaultParameters(Client.ConvertGameMode(dmpGame.gameMode), (GameParameters.Preset)dmpGame.serverDifficulty);
                }
                else
                {
                    GameParameters newParameters = new GameParameters();
                    //TODO: Ask RockyTV what these do?
                    //GameParameters.AdvancedParams newAdvancedParameters = new GameParameters.AdvancedParams();
                    //CommNet.CommNetParams newCommNetParameters = new CommNet.CommNetParams();
                    newParameters.Difficulty.AllowStockVessels = mr.Read<bool>();
                    newParameters.Difficulty.AutoHireCrews = mr.Read<bool>();
                    newParameters.Difficulty.BypassEntryPurchaseAfterResearch = mr.Read<bool>();
                    newParameters.Difficulty.IndestructibleFacilities = mr.Read<bool>();
                    newParameters.Difficulty.MissingCrewsRespawn = mr.Read<bool>();
                    newParameters.Difficulty.ReentryHeatScale = mr.Read<float>();
                    newParameters.Difficulty.ResourceAbundance = mr.Read<float>();
                    newParameters.Flight.CanQuickLoad = newParameters.Flight.CanRestart = newParameters.Flight.CanLeaveToEditor = mr.Read<bool>();
                    newParameters.Career.FundsGainMultiplier = mr.Read<float>();
                    newParameters.Career.FundsLossMultiplier = mr.Read<float>();
                    newParameters.Career.RepGainMultiplier = mr.Read<float>();
                    newParameters.Career.RepLossMultiplier = mr.Read<float>();
                    newParameters.Career.RepLossDeclined = mr.Read<float>();
                    newParameters.Career.ScienceGainMultiplier = mr.Read<float>();
                    newParameters.Career.StartingFunds = mr.Read<float>();
                    newParameters.Career.StartingReputation = mr.Read<float>();
                    newParameters.Career.StartingScience = mr.Read<float>();
                    //New KSP 1.2 Settings
                    newParameters.Difficulty.RespawnTimer = mr.Read<float>();
                    newParameters.Difficulty.EnableCommNet = mr.Read<bool>();
                    newParameters.CustomParams<GameParameters.AdvancedParams>().EnableKerbalExperience = mr.Read<bool>();
                    newParameters.CustomParams<GameParameters.AdvancedParams>().ImmediateLevelUp = mr.Read<bool>();
                    newParameters.CustomParams<GameParameters.AdvancedParams>().AllowNegativeCurrency = mr.Read<bool>();
                    newParameters.CustomParams<GameParameters.AdvancedParams>().ResourceTransferObeyCrossfeed = mr.Read<bool>();
                    newParameters.CustomParams<GameParameters.AdvancedParams>().BuildingImpactDamageMult = mr.Read<float>();
                    newParameters.CustomParams<GameParameters.AdvancedParams>().PartUpgradesInCareer = newParameters.CustomParams<GameParameters.AdvancedParams>().PartUpgradesInSandbox = mr.Read<bool>();
                    newParameters.CustomParams<GameParameters.AdvancedParams>().PressurePartLimits = newParameters.CustomParams<GameParameters.AdvancedParams>().GPartLimits = newParameters.CustomParams<GameParameters.AdvancedParams>().GKerbalLimits = mr.Read<bool>();
                    newParameters.CustomParams<GameParameters.AdvancedParams>().KerbalGToleranceMult = mr.Read<float>();
                    newParameters.CustomParams<CommNet.CommNetParams>().requireSignalForControl = mr.Read<bool>();
                    newParameters.CustomParams<CommNet.CommNetParams>().plasmaBlackout = mr.Read<bool>();
                    newParameters.CustomParams<CommNet.CommNetParams>().rangeModifier = mr.Read<float>();
                    newParameters.CustomParams<CommNet.CommNetParams>().DSNModifier = mr.Read<float>();
                    newParameters.CustomParams<CommNet.CommNetParams>().occlusionMultiplierVac = mr.Read<float>();
                    newParameters.CustomParams<CommNet.CommNetParams>().occlusionMultiplierAtm = mr.Read<float>();
                    newParameters.CustomParams<CommNet.CommNetParams>().enableGroundStations = mr.Read<bool>();

                    dmpGame.serverParameters = newParameters;
                }
            }
        }

        private void HandlePlayerStatus(ByteArray messageData, Connection<ClientObject> connection)
        {
            if (serverConnection != connection)
            {
                return;
            }
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                string playerName = mr.Read<string>();
                string vesselText = mr.Read<string>();
                string statusText = mr.Read<string>();
                PlayerStatus newStatus = new PlayerStatus();
                newStatus.playerName = playerName;
                newStatus.vesselText = vesselText;
                newStatus.statusText = statusText;
                playerStatusWorker.AddPlayerStatus(newStatus);
            }
        }

        private void HandlePlayerJoin(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                string playerName = mr.Read<string>();
                chatWorker.QueueChannelMessage(chatWorker.consoleIdentifier, "", playerName + " has joined the server");
            }
        }

        private void HandlePlayerDisconnect(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                string playerName = mr.Read<string>();
                warpWorker.RemovePlayer(playerName);
                playerStatusWorker.RemovePlayerStatus(playerName);
                chatWorker.QueueRemovePlayer(playerName);
                lockSystem.ReleasePlayerLocks(playerName);
                chatWorker.QueueChannelMessage(chatWorker.consoleIdentifier, "", playerName + " has left the server");
            }
        }

        private void HandleSyncTimeReply(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                long clientSend = mr.Read<long>();
                long serverReceive = mr.Read<long>();
                timeSyncer.HandleSyncTime(clientSend, serverReceive);
            }
        }

        private void HandleScenarioModuleData(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                string[] scenarioName = mr.Read<string[]>();
                for (int i = 0; i < scenarioName.Length; i++)
                {
                    ByteArray compressedData = mr.Read<ByteArray>();
                    if (compressedData.Length == 1)
                    {
                        DarkLog.Debug("Scenario data is empty for " + scenarioName[i]);
                        ByteRecycler.ReleaseObject(compressedData);
                    }
                    ByteArray scenarioData = Compression.DecompressIfNeeded(compressedData);
                    ConfigNode scenarioNode = configNodeSerializer.Deserialize(scenarioData);
                    ByteRecycler.ReleaseObject(scenarioData);
                    ByteRecycler.ReleaseObject(compressedData);
                    if (scenarioNode != null)
                    {
                        scenarioWorker.QueueScenarioData(scenarioName[i], scenarioNode);
                    }
                    else
                    {
                        DarkLog.Debug("Scenario data has been lost for " + scenarioName[i]);
                        ScreenMessages.PostScreenMessage("Scenario data has been lost for " + scenarioName[i], 5f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
            }
        }

        private void HandleKerbalReply(ByteArray messageData, Connection<ClientObject> connection)
        {
            numberOfKerbalsReceived++;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                double planetTime = mr.Read<double>();
                string kerbalName = mr.Read<string>();
                byte[] kerbalData = mr.Read<byte[]>();
                ConfigNode kerbalNode = null;
                bool dataOK = false;
                for (int i = 0; i < kerbalData.Length; i++)
                {
                    //Apparently we have to defend against all NULL files now?
                    if (kerbalData[i] != 0)
                    {
                        dataOK = true;
                        break;
                    }
                }
                if (dataOK)
                {
                    kerbalNode = configNodeSerializer.Deserialize(kerbalData);
                }
                if (kerbalNode != null)
                {
                    vesselWorker.QueueKerbal(planetTime, kerbalName, kerbalNode);
                }
                else
                {
                    DarkLog.Debug("Failed to load kerbal!");
                    chatWorker.PMMessageServer("WARNING: Kerbal " + kerbalName + " is DAMAGED!. Skipping load.");
                }
            }
            if (state == ClientState.SYNCING_KERBALS)
            {
                if (numberOfKerbals != 0)
                {
                    connectionWindow.status = "Syncing kerbals " + numberOfKerbalsReceived + "/" + numberOfKerbals + " (" + (int)((numberOfKerbalsReceived / (float)numberOfKerbals) * 100) + "%)";
                }
            }
        }

        private void HandleKerbalComplete(ByteArray messageData, Connection<ClientObject> connection)
        {
            state = ClientState.KERBALS_SYNCED;
            DarkLog.Debug("Kerbals Synced!");
            connectionWindow.status = "Kerbals synced";
        }

        private void HandleKerbalRemove(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                double planetTime = mr.Read<double>();
                string kerbalName = mr.Read<string>();
                DarkLog.Debug("Kerbal removed: " + kerbalName);
                ScreenMessages.PostScreenMessage("Kerbal " + kerbalName + " removed from game at " + planetTime, 5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void HandleVesselList(ByteArray messageData, Connection<ClientObject> connection)
        {
            state = ClientState.SYNCING_VESSELS;
            connectionWindow.status = "Syncing vessels";
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                List<string> serverVessels = new List<string>(mr.Read<string[]>());
                List<string> cacheObjects = new List<string>(universeSyncCache.GetCachedObjects());
                List<string> requestedObjects = new List<string>();
                foreach (string serverVessel in serverVessels)
                {
                    if (!cacheObjects.Contains(serverVessel))
                    {
                        requestedObjects.Add(serverVessel);
                    }
                    else
                    {
                        bool added = false;
                        byte[] vesselBytes = universeSyncCache.GetFromCache(serverVessel);
                        if (vesselBytes.Length != 0)
                        {
                            ConfigNode vesselNode = configNodeSerializer.Deserialize(vesselBytes);
                            if (vesselNode != null)
                            {
                                string vesselIDString = Common.ConvertConfigStringToGUIDString(vesselNode.GetValue("pid"));
                                if (vesselIDString != null)
                                {
                                    Guid vesselID = new Guid(vesselIDString);
                                    if (vesselID != Guid.Empty)
                                    {
                                        vesselWorker.QueueVesselProto(vesselID, 0, vesselNode);
                                        added = true;
                                        numberOfVesselsReceived++;
                                    }
                                    else
                                    {
                                        DarkLog.Debug("Cached object " + serverVessel + " is damaged - Returned GUID.Empty");
                                    }
                                }
                                else
                                {
                                    DarkLog.Debug("Cached object " + serverVessel + " is damaged - Failed to get vessel ID");
                                }
                            }
                            else
                            {
                                DarkLog.Debug("Cached object " + serverVessel + " is damaged - Failed to create a config node");
                            }
                        }
                        else
                        {
                            DarkLog.Debug("Cached object " + serverVessel + " is damaged - Object is a 0 length file!");
                        }
                        if (!added)
                        {
                            requestedObjects.Add(serverVessel);
                        }
                    }
                }
                if (numberOfVessels != 0)
                {
                    connectionWindow.status = "Syncing vessels " + numberOfVesselsReceived + "/" + numberOfVessels + " (" + (int)((numberOfVesselsReceived / (float)numberOfVessels) * 100) + "%)";
                }
                SendVesselsRequest(requestedObjects.ToArray());
            }
        }

        private void HandleVesselProto(ByteArray messageData, Connection<ClientObject> connection)
        {
            numberOfVesselsReceived++;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                double planetTime = mr.Read<double>();
                string vesselID = mr.Read<string>();
                //Docking - don't care.
                mr.Read<bool>();
                //Flying - don't care.
                mr.Read<bool>();
                ByteArray compressedData = mr.Read<ByteArray>();
                if (compressedData.Length == 1)
                {
                    ByteRecycler.ReleaseObject(compressedData);
                    Console.WriteLine(vesselID + " is an empty vessel file, skipping");
                    return;
                }
                ByteArray vesselData = Compression.DecompressIfNeeded(compressedData);
                ByteRecycler.ReleaseObject(compressedData);
                bool dataOK = false;
                for (int i = 0; i < vesselData.Length; i++)
                {
                    //Apparently we have to defend against all NULL files now?
                    if (vesselData.data[i] != 0)
                    {
                        dataOK = true;
                        break;
                    }
                }
                ConfigNode vesselNode = null;
                if (dataOK)
                {
                    vesselNode = configNodeSerializer.Deserialize(vesselData);
                }
                if (vesselNode != null)
                {
                    string configGuid = vesselNode.GetValue("pid");
                    if (!String.IsNullOrEmpty(configGuid) && vesselID == Common.ConvertConfigStringToGUIDString(configGuid))
                    {
                        universeSyncCache.QueueToCache(vesselData);
                        vesselWorker.QueueVesselProto(new Guid(vesselID), planetTime, vesselNode);
                    }
                    else
                    {
                        DarkLog.Debug("Failed to load vessel " + vesselID + "!");
                        chatWorker.PMMessageServer("WARNING: Vessel " + vesselID + " is DAMAGED!. Skipping load.");
                    }
                }
                else
                {
                    DarkLog.Debug("Failed to load vessel" + vesselID + "!");
                    chatWorker.PMMessageServer("WARNING: Vessel " + vesselID + " is DAMAGED!. Skipping load.");
                }
                ByteRecycler.ReleaseObject(vesselData);
            }
            if (state == ClientState.SYNCING_VESSELS)
            {
                if (numberOfVessels != 0)
                {
                    if (numberOfVesselsReceived > numberOfVessels)
                    {
                        //Received 102 / 101 vessels!
                        numberOfVessels = numberOfVesselsReceived;
                    }
                    connectionWindow.status = "Syncing vessels " + numberOfVesselsReceived + "/" + numberOfVessels + " (" + (int)((numberOfVesselsReceived / (float)numberOfVessels) * 100) + "%)";
                }
            }
        }

        public VesselUpdate VeselUpdateFromBytes(byte[] messageData)
        {
            VesselUpdate update = Recycler<VesselUpdate>.GetObject();
            update.SetVesselWorker(vesselWorker);
            using (MessageReader mr = new MessageReader(messageData))
            {
                update.planetTime = mr.Read<double>();
                update.vesselID = new Guid(mr.Read<string>());
                update.bodyName = mr.Read<string>();
                update.rotation = mr.Read<float[]>();
                update.angularVelocity = mr.Read<float[]>();
                //FlightState variables
                update.flightState = new FlightCtrlState();
                update.flightState.mainThrottle = mr.Read<float>();
                update.flightState.wheelThrottleTrim = mr.Read<float>();
                update.flightState.X = mr.Read<float>();
                update.flightState.Y = mr.Read<float>();
                update.flightState.Z = mr.Read<float>();
                update.flightState.killRot = mr.Read<bool>();
                update.flightState.gearUp = mr.Read<bool>();
                update.flightState.gearDown = mr.Read<bool>();
                update.flightState.headlight = mr.Read<bool>();
                update.flightState.wheelThrottle = mr.Read<float>();
                update.flightState.roll = mr.Read<float>();
                update.flightState.yaw = mr.Read<float>();
                update.flightState.pitch = mr.Read<float>();
                update.flightState.rollTrim = mr.Read<float>();
                update.flightState.yawTrim = mr.Read<float>();
                update.flightState.pitchTrim = mr.Read<float>();
                update.flightState.wheelSteer = mr.Read<float>();
                update.flightState.wheelSteerTrim = mr.Read<float>();
                //Action group controls
                update.actiongroupControls = mr.Read<bool[]>();
                //Position/velocity
                update.isSurfaceUpdate = mr.Read<bool>();
                if (update.isSurfaceUpdate)
                {
                    update.position = mr.Read<double[]>();
                    update.velocity = mr.Read<double[]>();
                    update.acceleration = mr.Read<double[]>();
                    update.terrainNormal = mr.Read<float[]>();
                }
                else
                {
                    update.orbit = mr.Read<double[]>();
                }
                update.sasEnabled = mr.Read<bool>();
                if (update.sasEnabled)
                {
                    update.autopilotMode = mr.Read<int>();
                    update.lockedRotation = mr.Read<float[]>();
                }
                return update;
            }
        }

        private void HandleVesselUpdate(ByteArray messageData, Connection<ClientObject> connection)
        {
            bool fromMesh = IsMeshConnection(connection);
            VesselUpdate update = VeselUpdateFromBytes(messageData.data);
            vesselWorker.QueueVesselUpdate(update, fromMesh);
        }

        private void HandleSetActiveVessel(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                string player = mr.Read<string>();
                Guid vesselID = new Guid(mr.Read<string>());
                vesselWorker.QueueActiveVessel(player, vesselID);
            }
        }

        private void HandleVesselComplete(ByteArray messageData, Connection<ClientObject> connection)
        {
            state = ClientState.VESSELS_SYNCED;
        }

        private void HandleVesselRemove(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                double planetTime = mr.Read<double>();
                Guid vesselID = new Guid(mr.Read<string>());
                bool isDockingUpdate = mr.Read<bool>();
                string dockingPlayer = null;
                if (isDockingUpdate)
                {
                    DarkLog.Debug("Got a docking update!");
                    dockingPlayer = mr.Read<string>();
                }
                else
                {
                    DarkLog.Debug("Got removal command for vessel " + vesselID);
                }
                vesselWorker.QueueVesselRemove(vesselID, planetTime, isDockingUpdate, dockingPlayer);
            }
        }

        private void HandleCraftLibrary(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
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
                                DarkLog.Debug("Player: " + player + ", VAB: " + vabExists + ", SPH: " + sphExists + ", SUBASSEMBLY: " + subassemblyExists);
                                if (vabExists)
                                {
                                    string[] vabCrafts = mr.Read<string[]>();
                                    foreach (string vabCraft in vabCrafts)
                                    {
                                        CraftChangeEntry cce = new CraftChangeEntry();
                                        cce.playerName = player;
                                        cce.craftType = CraftType.VAB;
                                        cce.craftName = vabCraft;
                                        craftLibraryWorker.QueueCraftAdd(cce);
                                    }
                                }
                                if (sphExists)
                                {
                                    string[] sphCrafts = mr.Read<string[]>();
                                    foreach (string sphCraft in sphCrafts)
                                    {
                                        CraftChangeEntry cce = new CraftChangeEntry();
                                        cce.playerName = player;
                                        cce.craftType = CraftType.SPH;
                                        cce.craftName = sphCraft;
                                        craftLibraryWorker.QueueCraftAdd(cce);
                                    }
                                }
                                if (subassemblyExists)
                                {
                                    string[] subassemblyCrafts = mr.Read<string[]>();
                                    foreach (string subassemblyCraft in subassemblyCrafts)
                                    {
                                        CraftChangeEntry cce = new CraftChangeEntry();
                                        cce.playerName = player;
                                        cce.craftType = CraftType.SUBASSEMBLY;
                                        cce.craftName = subassemblyCraft;
                                        craftLibraryWorker.QueueCraftAdd(cce);
                                    }
                                }
                            }
                        }
                        break;
                    case CraftMessageType.ADD_FILE:
                        {
                            CraftChangeEntry cce = new CraftChangeEntry();
                            cce.playerName = mr.Read<string>();
                            cce.craftType = (CraftType)mr.Read<int>();
                            cce.craftName = mr.Read<string>();
                            craftLibraryWorker.QueueCraftAdd(cce);
                            chatWorker.QueueChannelMessage(chatWorker.consoleIdentifier, "", cce.playerName + " shared " + cce.craftName + " (" + cce.craftType + ")");
                        }
                        break;
                    case CraftMessageType.DELETE_FILE:
                        {
                            CraftChangeEntry cce = new CraftChangeEntry();
                            cce.playerName = mr.Read<string>();
                            cce.craftType = (CraftType)mr.Read<int>();
                            cce.craftName = mr.Read<string>();
                            craftLibraryWorker.QueueCraftDelete(cce);
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
                                craftLibraryWorker.QueueCraftResponse(cre);
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

        private void HandleScreenshotLibrary(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                ScreenshotMessageType messageType = (ScreenshotMessageType)mr.Read<int>();
                switch (messageType)
                {
                    case ScreenshotMessageType.SEND_START_NOTIFY:
                        {
                            string fromPlayer = mr.Read<string>();
                            screenshotWorker.downloadingScreenshotFromPlayer = fromPlayer;
                        }
                        break;
                    case ScreenshotMessageType.NOTIFY:
                        {
                            string fromPlayer = mr.Read<string>();
                            screenshotWorker.QueueNewNotify(fromPlayer);
                        }
                        break;
                    case ScreenshotMessageType.SCREENSHOT:
                        {
                            string fromPlayer = mr.Read<string>();
                            byte[] screenshotData = mr.Read<byte[]>();
                            screenshotWorker.QueueNewScreenshot(fromPlayer, screenshotData);
                        }
                        break;
                    case ScreenshotMessageType.WATCH:
                        {
                            string fromPlayer = mr.Read<string>();
                            string watchPlayer = mr.Read<string>();
                            screenshotWorker.QueueNewScreenshotWatch(fromPlayer, watchPlayer);
                        }
                        break;

                }
            }
        }

        private void HandlePingReply(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                int pingTime = (int)((DateTime.UtcNow.Ticks - mr.Read<long>()) / 10000f);
                chatWorker.QueueChannelMessage(chatWorker.consoleIdentifier, "", "Ping: " + pingTime + "ms.");
            }

        }

        private void HandleMotdReply(ByteArray messageData, Connection<ClientObject> connection)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                serverMotd = mr.Read<string>();
                if (serverMotd != "")
                {
                    displayMotd = true;
                    chatWorker.QueueChannelMessage(chatWorker.consoleIdentifier, "", serverMotd);
                }
            }
        }

        private void HandleGroupMessage(ByteArray messageData, Connection<ClientObject> connection)
        {
            groups.QueueMessage(messageData);
        }

        private void HandlePermissionMessage(ByteArray messageData, Connection<ClientObject> connection)
        {
            permissions.QueueMessage(messageData);
        }

        private void HandleWarpControl(ByteArray messageData, Connection<ClientObject> connection)
        {
            warpWorker.QueueWarpMessage(messageData);
        }

        private void HandleConnectionEnd(ByteArray messageData, Connection<ClientObject> connection)
        {
            string reason = "";
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                reason = mr.Read<string>();
            }
            Disconnect("Server closed connection: " + reason);
        }

        private void HandleModpackData(ByteArray messageData, Connection<ClientObject> connection)
        {
            modpackWorker.HandleModpackMessage(messageData);
        }

        private void SendHandshakeResponse(byte[] signature)
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<int>(Common.PROTOCOL_VERSION);
                mw.Write<string>(dmpSettings.playerName);
                mw.Write<string>(dmpSettings.playerPublicKey);
                mw.Write<byte[]>(signature);
                mw.Write<string>(Common.PROGRAM_VERSION);
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.HANDSHAKE_RESPONSE, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from ChatWindow
        public void SendChatMessage(byte[] messageData)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.CHAT_MESSAGE, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from PlayerStatusWorker
        public void SendPlayerStatus(PlayerStatus playerStatus)
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<string>(playerStatus.playerName);
                mw.Write<string>(playerStatus.vesselText);
                mw.Write<string>(playerStatus.statusText);
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.PLAYER_STATUS, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from PlayerColorWorker
        public void SendPlayerColorMessage(byte[] messageData)
        {
            //TODO: Use recycling here
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.PLAYER_COLOR, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called from timeSyncer
        public void SendTimeSync()
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<long>(DateTime.UtcNow.Ticks);
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.SYNC_TIME_REQUEST, newMessageLength, NetworkMessageType.UNORDERED_UNRELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }

        private void SendGroupsRequest()
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<int>((int)GroupMessageType.GROUP_REQUEST);
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.GROUP, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }

        private void SendPermissionsRequest()
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<int>((int)PermissionMessageType.PERMISSION_REQUEST);
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.PERMISSION, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }

        private void SendKerbalsRequest()
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.KERBALS_REQUEST, 0, NetworkMessageType.ORDERED_RELIABLE);
            QueueOutgoingMessage(newMessage, true);
        }

        private void SendVesselsRequest(string[] requestList)
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<string[]>(requestList);
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.VESSELS_REQUEST, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }

        private bool VesselHasNaNPosition(ProtoVessel pv)
        {
            if (pv.landed || pv.splashed)
            {
                //Ground checks
                if (double.IsNaN(pv.latitude) || double.IsInfinity(pv.latitude))
                {
                    return true;
                }
                if (double.IsNaN(pv.longitude) || double.IsInfinity(pv.longitude))
                {
                    return true;
                }
                if (double.IsNaN(pv.altitude) || double.IsInfinity(pv.altitude))
                {
                    return true;
                }
            }
            else
            {
                //Orbit checks
                if (double.IsNaN(pv.orbitSnapShot.argOfPeriapsis) || double.IsInfinity(pv.orbitSnapShot.argOfPeriapsis))
                {
                    return true;
                }
                if (double.IsNaN(pv.orbitSnapShot.eccentricity) || double.IsInfinity(pv.orbitSnapShot.eccentricity))
                {
                    return true;
                }
                if (double.IsNaN(pv.orbitSnapShot.epoch) || double.IsInfinity(pv.orbitSnapShot.epoch))
                {
                    return true;
                }
                if (double.IsNaN(pv.orbitSnapShot.inclination) || double.IsInfinity(pv.orbitSnapShot.inclination))
                {
                    return true;
                }
                if (double.IsNaN(pv.orbitSnapShot.LAN) || double.IsInfinity(pv.orbitSnapShot.LAN))
                {
                    return true;
                }
                if (double.IsNaN(pv.orbitSnapShot.meanAnomalyAtEpoch) || double.IsInfinity(pv.orbitSnapShot.meanAnomalyAtEpoch))
                {
                    return true;
                }
                if (double.IsNaN(pv.orbitSnapShot.semiMajorAxis) || double.IsInfinity(pv.orbitSnapShot.semiMajorAxis))
                {
                    return true;
                }
            }

            return false;
        }

        //Called from vesselWorker
        public void SendVesselProtoMessage(ProtoVessel vessel, bool isDockingUpdate, bool isFlyingUpdate)
        {
            //Defend against NaN orbits
            if (VesselHasNaNPosition(vessel))
            {
                DarkLog.Debug("Vessel " + vessel.vesselID + " has NaN position");
                return;
            }

            // Handle contract vessels
            bool isContractVessel = false;
            foreach (ProtoPartSnapshot pps in vessel.protoPartSnapshots)
            {
                foreach (ProtoCrewMember pcm in pps.protoModuleCrew.ToArray())
                {
                    if (pcm.type == ProtoCrewMember.KerbalType.Tourist || pcm.type == ProtoCrewMember.KerbalType.Unowned)
                    {
                        isContractVessel = true;
                    }
                }
            }
            if (!asteroidWorker.VesselIsAsteroid(vessel) && (DiscoveryLevels)int.Parse(vessel.discoveryInfo.GetValue("state")) != DiscoveryLevels.Owned)
            {
                isContractVessel = true;
            }

            ConfigNode vesselNode = new ConfigNode();
            vessel.Save(vesselNode);
            if (isContractVessel)
            {
                ConfigNode dmpNode = new ConfigNode();
                dmpNode.AddValue("contractOwner", dmpSettings.playerPublicKey);
                vesselNode.AddNode("DarkMultiPlayer", dmpNode);
            }

            ByteArray vesselBytes = configNodeSerializer.Serialize(vesselNode);
            bool dataOK = false;
            if (vesselBytes.Length > 0)
            {
                for (int i = 0; i < vesselBytes.Length; i++)
                {
                    if (vesselBytes.data[i] != 0)
                    {
                        dataOK = true;
                        break;
                    }
                }
            }
            if (vesselBytes.Length == 0)
            {
                DarkLog.Debug("Error generating protovessel from this confignode: " + vesselNode);
            }
            if (vesselBytes != null && vesselBytes.Length > 0 && dataOK)
            {
                universeSyncCache.QueueToCache(vesselBytes);
                int newMessageLength = 0;
                using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
                {
                    mw.Write<double>(Planetarium.GetUniversalTime());
                    mw.Write<string>(vessel.vesselID.ToString());
                    mw.Write<bool>(isDockingUpdate);
                    mw.Write<bool>(isFlyingUpdate);
                    ByteArray compressBytes = Compression.CompressIfNeeded(vesselBytes);
                    mw.Write<ByteArray>(compressBytes);
                    ByteRecycler.ReleaseObject(compressBytes);
                    newMessageLength = (int)mw.GetMessageLength();
                }
                NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.VESSEL_PROTO, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
                Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
                DarkLog.Debug("Sending vessel " + vessel.vesselID + ", name " + vessel.vesselName + ", type: " + vessel.vesselType + ", size: " + newMessage.data.Length);
                QueueOutgoingMessage(newMessage, false);
                vesselRecorder.RecordSend(newMessage.data, ClientMessageType.VESSEL_PROTO, vessel.vesselID);
                ByteRecycler.ReleaseObject(vesselBytes);
            }
            else
            {
                DarkLog.Debug("Failed to create byte[] data for " + vessel.vesselID);
            }
        }

        public NetworkMessage GetVesselUpdateMessage(VesselUpdate update)
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<double>(update.planetTime);
                mw.Write<string>(update.vesselID.ToString());
                mw.Write<string>(update.bodyName);
                mw.Write<float[]>(update.rotation);
                //mw.Write<float[]>(update.vesselForward);
                //mw.Write<float[]>(update.vesselUp);
                mw.Write<float[]>(update.angularVelocity);
                //FlightState variables
                mw.Write<float>(update.flightState.mainThrottle);
                mw.Write<float>(update.flightState.wheelThrottleTrim);
                mw.Write<float>(update.flightState.X);
                mw.Write<float>(update.flightState.Y);
                mw.Write<float>(update.flightState.Z);
                mw.Write<bool>(update.flightState.killRot);
                mw.Write<bool>(update.flightState.gearUp);
                mw.Write<bool>(update.flightState.gearDown);
                mw.Write<bool>(update.flightState.headlight);
                mw.Write<float>(update.flightState.wheelThrottle);
                mw.Write<float>(update.flightState.roll);
                mw.Write<float>(update.flightState.yaw);
                mw.Write<float>(update.flightState.pitch);
                mw.Write<float>(update.flightState.rollTrim);
                mw.Write<float>(update.flightState.yawTrim);
                mw.Write<float>(update.flightState.pitchTrim);
                mw.Write<float>(update.flightState.wheelSteer);
                mw.Write<float>(update.flightState.wheelSteerTrim);
                //Action group controls
                mw.Write<bool[]>(update.actiongroupControls);
                //Position/velocity
                mw.Write<bool>(update.isSurfaceUpdate);
                if (update.isSurfaceUpdate)
                {
                    mw.Write<double[]>(update.position);
                    mw.Write<double[]>(update.velocity);
                    mw.Write<double[]>(update.acceleration);
                    mw.Write<float[]>(update.terrainNormal);
                }
                else
                {
                    mw.Write<double[]>(update.orbit);
                }
                mw.Write<bool>(update.sasEnabled);
                if (update.sasEnabled)
                {
                    mw.Write<int>(update.autopilotMode);
                    mw.Write<float[]>(update.lockedRotation);
                }
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.VESSEL_UPDATE, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            return newMessage;
        }
        //Called from vesselWorker
        public void SendVesselUpdate(VesselUpdate update)
        {
            NetworkMessage newMessage = GetVesselUpdateMessage(update);
            QueueOutgoingMessage(newMessage, false);
            vesselRecorder.RecordSend(newMessage.data, ClientMessageType.VESSEL_UPDATE, update.vesselID);
        }
        //Called from vesselWorker
        /*
        public void SendVesselUpdateMesh(VesselUpdate update, List<string> clientsInSubspace)
        {
            ClientMessage newMessage = GetVesselUpdateMessage(update);
            foreach (string playerName in clientsInSubspace)
            {
                if (meshPlayerGuids.ContainsKey(playerName) && playerName != dmpSettings.playerName)
                {
                    meshClient.SendMessageToClient(meshPlayerGuids[playerName], (int)MeshMessageType.VESSEL_UPDATE, newMessage.data.data, newMessage.data.Length);
                }
            }
            ByteRecycler.ReleaseObject(newMessage.data);
        }
        */
        //Called from vesselWorker
        public void SendVesselRemove(Guid vesselID, bool isDockingUpdate)
        {
            if (!permissions.PlayerHasVesselPermission(dmpSettings.playerName, vesselID))
            {
                return;
            }
            DarkLog.Debug("Removing " + vesselID + " from the server");
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<double>(Planetarium.GetUniversalTime());
                mw.Write<string>(vesselID.ToString());
                mw.Write<bool>(isDockingUpdate);
                if (isDockingUpdate)
                {
                    mw.Write<string>(dmpSettings.playerName);
                }
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.VESSEL_REMOVE, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, false);
            vesselRecorder.RecordSend(newMessage.data, ClientMessageType.VESSEL_REMOVE, vesselID);
        }

        // Called from VesselWorker
        public void SendKerbalRemove(string kerbalName)
        {
            DarkLog.Debug("Removing kerbal " + kerbalName + " from the server");
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<double>(Planetarium.GetUniversalTime());
                mw.Write<string>(kerbalName);
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.KERBAL_REMOVE, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called fro craftLibraryWorker
        public void SendCraftLibraryMessage(byte[] messageData)
        {
            //TODO: Use recycling here
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.CRAFT_LIBRARY, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called from ScreenshotWorker
        public void SendScreenshotMessage(byte[] messageData)
        {
            //TODO: Use recycling here
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.SCREENSHOT_LIBRARY, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called from ScenarioWorker
        public void SendScenarioModuleData(string[] scenarioNames, ByteArray[] scenarioData)
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<string[]>(scenarioNames);
                foreach (ByteArray scenarioBytes in scenarioData)
                {
                    ByteArray compressBytes = Compression.CompressIfNeeded(scenarioBytes);
                    mw.Write<ByteArray>(compressBytes);
                    ByteRecycler.ReleaseObject(compressBytes);
                    ByteRecycler.ReleaseObject(scenarioBytes);
                }
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.SCENARIO_DATA, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            DarkLog.Debug("Sending " + scenarioNames.Length + " scenario modules");
            QueueOutgoingMessage(newMessage, false);
        }
        // Same method as above, only that in this, the message is queued as high priority
        public void SendScenarioModuleDataHighPriority(string[] scenarioNames, ByteArray[] scenarioData)
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<string[]>(scenarioNames);
                foreach (ByteArray scenarioBytes in scenarioData)
                {
                    ByteArray compressBytes = Compression.CompressIfNeeded(scenarioBytes);
                    mw.Write<ByteArray>(compressBytes);
                    ByteRecycler.ReleaseObject(compressBytes);
                    ByteRecycler.ReleaseObject(scenarioBytes);
                }
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.SCENARIO_DATA, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            DarkLog.Debug("Sending " + scenarioNames.Length + " scenario modules (high priority)");
            QueueOutgoingMessage(newMessage, true);
        }

        public void SendKerbalProtoMessage(string kerbalName, ByteArray kerbalBytes)
        {
            bool dataOK = false;
            if (kerbalBytes != null)
            {
                for (int i = 0; i < kerbalBytes.Length; i++)
                {
                    if (kerbalBytes.data[i] != 0)
                    {
                        dataOK = true;
                        break;
                    }
                }
            }
            if (kerbalBytes != null && kerbalBytes.Length > 0 && dataOK)
            {
                int newMessageLength = 0;
                using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
                {
                    mw.Write<double>(Planetarium.GetUniversalTime());
                    mw.Write<string>(kerbalName);
                    mw.Write<ByteArray>(kerbalBytes);
                    newMessageLength = (int)mw.GetMessageLength();
                }
                NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.KERBAL_PROTO, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
                Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
                DarkLog.Debug("Sending kerbal " + kerbalName + ", size: " + newMessage.data.Length);
                QueueOutgoingMessage(newMessage, false);
            }
            else
            {
                DarkLog.Debug("Failed to create byte[] data for kerbal " + kerbalName);
            }
        }
        //Called from chatWorker
        public void SendPingRequest()
        {
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<long>(DateTime.UtcNow.Ticks);
                newMessageLength = (int)mw.GetMessageLength();
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.PING_REQUEST, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from networkWorker
        public void SendMotdRequest()
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.MOTD_REQUEST, 0, NetworkMessageType.ORDERED_RELIABLE);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from FlagSyncer
        public void SendFlagMessage(ByteArray messageData)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.FLAG_SYNC, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called from warpWorker
        public void SendWarpMessage(ByteArray messageData)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.WARP_CONTROL, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from groups
        public void SendGroupMessage(ByteArray messageData)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.GROUP, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from permissions
        public void SendPermissionsMessage(ByteArray messageData)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.PERMISSION, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from lockSystem
        public void SendLockSystemMessage(ByteArray messageData)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.LOCK_SYSTEM, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from warpWorker
        public void SendModpackMessage(ByteArray messageData)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.MODPACK_DATA, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData.data, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, false);
        }

        /// <summary>
        /// If you are a mod, call dmpModInterface.SendModMessage.
        /// </summary>
        public void SendModMessage(byte[] messageData, bool highPriority)
        {
            NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.MOD_DATA, messageData.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(messageData, 0, newMessage.data.data, 0, messageData.Length);
            QueueOutgoingMessage(newMessage, highPriority);
        }
        //Called from main
        public void SendDisconnect(string disconnectReason = "Unknown")
        {
            if (state != ClientState.DISCONNECTING && state >= ClientState.CONNECTED)
            {
                DarkLog.Debug("Sending disconnect message, reason: " + disconnectReason);
                connectionWindow.status = "Disconnected: " + disconnectReason;
                state = ClientState.DISCONNECTING;
                int newMessageLength = 0;
                using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
                {
                    mw.Write<string>(disconnectReason);
                    newMessageLength = (int)mw.GetMessageLength();
                }
                NetworkMessage newMessage = NetworkMessage.Create((int)ClientMessageType.CONNECTION_END, newMessageLength, NetworkMessageType.ORDERED_RELIABLE);
                Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
                QueueOutgoingMessage(newMessage, true);
            }
        }

        public bool IsMeshConnection(Connection<ClientObject> connection)
        {
            return serverConnection != connection;
        }
    }

    class DMPServerAddress
    {
        public string ip;
        public int port;
    }
}

