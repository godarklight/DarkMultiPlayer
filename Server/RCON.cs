using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    /// <summary>
    /// A system to use SRCDS-style RCON commands.
    /// </summary>
    public static class RCON
    {
        public delegate void CommandCallback(string output);

        private static bool _running;
        private static TcpListener _tcpListener;
        private static List<RCONClient> _clients = new List<RCONClient>();

        /// <summary>
        /// Start the RCON server
        /// </summary>
        public static void Start()
        {
            if (Settings.settingsStore.rconPassword == "changeme")
            {
                DarkLog.Error("RCON password has not been changed - RCON disabled.");
                return;
            }

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

            DarkLog.Normal("RCON connection from " + client.RemoteIP.ToString());
            _clients.Add(client);

            // accept another client
            _tcpListener.BeginAcceptTcpClient(AcceptTcpClient, null);
        }
    }

    public class RCONClient
    {
        public RCONClientState State { get; set; }
        public TcpClient TcpClient { get; }
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

            // create task for asynchronous reading 
            Task readTask = new Task(ReadLoop);
            readTask.Start();
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
                    {
                        bool authenticated = packet.Body == Settings.settingsStore.rconPassword;

                        // send response
                        SendPacket(new RCONPacket()
                        {
                            ID = (authenticated ? packet.ID : -1),  // match packet id if pass good, otherwise -1
                            Type = RCONPacketType.SERVERDATA_AUTH_RESPONSE
                        });

                        if (authenticated)
                            State = RCONClientState.AUTHENTICATED;
                    }
                    break;
                case RCONPacketType.SERVERDATA_EXECCOMMAND: // command
                    {
                        if (State != RCONClientState.AUTHENTICATED)
                        {
                            // not authenticated
                            Close();
                            break;
                        }

                        // we use a wait handle because the server must always execute RCON commands in order
                        using (AutoResetEvent waitHandle = new AutoResetEvent(false))
                        {
                            DarkLog.Normal("RCON command from " + RemoteIP.ToString() + ": " + packet.Body);
                            CommandHandler.HandleServerInput(packet.Body, (string output) =>
                            {
                            // we can fit 4082 characters in a single packet
                            List<string> responses = new List<string>
                                {
                                    output.Substring(0, Math.Min(RCONPacket.MAXIMUM_BODY_LENGTH, output.Length))
                                };

                                while (output.Length - (RCONPacket.MAXIMUM_BODY_LENGTH * responses.Count) > RCONPacket.MAXIMUM_BODY_LENGTH)
                                {
                                    responses.Add(output.Substring(RCONPacket.MAXIMUM_BODY_LENGTH * responses.Count, RCONPacket.MAXIMUM_BODY_LENGTH));
                                }

                                // send the packet(s)
                                foreach (string response in responses)
                                {
                                    SendPacket(new RCONPacket()
                                    {
                                        Body = response,
                                        Type = RCONPacketType.SERVERDATA_RESPONSE_VALUE,
                                        ID = packet.ID
                                    });
                                }

                                // release wait handle
                                waitHandle.Set();
                            });
                            waitHandle.WaitOne();
                        }
                    }
                    break;
                case RCONPacketType.SERVERDATA_RESPONSE_VALUE:  // see comment below
                    {
                        // see: https://developer.valvesoftware.com/wiki/Source_RCON_Protocol#Multiple-packet_Responses
                        SendPacket(packet);
                        SendPacket(new RCONPacket()
                        {
                            Type = RCONPacketType.SERVERDATA_RESPONSE_VALUE,
                            BodyRaw = new List<byte>(new byte[] { 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 })
                        });
                    }
                    break;
            }
        }
    }

    public class RCONPacket
    {
        /// <summary>
        /// The minimum size of a packet, including the size field
        /// </summary>
        public const int PACKET_OVERHEAD = 14;

        /// <summary>
        /// The maximum size of a packet
        /// </summary>
        public const int MAXIMUM_SIZE = 4096;

        /// <summary>
        /// The maximum length of the packet body, in chars
        /// </summary>
        public const int MAXIMUM_BODY_LENGTH = MAXIMUM_SIZE - PACKET_OVERHEAD;

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

        public string Body
        {
            get
            {
                return Encoding.ASCII.GetString(BodyRaw.ToArray());
            }
            set
            {
                BodyRaw.Clear();
                BodyRaw.AddRange(Encoding.ASCII.GetBytes(value));
            }
        }

        public List<byte> BodyRaw { get; set; } = new List<byte>();
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
