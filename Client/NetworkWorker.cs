using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using DarkMultiPlayerCommon;
using MessageStream2;
using UDPMeshLib;
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

        private TcpClient clientConnection = null;
        private long lastSendTime = 0;
        private AutoResetEvent sendEvent = new AutoResetEvent(false);
        private Queue<ClientMessage> sendMessageQueueHigh = new Queue<ClientMessage>();
        private Queue<ClientMessage> sendMessageQueueSplit = new Queue<ClientMessage>();
        private Queue<ClientMessage> sendMessageQueueLow = new Queue<ClientMessage>();
        private byte[] messageWriterBuffer = new byte[Common.MAX_MESSAGE_SIZE];
        private ClientMessageType lastSplitMessageType = ClientMessageType.HEARTBEAT;
        //Receive buffer
        private long lastReceiveTime = 0;
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
        //Connection tracking
        private bool terminateOnNextMessageSend;
        private string connectionEndReason;
        private bool terminateThreadsOnNextUpdate;
        //Network traffic tracking
        private long bytesQueuedOut;
        private long bytesSent;
        private long bytesReceived;
        //Locking
        private int connectingThreads = 0;
        private object connectLock = new object();
        private object disconnectLock = new object();
        private object messageQueueLock = new object();
        private Thread connectThread;
        private List<Thread> parallelConnectThreads = new List<Thread>();
        private Thread receiveThread;
        private Thread sendThread;
        //Mesh stuff
        private UdpMeshClient meshClient;
        private Thread meshClientThread;
        private double lastSendSetPlayer;
        private Dictionary<string, Guid> meshPlayerGuids = new Dictionary<string, Guid>();
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
        private DynamicTickWorker dynamicTickWorker;
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

        public void SetDependencies(TimeSyncer timeSyncer, WarpWorker warpWorker, ChatWorker chatWorker, PlayerColorWorker playerColorWorker, FlagSyncer flagSyncer, PartKiller partKiller, KerbalReassigner kerbalReassigner, AsteroidWorker asteroidWorker, VesselWorker vesselWorker, PlayerStatusWorker playerStatusWorker, ScenarioWorker scenarioWorker, DynamicTickWorker dynamicTickWorker, CraftLibraryWorker craftLibraryWorker, ScreenshotWorker screenshotWorker, ToolbarSupport toolbarSupport, AdminSystem adminSystem, LockSystem lockSystem, DMPModInterface dmpModInterface, UniverseSyncCache universeSyncCache, VesselRecorder vesselRecorder, Groups groups, Permissions permissions, ModpackWorker modpackWorker)
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
            this.dynamicTickWorker = dynamicTickWorker;
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
        }

        //Called from main
        private void Update()
        {
            if (terminateThreadsOnNextUpdate)
            {
                terminateThreadsOnNextUpdate = false;
                TerminateThreads();
            }

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
                dynamicTickWorker.workerEnabled = true;
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

        //This isn't tied to frame rate, During the loading screen Update doesn't fire.
        public void SendThreadMain()
        {
            try
            {
                while (true)
                {
                    long startTime = profiler.GetCurrentTime;
                    long startMemory = profiler.GetCurrentMemory;
                    CheckDisconnection();
                    SendHeartBeat();
                    SendMeshSetPlayer();
                    bool sentMessage = SendOutgoingMessages();
                    profiler.Report("NetworkWorker.SendThread", startTime, startMemory);
                    if (!sentMessage)
                    {
                        sendEvent.WaitOne(100);
                    }
                }
            }
            catch (ThreadAbortException)
            {
                //Don't care
            }
            catch (Exception e)
            {
                DarkLog.Debug("Send thread error: " + e);
            }
        }

        #region Connecting to server

        //Called from main
        public void ConnectToServer(string address, int port)
        {
            DMPServerAddress connectAddress = new DMPServerAddress();
            connectAddress.ip = address;
            connectAddress.port = port;
            connectThread = new Thread(new ParameterizedThreadStart(ConnectToServerMain));
            connectThread.IsBackground = true;
            //ParameterizedThreadStart only takes one object.
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
                sendMessageQueueHigh = new Queue<ClientMessage>();
                sendMessageQueueSplit = new Queue<ClientMessage>();
                sendMessageQueueLow = new Queue<ClientMessage>();
                numberOfKerbals = 0;
                numberOfKerbalsReceived = 0;
                numberOfVessels = 0;
                numberOfVesselsReceived = 0;
                bytesReceived = 0;
                bytesQueuedOut = 0;
                bytesSent = 0;
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
                            List<IPEndPoint> addressToConnectTo = new List<IPEndPoint>();
                            foreach (IPAddress testAddress in dnsResult.AddressList)
                            {
                                if (testAddress.AddressFamily == AddressFamily.InterNetwork || testAddress.AddressFamily == AddressFamily.InterNetworkV6)
                                {
                                    Interlocked.Increment(ref connectingThreads);
                                    connectionWindow.status = "Connecting";
                                    connectionWindow.networkWorkerDisconnected = false;
                                    lastSendTime = Common.GetCurrentUnixTime();
                                    lastReceiveTime = Common.GetCurrentUnixTime();
                                    state = ClientState.CONNECTING;
                                    addressToConnectTo.Add(new IPEndPoint(testAddress, port));
                                }
                            }
                            foreach (IPEndPoint endpoint in addressToConnectTo)
                            {
                                Thread parallelConnectThread = new Thread(new ParameterizedThreadStart(ConnectToServerAddress));
                                parallelConnectThreads.Add(parallelConnectThread);
                                parallelConnectThread.Start(endpoint);
                            }
                            if (addressToConnectTo.Count == 0)
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
                    Interlocked.Increment(ref connectingThreads);
                    connectionWindow.status = "Connecting";
                    connectionWindow.networkWorkerDisconnected = false;
                    lastSendTime = Common.GetCurrentUnixTime();
                    lastReceiveTime = Common.GetCurrentUnixTime();
                    state = ClientState.CONNECTING;
                    ConnectToServerAddress(new IPEndPoint(destinationAddress, port));
                }
            }

            while (state == ClientState.CONNECTING)
            {
                Thread.Sleep(500);
                CheckInitialDisconnection();
            }
        }

        private void ConnectToServerAddress(object destinationObject)
        {
            IPEndPoint destination = (IPEndPoint)destinationObject;
            TcpClient testConnection = new TcpClient(destination.AddressFamily);
            testConnection.NoDelay = true;
            try
            {
                DarkLog.Debug("Connecting to " + destination.Address + " port " + destination.Port + "...");
                testConnection.Connect(destination.Address, destination.Port);
                lock (connectLock)
                {
                    if (state == ClientState.CONNECTING)
                    {
                        if (testConnection.Connected)
                        {
                            clientConnection = testConnection;
                            //Timeout didn't expire.
                            DarkLog.Debug("Connected to " + destination.Address + " port " + destination.Port);
                            connectionWindow.status = "Connected";
                            if (UdpMeshCommon.IsIPv4(destination.Address))
                            {
                                IPAddress[] myAddresses = new IPAddress[0];
                                try
                                {
                                    myAddresses = UdpMeshCommon.GetLocalIPAddresses();
                                }
                                catch
                                {
                                }
                                meshClient = new UdpMeshClient(destination, null, myAddresses, DarkLog.Debug);
                            }
                            if (UdpMeshCommon.IsIPv6(destination.Address))
                            {
                                IPAddress[] myAddresses = new IPAddress[0];
                                try
                                {
                                    myAddresses = UdpMeshCommon.GetLocalIPAddresses();
                                }
                                catch
                                {
                                }
                                meshClient = new UdpMeshClient(null, destination, myAddresses, DarkLog.Debug);
                            }
                            meshClient.RegisterCallback((int)MeshMessageType.SET_PLAYER, HandleMeshSetPlayer);
                            meshClient.RegisterCallback((int)MeshMessageType.VESSEL_UPDATE, HandleMeshVesselUpdate);
                            meshClientThread = meshClient.Start();
                            state = ClientState.CONNECTED;
                            sendThread = new Thread(new ThreadStart(SendThreadMain));
                            sendThread.IsBackground = true;
                            sendThread.Start();
                            receiveThread = new Thread(new ThreadStart(StartReceivingIncomingMessages));
                            receiveThread.IsBackground = true;
                            receiveThread.Start();
                        }
                        else
                        {
                            //The connection actually comes good, but after the timeout, so we can send the disconnect message.
                            if ((connectingThreads == 1) && (state == ClientState.CONNECTING))
                            {
                                DarkLog.Debug("Failed to connect within the timeout!");
                                Disconnect("Initial connection timeout");
                            }
                        }
                    }
                    else
                    {
                        if (testConnection.Connected)
                        {
                            testConnection.GetStream().Close();
                            testConnection.GetStream().Dispose();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if ((connectingThreads == 1) && (state == ClientState.CONNECTING))
                {
                    HandleDisconnectException(e);
                }
            }
            Interlocked.Decrement(ref connectingThreads);
            lock (parallelConnectThreads)
            {
                parallelConnectThreads.Remove(Thread.CurrentThread);
            }
        }

        #endregion

        #region Connection housekeeping

        private void CheckInitialDisconnection()
        {
            if (state == ClientState.CONNECTING)
            {
                if ((Common.GetCurrentUnixTime() - lastReceiveTime) > (Common.INITIAL_CONNECTION_TIMEOUT / 1000))
                {
                    Disconnect("Failed to connect!");
                    connectionWindow.status = "Failed to connect - no reply";
                    if (connectThread != null)
                    {
                        try
                        {
                            lock (parallelConnectThreads)
                            {
                                foreach (Thread parallelConnectThread in parallelConnectThreads)
                                {
                                    parallelConnectThread.Abort();
                                }
                                parallelConnectThreads.Clear();
                                connectingThreads = 0;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private void CheckDisconnection()
        {
            if (state >= ClientState.CONNECTED)
            {
                if ((Common.GetCurrentUnixTime() - lastReceiveTime) > (Common.CONNECTION_TIMEOUT / 1000))
                {
                    Disconnect("Connection timeout");
                }
            }
        }

        public void Disconnect(string reason)
        {
            lock (disconnectLock)
            {
                if (state != ClientState.DISCONNECTED)
                {
                    DarkLog.Debug("Disconnected, reason: " + reason);
                    if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight)
                    {
                        SendDisconnect("Force quit to main menu");
                        dmpGame.forceQuit = true;
                    }
                    else
                    {
                        Client.displayDisconnectMessage = true;
                    }
                    connectionWindow.status = reason;
                    connectionWindow.networkWorkerDisconnected = true;
                    state = ClientState.DISCONNECTED;

                    try
                    {
                        if (clientConnection != null)
                        {
                            clientConnection.GetStream().Close();
                            clientConnection.Close();
                            clientConnection = null;
                        }
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("Error closing connection: " + e.Message);
                    }
                    if (meshClient != null)
                    {
                        meshClient.Shutdown();
                    }
                    terminateThreadsOnNextUpdate = true;
                }
            }
        }

        private void TerminateThreads()
        {
            foreach (Thread parallelConnectThread in parallelConnectThreads)
            {
                try
                {
                    parallelConnectThread.Abort();
                }
                catch
                {
                    //Don't care
                }
            }
            parallelConnectThreads.Clear();
            connectingThreads = 0;
            try
            {
                connectThread.Abort();
            }
            catch
            {
                //Don't care
            }
            try
            {
                sendThread.Abort();
            }
            catch
            {
                //Don't care
            }
            try
            {
                receiveThread.Abort();
            }
            catch
            {
                //Don't care
            }
            try
            {
                meshClientThread.Abort();
            }
            catch
            {
            }
        }

        #endregion

        #region Network writers/readers

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

        private void StartReceivingIncomingMessages()
        {
            lastReceiveTime = Common.GetCurrentUnixTime();
            //Allocate byte for header
            isReceivingMessage = false;
            receiveMessage = new ServerMessage();
            receiveMessage.data = ByteRecycler.GetObject(8);
            receiveMessageBytesLeft = receiveMessage.data.Length;
            try
            {
                while (true)
                {
                    int bytesRead = clientConnection.GetStream().Read(receiveMessage.data.data, receiveMessage.data.Length - receiveMessageBytesLeft, receiveMessageBytesLeft);
                    long startTime = profiler.GetCurrentTime;
                    long startMemory = profiler.GetCurrentMemory;
                    bytesReceived += bytesRead;
                    receiveMessageBytesLeft -= bytesRead;
                    if (bytesRead > 0)
                    {
                        lastReceiveTime = Common.GetCurrentUnixTime();
                    }
                    else
                    {
                        Disconnect("Connection closed");
                        return;
                    }
                    if (receiveMessageBytesLeft == 0)
                    {
                        //We either have the header or the message data, let's do something
                        if (!isReceivingMessage)
                        {
                            //We have the header
                            using (MessageReader mr = new MessageReader(receiveMessage.data.data))
                            {
                                int messageType = mr.Read<int>();
                                int messageLength = mr.Read<int>();
                                //This is from the little endian -> big endian format change.
                                //The handshake challange type is 1, and the payload length is always 1032 bytes.
                                //Little endian (the previous format) DMPServer sends 01 00 00 00 | 08 04 00 00 as the first message, the handshake challange.
                                if (messageType == 16777216 && messageLength == 134479872)
                                {
                                    Disconnect("Disconnected from pre-v0.2 DMP server");
                                    return;
                                }
                                if (messageType > (Enum.GetNames(typeof(ServerMessageType)).Length - 1))
                                {
                                    //Malformed message, most likely from a non DMP-server.
                                    Disconnect("Disconnected from non-DMP server");
                                    //Returning from ReceiveCallback will break the receive loop and stop processing any further messages.
                                    return;
                                }
                                receiveMessage.type = (ServerMessageType)messageType;
                                ByteRecycler.ReleaseObject(receiveMessage.data);
                                receiveMessage.data = null;
                                if (messageLength == 0)
                                {
                                    switch (receiveMessage.type)
                                    {
                                        case ServerMessageType.HEARTBEAT:
                                        case ServerMessageType.KERBAL_COMPLETE:
                                        case ServerMessageType.VESSEL_COMPLETE:
                                            HandleMessage(receiveMessage);
                                            break;
                                    }
                                    receiveMessage.type = 0;
                                    receiveMessage.data = ByteRecycler.GetObject(8);
                                    receiveMessageBytesLeft = receiveMessage.data.Length;
                                }
                                else
                                {
                                    if (messageLength < Common.MAX_MESSAGE_SIZE)
                                    {
                                        isReceivingMessage = true;
                                        receiveMessage.data = ByteRecycler.GetObject(messageLength);
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
                            HandleMessage(receiveMessage);
                            receiveMessage.type = 0;
                            ByteRecycler.ReleaseObject(receiveMessage.data);
                            receiveMessage.data = ByteRecycler.GetObject(8);
                            receiveMessageBytesLeft = receiveMessage.data.Length;
                        }
                    }
                    if (state < ClientState.CONNECTED || state == ClientState.DISCONNECTING)
                    {
                        return;
                    }
                    profiler.Report("NetworkWorker.ReceiveThread", startTime, startMemory);
                }
            }
            catch (Exception e)
            {
                if (state != ClientState.DISCONNECTED)
                {
                    HandleDisconnectException(e);
                }
            }
        }

        private void QueueOutgoingMessage(ClientMessage message, bool highPriority)
        {
            lock (messageQueueLock)
            {
                //All messages have an 8 byte header
                bytesQueuedOut += 8;
                if (message.data != null && message.data.Length > 0)
                {
                    //Count the payload if we have one.
                    bytesQueuedOut += message.data.Length;
                }
                if (highPriority)
                {
                    sendMessageQueueHigh.Enqueue(message);
                }
                else
                {
                    sendMessageQueueLow.Enqueue(message);
                }
            }
            sendEvent.Set();
        }

        private bool SendOutgoingMessages()
        {
            ClientMessage sendMessage = null;
            lock (messageQueueLock)
            {
                if (state >= ClientState.CONNECTED)
                {
                    if (sendMessageQueueHigh.Count > 0)
                    {
                        sendMessage = sendMessageQueueHigh.Dequeue();
                    }
                    if ((sendMessage == null) && (sendMessageQueueSplit.Count > 0))
                    {
                        sendMessage = sendMessageQueueSplit.Dequeue();
                        //We just sent the last piece of a split message
                        if (sendMessageQueueSplit.Count == 0)
                        {
                            if (lastSplitMessageType == ClientMessageType.CRAFT_LIBRARY)
                            {
                                craftLibraryWorker.finishedUploadingCraft = true;
                            }
                            if (lastSplitMessageType == ClientMessageType.SCREENSHOT_LIBRARY)
                            {
                                screenshotWorker.finishedUploadingScreenshot = true;
                            }
                        }
                    }
                    if ((sendMessage == null) && (sendMessageQueueLow.Count > 0))
                    {
                        sendMessage = sendMessageQueueLow.Dequeue();
                        //Splits large messages to higher priority messages can get into the queue faster
                        SplitAndRewriteMessage(ref sendMessage);
                    }
                }
            }
            if (sendMessage != null)
            {
                SendNetworkMessage(sendMessage);
                //Now that we have sent the message, we can release it's memory
                if (sendMessage.data != null)
                {
                    ByteRecycler.ReleaseObject(sendMessage.data);
                }
                return true;
            }
            return false;
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
                lastSplitMessageType = message.type;
                ClientMessage newSplitMessage = new ClientMessage();
                newSplitMessage.type = ClientMessageType.SPLIT_MESSAGE;
                int splitBytesLeft = message.data.Length;
                newSplitMessage.data = ByteRecycler.GetObject(12 + Common.SPLIT_MESSAGE_LENGTH);
                using (MessageWriter mw = new MessageWriter(newSplitMessage.data.data))
                {
                    mw.Write<int>((int)message.type);
                    mw.Write<int>(message.data.Length);
                    ByteArray firstSplit = ByteRecycler.GetObject(Common.SPLIT_MESSAGE_LENGTH);
                    Array.Copy(message.data.data, 0, firstSplit.data, 0, Common.SPLIT_MESSAGE_LENGTH);
                    mw.Write<ByteArray>(firstSplit);
                    ByteRecycler.ReleaseObject(firstSplit);
                    splitBytesLeft -= Common.SPLIT_MESSAGE_LENGTH;
                    //SPLIT_MESSAGE adds a 12 byte header.
                    bytesQueuedOut += 12;
                    sendMessageQueueSplit.Enqueue(newSplitMessage);
                }

                while (splitBytesLeft > 0)
                {
                    ClientMessage currentSplitMessage = new ClientMessage();
                    currentSplitMessage.type = ClientMessageType.SPLIT_MESSAGE;
                    currentSplitMessage.data = ByteRecycler.GetObject(Math.Min(splitBytesLeft, Common.SPLIT_MESSAGE_LENGTH));
                    Array.Copy(message.data.data, message.data.Length - splitBytesLeft, currentSplitMessage.data.data, 0, currentSplitMessage.data.Length);
                    splitBytesLeft -= currentSplitMessage.data.Length;
                    //Add the SPLIT_MESSAGE header to the out queue count.
                    bytesQueuedOut += 8;
                    sendMessageQueueSplit.Enqueue(currentSplitMessage);
                }
                ByteRecycler.ReleaseObject(message.data);
                message = sendMessageQueueSplit.Dequeue();
            }
        }

        private void SendNetworkMessage(ClientMessage message)
        {
            ByteArray messageBytes = Common.PrependNetworkFrame((int)message.type, message.data);

            lock (messageQueueLock)
            {
                bytesQueuedOut -= messageBytes.Length;
                bytesSent += messageBytes.Length;
            }
            //Disconnect after EndWrite completes
            if (message.type == ClientMessageType.CONNECTION_END)
            {
                using (MessageReader mr = new MessageReader(message.data.data))
                {
                    terminateOnNextMessageSend = true;
                    connectionEndReason = mr.Read<string>();
                }
            }
            lastSendTime = Common.GetCurrentUnixTime();
            try
            {
                clientConnection.GetStream().Write(messageBytes.data, 0, messageBytes.Length);
                if (terminateOnNextMessageSend)
                {
                    Disconnect("Connection ended: " + connectionEndReason);
                    connectionEndReason = null;
                    terminateOnNextMessageSend = false;
                }
            }
            catch (Exception e)
            {
                if (state != ClientState.DISCONNECTED)
                {
                    HandleDisconnectException(e);
                }
            }
            //Release the send buffer we got with PrependNetworkFrame.
            ByteRecycler.ReleaseObject(messageBytes);
        }

        private void HandleDisconnectException(Exception e)
        {
            if (e.InnerException != null)
            {
                DarkLog.Debug("Connection error: " + e.Message + ", " + e.InnerException);
                Disconnect("Connection error: " + e.Message + ", " + e.InnerException.Message);
            }
            else
            {
                DarkLog.Debug("Connection error: " + e);
                Disconnect("Connection error: " + e.Message);
            }
        }

        #endregion

        #region Message Handling

        private void HandleMessage(ServerMessage message)
        {
#if !DEBUG
            try
            {
#endif
                switch (message.type)
                {
                    case ServerMessageType.HEARTBEAT:
                        break;
                    case ServerMessageType.HANDSHAKE_CHALLANGE:
                        HandleHandshakeChallange(message.data);
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
                    case ServerMessageType.PLAYER_COLOR:
                        playerColorWorker.HandlePlayerColorMessage(message.data);
                        break;
                    case ServerMessageType.PLAYER_JOIN:
                        HandlePlayerJoin(message.data);
                        break;
                    case ServerMessageType.PLAYER_DISCONNECT:
                        HandlePlayerDisconnect(message.data);
                        break;
                    case ServerMessageType.GROUP:
                        HandleGroupMessage(message.data);
                        break;
                    case ServerMessageType.PERMISSION:
                        HandlePermissionMessage(message.data);
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
                    case ServerMessageType.KERBAL_REMOVE:
                        HandleKerbalRemove(message.data);
                        break;
                    case ServerMessageType.VESSEL_LIST:
                        HandleVesselList(message.data);
                        break;
                    case ServerMessageType.VESSEL_PROTO:
                        HandleVesselProto(message.data);
                        break;
                    case ServerMessageType.VESSEL_UPDATE:
                        HandleVesselUpdate(message.data, false);
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
                    case ServerMessageType.FLAG_SYNC:
                        flagSyncer.HandleMessage(message.data);
                        break;
                    case ServerMessageType.SET_SUBSPACE:
                        warpWorker.HandleSetSubspace(message.data);
                        break;
                    case ServerMessageType.SYNC_TIME_REPLY:
                        HandleSyncTimeReply(message.data);
                        break;
                    case ServerMessageType.PING_REPLY:
                        HandlePingReply(message.data);
                        break;
                    case ServerMessageType.MOTD_REPLY:
                        HandleMotdReply(message.data);
                        break;
                    case ServerMessageType.WARP_CONTROL:
                        HandleWarpControl(message.data);
                        break;
                    case ServerMessageType.ADMIN_SYSTEM:
                        adminSystem.HandleAdminMessage(message.data);
                        break;
                    case ServerMessageType.LOCK_SYSTEM:
                        lockSystem.HandleLockMessage(message.data);
                        break;
                    case ServerMessageType.MOD_DATA:
                        dmpModInterface.HandleModData(message.data);
                        break;
                    case ServerMessageType.SPLIT_MESSAGE:
                        HandleSplitMessage(message.data);
                        break;
                    case ServerMessageType.CONNECTION_END:
                        HandleConnectionEnd(message.data);
                        break;
                    case ServerMessageType.MODPACK_DATA:
                        HandleModpackData(message.data);
                        break;
                default:
                        DarkLog.Debug("Unhandled message type " + message.type);
                        break;
                }
#if !DEBUG
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling message type " + message.type + ", exception: " + e);
                SendDisconnect("Error handling " + message.type + " message");
            }
#endif
        }

        private void HandleHandshakeChallange(ByteArray messageData)
        {
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

        private void HandleHandshakeReply(ByteArray messageData)
        {

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
                        Compression.compressionEnabled = mr.Read<bool>() && dmpSettings.compressionEnabled;
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

        private void HandleChatMessage(ByteArray messageData)
        {
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

        private void HandleServerSettings(ByteArray messageData)
        {
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

        private void HandlePlayerStatus(ByteArray messageData)
        {
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

        private void HandlePlayerJoin(ByteArray messageData)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                string playerName = mr.Read<string>();
                chatWorker.QueueChannelMessage(chatWorker.consoleIdentifier, "", playerName + " has joined the server");
            }
        }

        private void HandlePlayerDisconnect(ByteArray messageData)
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

        private void HandleSyncTimeReply(ByteArray messageData)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                long clientSend = mr.Read<long>();
                long serverReceive = mr.Read<long>();
                long serverSend = mr.Read<long>();
                timeSyncer.HandleSyncTime(clientSend, serverReceive, serverSend);
            }
        }

        private void HandleScenarioModuleData(ByteArray messageData)
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

        private void HandleKerbalReply(ByteArray messageData)
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

        private void HandleKerbalComplete()
        {
            state = ClientState.KERBALS_SYNCED;
            DarkLog.Debug("Kerbals Synced!");
            connectionWindow.status = "Kerbals synced";
        }

        private void HandleKerbalRemove(ByteArray messageData)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                double planetTime = mr.Read<double>();
                string kerbalName = mr.Read<string>();
                DarkLog.Debug("Kerbal removed: " + kerbalName);
                ScreenMessages.PostScreenMessage("Kerbal " + kerbalName + " removed from game at " + planetTime, 5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void HandleVesselList(ByteArray messageData)
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

        private void HandleVesselProto(ByteArray messageData)
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

        public VesselUpdate VeselUpdateFromBytes(byte[] messageData, bool fromMesh)
        {
            VesselUpdate update = Recycler<VesselUpdate>.GetObject();
            update.SetVesselWorker(vesselWorker);
            using (MessageReader mr = new MessageReader(messageData))
            {
                if (fromMesh)
                {
                    for (int i = 0; i < 24; i++)
                    {
                        //Don't care, we are stripping the header.
                        mr.Read<byte>();
                    }
                }
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

        private void HandleVesselUpdate(ByteArray messageData, bool fromMesh)
        {
            VesselUpdate update = VeselUpdateFromBytes(messageData.data, fromMesh);
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

        private void HandleVesselComplete()
        {
            state = ClientState.VESSELS_SYNCED;
        }

        private void HandleVesselRemove(ByteArray messageData)
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

        private void HandleCraftLibrary(ByteArray messageData)
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

        private void HandleScreenshotLibrary(ByteArray messageData)
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

        private void HandlePingReply(ByteArray messageData)
        {
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                int pingTime = (int)((DateTime.UtcNow.Ticks - mr.Read<long>()) / (float)TimeSpan.TicksPerMillisecond);
                chatWorker.QueueChannelMessage(chatWorker.consoleIdentifier, "", "Ping: " + pingTime + "ms.");
            }

        }

        private void HandleMotdReply(ByteArray messageData)
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

        private void HandleGroupMessage(ByteArray messageData)
        {
            groups.QueueMessage(messageData);
        }

        private void HandlePermissionMessage(ByteArray messageData)
        {
            permissions.QueueMessage(messageData);
        }

        private void HandleWarpControl(ByteArray messageData)
        {
            warpWorker.QueueWarpMessage(messageData);
        }

        private void HandleSplitMessage(ByteArray messageData)
        {
            if (!isReceivingSplitMessage)
            {
                //New split message
                using (MessageReader mr = new MessageReader(messageData.data))
                {
                    receiveSplitMessage = new ServerMessage();
                    receiveSplitMessage.type = (ServerMessageType)mr.Read<int>();
                    receiveSplitMessage.data = ByteRecycler.GetObject(mr.Read<int>());
                    receiveSplitMessageBytesLeft = receiveSplitMessage.data.Length;
                    ByteArray firstSplitData = mr.Read<ByteArray>();
                    Array.Copy(firstSplitData.data, 0, receiveSplitMessage.data.data, 0, firstSplitData.Length);
                    receiveSplitMessageBytesLeft -= firstSplitData.Length;
                    ByteRecycler.ReleaseObject(firstSplitData);
                }
                isReceivingSplitMessage = true;
            }
            else
            {
                //Continued split message
                Array.Copy(messageData.data, 0, receiveSplitMessage.data.data, receiveSplitMessage.data.Length - receiveSplitMessageBytesLeft, messageData.Length);
                receiveSplitMessageBytesLeft -= messageData.Length;
            }
            if (receiveSplitMessageBytesLeft == 0)
            {
                HandleMessage(receiveSplitMessage);
                ByteRecycler.ReleaseObject(receiveSplitMessage.data);
                receiveSplitMessage = null;
                isReceivingSplitMessage = false;
            }
        }

        private void HandleConnectionEnd(ByteArray messageData)
        {
            string reason = "";
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                reason = mr.Read<string>();
            }
            Disconnect("Server closed connection: " + reason);
        }

        private void HandleModpackData(ByteArray data)
        {
            modpackWorker.HandleModpackMessage(data);
        }

        #endregion

        #region Message Sending

        private void SendHeartBeat()
        {
            if (state >= ClientState.CONNECTED && sendMessageQueueHigh.Count == 0)
            {
                if ((Common.GetCurrentUnixTime() - lastSendTime) > (Common.HEART_BEAT_INTERVAL / 1000))
                {
                    lastSendTime = Common.GetCurrentUnixTime();
                    ClientMessage newMessage = new ClientMessage();
                    newMessage.type = ClientMessageType.HEARTBEAT;
                    QueueOutgoingMessage(newMessage, true);
                }
            }
        }

        private void SendHandshakeResponse(byte[] signature)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.HANDSHAKE_RESPONSE;
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<int>(Common.PROTOCOL_VERSION);
                mw.Write<string>(dmpSettings.playerName);
                mw.Write<string>(dmpSettings.playerPublicKey);
                mw.Write<byte[]>(signature);
                mw.Write<string>(Common.PROGRAM_VERSION);
                mw.Write<bool>(dmpSettings.compressionEnabled);
                newMessageLength = (int)mw.GetMessageLength();
            }
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from ChatWindow
        public void SendChatMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.CHAT_MESSAGE;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from PlayerStatusWorker
        public void SendPlayerStatus(PlayerStatus playerStatus)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.PLAYER_STATUS;
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<string>(playerStatus.playerName);
                mw.Write<string>(playerStatus.vesselText);
                mw.Write<string>(playerStatus.statusText);
                newMessageLength = (int)mw.GetMessageLength();
            }
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from PlayerColorWorker
        public void SendPlayerColorMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.PLAYER_COLOR;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called from timeSyncer
        public void SendTimeSync()
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SYNC_TIME_REQUEST;
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<long>(DateTime.UtcNow.Ticks);
                newMessageLength = (int)mw.GetMessageLength();
            }
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }

        private void SendGroupsRequest()
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.GROUP;
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<int>((int)GroupMessageType.GROUP_REQUEST);
                newMessageLength = (int)mw.GetMessageLength();
            }
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }

        private void SendPermissionsRequest()
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.PERMISSION;
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<int>((int)PermissionMessageType.PERMISSION_REQUEST);
                newMessageLength = (int)mw.GetMessageLength();
            }
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }

        private void SendKerbalsRequest()
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.KERBALS_REQUEST;
            QueueOutgoingMessage(newMessage, true);
        }

        private void SendVesselsRequest(string[] requestList)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSELS_REQUEST;
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<string[]>(requestList);
                newMessageLength = (int)mw.GetMessageLength();
            }
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
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

            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSEL_PROTO;
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
                newMessage.data = ByteRecycler.GetObject(newMessageLength);
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

        public ClientMessage GetVesselUpdateMessage(VesselUpdate update)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSEL_UPDATE;
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
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            return newMessage;
        }
        //Called from vesselWorker
        public void SendVesselUpdate(VesselUpdate update)
        {
            ClientMessage newMessage = GetVesselUpdateMessage(update);
            QueueOutgoingMessage(newMessage, false);
            vesselRecorder.RecordSend(newMessage.data, ClientMessageType.VESSEL_UPDATE, update.vesselID);
        }
        //Called from vesselWorker
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
        //Called from vesselWorker
        public void SendVesselRemove(Guid vesselID, bool isDockingUpdate)
        {
            if (!permissions.PlayerHasVesselPermission(dmpSettings.playerName, vesselID))
            {
                return;
            }
            DarkLog.Debug("Removing " + vesselID + " from the server");
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.VESSEL_REMOVE;
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
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, false);
            vesselRecorder.RecordSend(newMessage.data, ClientMessageType.VESSEL_REMOVE, vesselID);
        }

        // Called from VesselWorker
        public void SendKerbalRemove(string kerbalName)
        {
            DarkLog.Debug("Removing kerbal " + kerbalName + " from the server");
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.KERBAL_REMOVE;
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<double>(Planetarium.GetUniversalTime());
                mw.Write<string>(kerbalName);
                newMessageLength = (int)mw.GetMessageLength();
            }
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called fro craftLibraryWorker
        public void SendCraftLibraryMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.CRAFT_LIBRARY;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called from ScreenshotWorker
        public void SendScreenshotMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SCREENSHOT_LIBRARY;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called from ScenarioWorker
        public void SendScenarioModuleData(string[] scenarioNames, ByteArray[] scenarioData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SCENARIO_DATA;
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
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            DarkLog.Debug("Sending " + scenarioNames.Length + " scenario modules");
            QueueOutgoingMessage(newMessage, false);
        }
        // Same method as above, only that in this, the message is queued as high priority
        public void SendScenarioModuleDataHighPriority(string[] scenarioNames, ByteArray[] scenarioData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.SCENARIO_DATA;
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
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
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
                ClientMessage newMessage = new ClientMessage();
                newMessage.type = ClientMessageType.KERBAL_PROTO;
                int newMessageLength = 0;
                using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
                {
                    mw.Write<double>(Planetarium.GetUniversalTime());
                    mw.Write<string>(kerbalName);
                    mw.Write<ByteArray>(kerbalBytes);
                    newMessageLength = (int)mw.GetMessageLength();
                }
                newMessage.data = ByteRecycler.GetObject(newMessageLength);
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
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.PING_REQUEST;
            int newMessageLength = 0;
            using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
            {
                mw.Write<long>(DateTime.UtcNow.Ticks);
                newMessageLength = (int)mw.GetMessageLength();
            }
            newMessage.data = ByteRecycler.GetObject(newMessageLength);
            Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from networkWorker
        public void SendMotdRequest()
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.MOTD_REQUEST;
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from FlagSyncer
        public void SendFlagMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.FLAG_SYNC;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, false);
        }
        //Called from warpWorker
        public void SendWarpMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.WARP_CONTROL;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from groups
        public void SendGroupMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.GROUP;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from permissions
        public void SendPermissionsMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.PERMISSION;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from lockSystem
        public void SendLockSystemMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.LOCK_SYSTEM;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, true);
        }
        //Called from warpWorker
        public void SendModpackMessage(byte[] messageData)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.MODPACK_DATA;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, false);
        }

        /// <summary>
        /// If you are a mod, call dmpModInterface.SendModMessage.
        /// </summary>
        public void SendModMessage(byte[] messageData, bool highPriority)
        {
            ClientMessage newMessage = new ClientMessage();
            newMessage.type = ClientMessageType.MOD_DATA;
            newMessage.data = ByteRecycler.GetObject(messageData.Length);
            messageData.CopyTo(newMessage.data.data, 0);
            QueueOutgoingMessage(newMessage, highPriority);
        }
        //Called from main
        public void SendDisconnect(string disconnectReason = "Unknown")
        {
            if (state != ClientState.DISCONNECTING && state >= ClientState.CONNECTED)
            {
                ClientMessage newMessage = new ClientMessage();
                newMessage.type = ClientMessageType.CONNECTION_END;
                DarkLog.Debug("Sending disconnect message, reason: " + disconnectReason);
                connectionWindow.status = "Disconnected: " + disconnectReason;
                state = ClientState.DISCONNECTING;
                int newMessageLength = 0;
                using (MessageWriter mw = new MessageWriter(messageWriterBuffer))
                {
                    mw.Write<string>(disconnectReason);
                    newMessageLength = (int)mw.GetMessageLength();
                }
                newMessage.data = ByteRecycler.GetObject(newMessageLength);
                Array.Copy(messageWriterBuffer, 0, newMessage.data.data, 0, newMessageLength);
                QueueOutgoingMessage(newMessage, true);
            }
        }

        public long GetStatistics(string statType)
        {
            switch (statType)
            {
                case "HighPriorityQueueLength":
                    return sendMessageQueueHigh.Count;
                case "SplitPriorityQueueLength":
                    return sendMessageQueueSplit.Count;
                case "LowPriorityQueueLength":
                    return sendMessageQueueLow.Count;
                case "QueuedOutBytes":
                    return bytesQueuedOut;
                case "SentBytes":
                    return bytesSent;
                case "ReceivedBytes":
                    return bytesReceived;
                case "LastReceiveTime":
                    return ((Common.GetCurrentUnixTime() - lastReceiveTime) * 1000);
                case "LastSendTime":
                    return ((Common.GetCurrentUnixTime() - lastSendTime) * 1000);
            }
            return 0;
        }

        public UdpMeshClient GetMesh()
        {
            return meshClient;
        }

        #endregion
    }

    class DMPServerAddress
    {
        public string ip;
        public int port;
    }
}

