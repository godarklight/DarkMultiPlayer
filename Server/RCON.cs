using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    /// <summary>
    /// A system to use SRCDS-style RCON commands.
    /// </summary>
    public static class RCON
    {
        public delegate void CommandReceivedEventHandler(object sender, RconCommandEventArgs e);
        public static event CommandReceivedEventHandler CommandReceived;

        private static bool _running;
        private static TcpListener _tcpListener;
        private static List<RCONClient> _clients = new List<RCONClient>();

        /// <summary>
        /// Start the RCON server
        /// </summary>
        public static void Start()
        {
            _tcpListener = new TcpListener(new IPEndPoint(IPAddress.Any, Settings.settingsStore.rconPort));

            _tcpListener.Start();
            _running = true;
            _tcpListener.BeginAcceptTcpClient(AcceptTcpClient, null);

            DarkLog.Normal("Started RCON server on port " + Settings.settingsStore.rconPort);
        }

        private static void AcceptTcpClient(IAsyncResult ar)
        {
            if (!_running)
                return;

            TcpClient tcpClient = _tcpListener.EndAcceptTcpClient(ar);
            RCONClient client = new RCONClient(tcpClient);

            DarkLog.Normal("RCON connection from " + ((IPEndPoint)client.TcpClient.Client.RemoteEndPoint).Address.ToString());
            _clients.Add(client);

            // accept another client
            _tcpListener.BeginAcceptTcpClient(AcceptTcpClient, null);
        }

        public static void ProcessCommand(RCONClient client, string command)
        {
            CommandReceived?.Invoke(client, new RconCommandEventArgs()
            {
                CommandText = command,
                OriginIP = ((IPEndPoint)client.TcpClient.Client.RemoteEndPoint).Address
            });
        }
    }

    public class RCONClient
    {
        public RCONClientState State { get; set; }
        public TcpClient TcpClient;
        public IPAddress RemoteIP
        {
            get
            {
                return ((IPEndPoint)TcpClient.Client.RemoteEndPoint).Address;
            }
        }

        public RCONClient(TcpClient client)
        {
            TcpClient = client;
            State = RCONClientState.UNAUTHENTICATED;

            Thread readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "RCON Client Read Thread (" + RemoteIP.ToString() + ")"
            };
            readThread.Start();
        }

        private void ReadLoop()
        {
            while (TcpClient.Connected)
            {
                lock (TcpClient)
                {
                    List<byte> packetRaw;
                    // read data from stream
                    try
                    {
                        byte[] lengthRaw = ReadBytes(4);
                        int length = BitConverter.ToInt32(lengthRaw.ReverseIfBigEndian(), 0);
                        packetRaw = new List<byte>(lengthRaw);
                        packetRaw.AddRange(ReadBytes(length));
                    }
                    catch (Exception)   // client disconnected
                    {
                        Close();
                        return;
                    }

                    // turn data into a packet and process it 
                    HandlePacket(new RCONPacket()
                    {
                        RawData = packetRaw
                    });
                }
            }
        }

        private byte[] ReadBytes(int amount)
        {
            byte[] buffer = new byte[amount];
            int bytesRead = 0;

            while (bytesRead < amount)
            {
                int j = TcpClient.GetStream().Read(buffer, bytesRead, amount - bytesRead);

                if (j == 0) // client disconnected
                {
                    Close();
                    throw new Exception("Socket closed");
                }

                bytesRead += j;
            }

            return buffer;
        }

        public void SendPacket(RCONPacket p)
        {
            lock (TcpClient)
            {
                TcpClient.GetStream().Write(p.RawData.ToArray(), 0, p.RawData.Count);
            }
        }

        public void Close()
        {
            TcpClient.Close();
        }

        private void HandlePacket(RCONPacket packet)
        {
            switch (packet.Type)
            {
                case RCONPacketType.SERVERDATA_AUTH:
                    bool authenticated = packet.Body == Settings.settingsStore.rconPassword;

                    // send response
                    SendPacket(new RCONPacket()
                    {
                        ID = (authenticated ? packet.ID : -1),  // match packet id if pass good, otherwise -1
                        Type = RCONPacketType.SERVERDATA_AUTH_RESPONSE
                    });

                    if (authenticated)
                        State = RCONClientState.AUTHENTICATED;

                    break;
                case RCONPacketType.SERVERDATA_EXECCOMMAND: // command
                    if (State != RCONClientState.AUTHENTICATED)
                    {
                        // not authenticated
                        Close();
                        break;
                    }
                    RCON.ProcessCommand(this, packet.Body); // run command
                    DarkLog.Normal("RCON command from " + RemoteIP.ToString() + ": " + packet.Body);
                    // TODO: send command response
                    break;
            }
        }
    }

    public class RCONPacket
    {
        public List<byte> RawData
        {
            get
            {
                // create builder for packet
                List<byte> builder = new List<byte>(BitConverter.GetBytes(Size).ReverseIfBigEndian());   // packet size
                builder.AddRange(BitConverter.GetBytes(ID).ReverseIfBigEndian());   // packet id
                builder.AddRange(BitConverter.GetBytes((int)Type).ReverseIfBigEndian()); // packet type
                builder.AddRange(Encoding.ASCII.GetBytes(Body));
                builder.AddRange(new byte[2]);  // packet suffix

                return builder;
            }
            set
            {
                List<byte> buffer = value;
                int size = buffer.ReadNextInt();
                ID = buffer.ReadNextInt();
                Type = (RCONPacketType)buffer.ReadNextInt();
                Body = Encoding.ASCII.GetString(buffer.ToArray(), 0, (size - 10));  // subtract size of header + suffix
            }
        }

        public int Size
        {
            get
            {
                // https://developer.valvesoftware.com/wiki/Source_RCON_Protocol#Packet_Size
                return Encoding.ASCII.GetBytes(Body).Length + 10;
            }
        }

        public int ID { get; set; } = 0;

        public RCONPacketType Type { get; set; }

        public string Body { get; set; } = "";
    }

    public class RconCommandEventArgs : EventArgs
    {
        public string CommandText { get; set; }
        public IPAddress OriginIP { get; set; }
    }

    public enum RCONPacketType
    {
        SERVERDATA_AUTH = 3,
        SERVERDATA_AUTH_RESPONSE = 2,
        SERVERDATA_EXECCOMMAND = 2,
        SERVERDATA_RESPONSE_VALUE = 0
    }

    public enum RCONClientState
    {
        /// <summary>
        /// Clients that have not yet authenticated
        /// </summary>
        UNAUTHENTICATED,
        /// <summary>
        /// Clients that have successfully authenticated
        /// </summary>
        AUTHENTICATED,
    }
}
