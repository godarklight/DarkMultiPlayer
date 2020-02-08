using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using MessageStream2;

namespace DarkMultiPlayer
{
    public class ServerListConnection
    {
        private Settings dmpSettings;
        private ServersWindow serversWindow;
        private List<IPAddress> addresses = new List<IPAddress>();
        private int tryAddress = 0;
        private TcpClient tcpClient;
        private bool connecting;
        private bool connected;
        private bool dnsRequested;
        private bool dnsReceived;
        private long lastConnectTime;
        private double lastSendTime;
        private double lastReceiveTime;
        private Dictionary<int, ServerListEntry> servers = new Dictionary<int, ServerListEntry>();

        public ServerListConnection(Settings dmpSettings)
        {
            this.dmpSettings = dmpSettings;
        }

        public void SetDependancy(ServersWindow serversWindow)
        {
            this.serversWindow = serversWindow;
        }

        public void Update()
        {
            if (dmpSettings.serverlistMode == 0)
            {
                return;
            }
            if (dmpSettings.serverlistMode == -1)
            {
                if (connecting || connected)
                {
                    Disconnect();
                    return;
                }
            }
            if (dmpSettings.serverlistMode == 1 && serversWindow.display)
            {
                if (!connected)
                {
                    Connect();
                }
            }
            if (dmpSettings.serverlistMode == 1 && !serversWindow.display)
            {
                if (connected)
                {
                    Disconnect();
                }
            }
            if (dmpSettings.serverlistMode == 2 && HighLogic.LoadedScene == GameScenes.MAINMENU)
            {
                if (!connected)
                {
                    Connect();
                }
            }
            if (dmpSettings.serverlistMode == 2 && HighLogic.LoadedScene != GameScenes.MAINMENU)
            {
                if (connected)
                {
                    Disconnect();
                }
            }
            if (connected)
            {
                //Keep connection alive
                if (Client.realtimeSinceStartup > (lastSendTime + 5f))
                {
                    SendHeartbeat();
                }
                //Connection timed out
                if (Client.realtimeSinceStartup > (lastReceiveTime + 60f))
                {
                    Disconnect();
                }
            }
        }

        byte[] heartbeatBytes;
        private void SendHeartbeat()
        {
            lastSendTime = Client.realtimeSinceStartup;
            if (heartbeatBytes == null)
            {
                heartbeatBytes = new byte[8];
            }
            if (tcpClient != null)
            {
                tcpClient.GetStream().BeginWrite(heartbeatBytes, 0, 0, EndSendHeartbeat, tcpClient);
            }
        }


        private void EndSendHeartbeat(IAsyncResult ar)
        {
            TcpClient client = (TcpClient)ar.AsyncState;
            client.GetStream().EndWrite(ar);
        }

        private void Disconnect()
        {
            connecting = false;
            connected = false;
            if (tcpClient != null)
            {
                try
                {
                    tcpClient.Close();
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Error closing serverlist connection: " + e);
                }
                servers.Clear();
                tcpClient = null;
                DarkLog.Debug("ServerList Disconnected!");
            }
        }

        private void Connect()
        {
            if (!dnsRequested)
            {
                dnsRequested = true;
                StartGetHostAddresses("godarklight.info.tm");
                StartGetHostAddresses("server.game.api.d-mp.org");
            }
            if (!dnsReceived)
            {
                return;
            }
            if (!connecting)
            {
                if (addresses.Count == 0)
                {
                    return;
                }
                if (Client.realtimeSinceStartup - lastConnectTime > 0.5f)
                {
                    connecting = true;
                    try
                    {
                        if (addresses.Count == tryAddress)
                        {
                            tryAddress = 0;
                        }
                        DarkLog.Debug("Serverlist attempting to connect to " + addresses[tryAddress]);
                        TcpClient newClient = new TcpClient(addresses[tryAddress].AddressFamily);
                        newClient.BeginConnect(addresses[tryAddress], 9003, HandleConnectCallback, newClient);
                    }
                    catch (Exception e)
                    {
                        //Instantly try next connection
                        DarkLog.Debug("Serverlist BeginConnect Error: " + e);
                        lastConnectTime = 0;
                    }
                    tryAddress++;
                }
            }
        }

        private void StartGetHostAddresses(string hostname)
        {
            try
            {
                Dns.BeginGetHostAddresses("godarklight.info.tm", EndGetHostAddresses, null);
            }
            catch (Exception e)
            {
                DarkLog.Debug("StartGetHostAddresses error: " + e);
            }
        }

        private void EndGetHostAddresses(IAsyncResult ar)
        {
            lock (addresses)
            {
                try
                {
                    IPAddress[] newAddrs = Dns.EndGetHostAddresses(ar);
                    {
                        //Preference IPv6
                        foreach (IPAddress newAddr in newAddrs)
                        {
                            if (newAddr.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                dnsReceived = true;
                                addresses.Add(newAddr);
                            }
                        }
                        foreach (IPAddress newAddr in newAddrs)
                        {
                            if (newAddr.AddressFamily == AddressFamily.InterNetwork)
                            {
                                dnsReceived = true;
                                addresses.Add(newAddr);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    DarkLog.Debug("EndGetHostAddresses error: " + e);
                }
            }
        }

        private void HandleConnectCallback(IAsyncResult ar)
        {
            try
            {
                lock (addresses)
                {
                    if (tcpClient != null)
                    {
                        return;
                    }
                    TcpClient newClient = (TcpClient)ar.AsyncState;
                    newClient.EndConnect(ar);
                    if (newClient.Connected)
                    {
                        //We're connected!
                        DarkLog.Debug("Serverlist connected!");
                        connected = true;
                        lastSendTime = Client.realtimeSinceStartup;
                        lastReceiveTime = Client.realtimeSinceStartup;
                        tcpClient = newClient;
                        ServerConnectionState scs = new ServerConnectionState();
                        scs.client = newClient;
                        scs.type = 0;
                        scs.header = true;
                        scs.pos = 0;
                        scs.length = 8;
                        scs.client.GetStream().BeginRead(scs.buffer, scs.pos, scs.length - scs.pos, EndReceive, scs);
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("EndConnect error: " + e);
            }
        }

        private void EndReceive(IAsyncResult ar)
        {
            try
            {
                ServerConnectionState scs = (ServerConnectionState)ar.AsyncState;
                int bytesRead = scs.client.GetStream().EndRead(ar);
                if (bytesRead != 0)
                {
                    lastReceiveTime = Client.realtimeSinceStartup;
                    scs.pos += bytesRead;
                    if (scs.pos == scs.length)
                    {
                        if (scs.header)
                        {
                            using (MessageReader mr = new MessageReader(scs.buffer))
                            {
                                scs.type = mr.Read<int>();
                                scs.length = mr.Read<int>();
                            }
                            if (scs.length == 0)
                            {
                                HandleMessage(scs.type, null, 0);
                                scs.header = true;
                                scs.pos = 0;
                                scs.length = 8;
                            }
                            else
                            {
                                scs.header = false;
                                scs.pos = 0;
                            }
                        }
                        else
                        {
                            HandleMessage(scs.type, scs.buffer, scs.length);
                            scs.header = true;
                            scs.pos = 0;
                            scs.length = 8;
                        }
                    }
                    scs.client.GetStream().BeginRead(scs.buffer, scs.pos, scs.length - scs.pos, EndReceive, scs);
                }
                else
                {
                    //Disconnected from server side
                    connected = false;
                    tcpClient = null;
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("ServerConnection Receive error: " + e);
                //Disconnected from error
                connected = false;
                tcpClient = null;
            }
        }

        private void HandleMessage(int type, byte[] data, int length)
        {
            try
            {
                switch (type)
                {
                    case (int)ServerListMessageType.CONNECT:
                        HandleConnect(data, length);
                        break;
                    case (int)ServerListMessageType.HEARTBEAT:
                        //Don't care.
                        break;
                    case (int)ServerListMessageType.REPORT:
                        HandleReport(data, length);
                        break;
                    case (int)ServerListMessageType.DISCONNECT:
                        HandleDisconnect(data, length);
                        break;
                }
            }
            catch
            {
                DarkLog.Debug("Failed to process serverlist message");
            }
        }


        private void HandleConnect(byte[] data, int length)
        {
            using (MessageReader mr = new MessageReader(data))
            {
                string serverID = mr.Read<string>();
                int clientID = mr.Read<int>();
                string remoteAddress = mr.Read<string>();
                int remotePort = mr.Read<int>();
                lock (servers)
                {
                    if (!servers.ContainsKey(clientID))
                    {
                        servers.Add(clientID, new ServerListEntry());
                    }
                    ServerListEntry sle = servers[clientID];
                    sle.gameAddress = remoteAddress;
                }
                Console.WriteLine("Server " + serverID + ":" + clientID + " connected from " + remoteAddress + " port " + remotePort);
            }
        }

        private void HandleReport(byte[] data, int length)
        {
            using (MessageReader mr = new MessageReader(data))
            {
                string serverID = mr.Read<string>();
                int clientID = mr.Read<int>();
                byte[] reportBytes = mr.Read<byte[]>();
                lock (servers)
                {
                    if (!servers.ContainsKey(clientID))
                    {
                        servers.Add(clientID, new ServerListEntry());
                    }
                    ServerListEntry sle = servers[clientID];
                    sle.FromBytes(reportBytes);
                }
                Console.WriteLine("Server " + serverID + ":" + clientID + " reported new state");
            }
        }

        private void HandleDisconnect(byte[] data, int length)
        {
            using (MessageReader mr = new MessageReader(data))
            {
                string serverID = mr.Read<string>();
                int clientID = mr.Read<int>();
                lock (servers)
                {
                    if (servers.ContainsKey(clientID))
                    {
                        servers.Remove(clientID);
                    }
                }
                Console.WriteLine("Server " + serverID + ":" + clientID + " disconnected");
            }
        }


        /// <summary>
        /// Make sure to lock onto the dictionary when using.
        /// </summary>
        /// <returns>The servers.</returns>
        public Dictionary<int, ServerListEntry> GetServers()
        {
            lock (servers)
            {
                return servers;
            }
        }

        //Lifted straight from godarklight/DBBackend-stateless
        public class ServerListEntry
        {
            public bool isValid = false;
            public string serverHash;
            public string serverName;
            public string description;
            public int gamePort;
            public string gameAddress;
            public int protocolVersion;
            public string programVersion;
            public int maxPlayers;
            public int modControl;
            public string modControlSha;
            public int gameMode;
            public bool cheats;
            public int warpMode;
            public long universeSize;
            public string banner;
            public string homepage;
            public int httpPort;
            public string admin;
            public string team;
            public string location;
            public bool fixedIP;
            public string[] players;

            public void FromBytes(byte[] inputBytes)
            {
                using (MessageReader mr = new MessageReader(inputBytes))
                {
                    serverHash = mr.Read<string>();
                    serverName = mr.Read<string>();
                    description = mr.Read<string>();
                    gamePort = mr.Read<int>();
                    gameAddress = mr.Read<string>();
                    protocolVersion = mr.Read<int>();
                    programVersion = mr.Read<string>();
                    maxPlayers = mr.Read<int>();
                    modControl = mr.Read<int>();
                    modControlSha = mr.Read<string>();
                    gameMode = mr.Read<int>();
                    cheats = mr.Read<bool>();
                    warpMode = mr.Read<int>();
                    universeSize = mr.Read<long>();
                    banner = mr.Read<string>();
                    homepage = mr.Read<string>();
                    httpPort = mr.Read<int>();
                    admin = mr.Read<string>();
                    team = mr.Read<string>();
                    location = mr.Read<string>();
                    fixedIP = mr.Read<bool>();
                    players = mr.Read<string[]>();
                }
                isValid = true;
            }
        }

        private class ServerConnectionState
        {
            public int type;
            public bool header;
            public int pos;
            public int length;
            public TcpClient client;
            //512kB buffer should be more than enough...
            public byte[] buffer = new byte[64 * 1024];
        }

        private enum ServerListMessageType
        {
            HEARTBEAT,
            CONNECT,
            REPORT,
            DISCONNECT,
        }
    }
}
