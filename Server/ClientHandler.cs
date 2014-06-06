using System;
using System.Threading;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using MessageStream;
using System.Linq;
using System.IO;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    public class ClientHandler
    {
        //No point support IPv6 until KSP enables it on their windows builds.
        private static TcpListener TCPServer;
        private static Queue<ClientObject> addClients;
        private static List<ClientObject> clients;
        private static Queue<ClientObject> deleteClients;
        private static Dictionary<int, Subspace> subspaces;
        private static string modFileData;
        private static string subspaceFile = Path.Combine(Server.universeDirectory, "subspace.txt");
        private static string modFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPModControl.txt");
        private static string banlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPPlayerBans.txt");
        private static string ipBanlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPIPBans.txt");
        private static string guidBanlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPGuidBans.txt");
        private static Dictionary<string, List<string>> playerChatChannels;
        private static List<string> bannedNames;
        private static List<IPAddress> bannedIPs;
        private static List<Guid> bannedGUIDs;
        private static List<string> banReasons;
        private static Dictionary<string, int> playerUploadedScreenshotIndex;
        private static Dictionary<string, Dictionary<string,int>> playerDownloadedScreenshotIndex;
        private static Dictionary<string, string> playerWatchScreenshot;
        private static LockSystem lockSystem;
        private static object clientLock = new object();
        #region Main loop
        public static void ThreadMain()
        {
            try
            {
                addClients = new Queue<ClientObject>();
                clients = new List<ClientObject>();
                deleteClients = new Queue<ClientObject>();
                subspaces = new Dictionary<int, Subspace>();
                playerChatChannels = new Dictionary<string, List<string>>();
                bannedNames = new List<string>();
                bannedIPs = new List<IPAddress>();
                bannedGUIDs = new List<Guid>();
                banReasons = new List<string>();
                playerUploadedScreenshotIndex = new Dictionary<string, int>();
                playerDownloadedScreenshotIndex = new Dictionary<string, Dictionary <string, int>>();
                playerWatchScreenshot = new Dictionary<string, string>();
                lockSystem = new LockSystem();
                LoadSavedSubspace();
                LoadModFile();
                LoadBans();
                SetupTCPServer();

                while (Server.serverRunning)
                {
                    lock (clientLock)
                    {
                        //Add new clients
                        while (addClients.Count > 0)
                        {
                            clients.Add(addClients.Dequeue());
                            Server.playerCount = GetActiveClientCount();
                            Server.players = GetActivePlayerNames();
                        }
                        //Process current clients
                        foreach (ClientObject client in clients)
                        {
                            CheckHeartBeat(client);
                            SendOutgoingMessages(client);
                        }
                        //Check timers
                        NukeKSC.CheckTimer();
                        Dekessler.CheckTimer();
                        //Delete old clients
                        while (deleteClients.Count > 0)
                        {
                            clients.Remove(deleteClients.Dequeue());
                            Server.playerCount = GetActiveClientCount();
                            Server.players = GetActivePlayerNames();
                        }
                    }
                    Thread.Sleep(10);
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Fatal error thrown, exception: " + e);
                Server.ShutDown("Crashed!");
            }
            try
            {
                bool sendingHighPriotityMessages = true;
                while (sendingHighPriotityMessages)
                {
                    sendingHighPriotityMessages = false;
                    foreach (ClientObject client in clients)
                    {
                        if (client.authenticated)
                        {
                            if (client.sendMessageQueueHigh != null ? client.sendMessageQueueHigh.Count > 0 : false)
                            {
                                SendOutgoingHighPriorityMessages(client);
                                sendingHighPriotityMessages = true;
                            }
                        }
                    }
                    Thread.Sleep(10);
                }
                ShutdownTCPServer();
            }
            catch (Exception e)
            {
                DarkLog.Fatal("Fatal error thrown during shutdown, exception: " + e);
                throw;
            }
        }
        #endregion
        #region Server setup
        private static void LoadSavedSubspace()
        {
            try
            {
                using (StreamReader sr = new StreamReader(subspaceFile))
                {
                    //Ignore the comment line.
                    string firstLine = "";
                    while (firstLine.StartsWith("#") || String.IsNullOrEmpty(firstLine))
                    {
                        firstLine = sr.ReadLine().Trim();
                    }
                    Subspace savedSubspace = new Subspace();
                    int subspaceID = Int32.Parse(firstLine);
                    savedSubspace.serverClock = Int64.Parse(sr.ReadLine().Trim());
                    savedSubspace.planetTime = Double.Parse(sr.ReadLine().Trim());
                    savedSubspace.subspaceSpeed = Single.Parse(sr.ReadLine().Trim());
                    subspaces.Add(subspaceID, savedSubspace);
                }
            }
            catch
            {
                DarkLog.Debug("Creating new subspace lock file");
                Subspace newSubspace = new Subspace();
                newSubspace.serverClock = DateTime.UtcNow.Ticks;
                newSubspace.planetTime = 100d;
                newSubspace.subspaceSpeed = 1f;
                subspaces.Add(0, newSubspace);
                SaveSubspace(0, newSubspace);
            }
        }

        private static void LoadModFile()
        {
            try
            {
                using (StreamReader sr = new StreamReader(modFile))
                {
                    modFileData = sr.ReadToEnd();
                }
            }
            catch
            {
                DarkLog.Debug("Creating new mod control file");
                GenerateNewModFile();
            }
        }

        private static void GenerateNewModFile()
        {
            if (File.Exists(modFile))
            {
                File.Move(modFile, modFile + ".bak");
            }
            //This is the same format as KMPModControl.txt. It's a fairly sane format, and it makes sense to remain compatible.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#You can comment by starting a line with a #, these are ignored by the server.");
            sb.AppendLine("#Commenting will NOT work unless the line STARTS with a '#'.");
            sb.AppendLine("#You can also indent the file with tabs or spaces.");
            sb.AppendLine("#Sections supported are required-files, optional-files, partslist, resource-blacklist and resource-whitelist.");
            sb.AppendLine("#The client will be required to have the files found in required-files, and they must match the SHA hash if specified (this is where part mod files and play-altering files should go, like KWRocketry or Ferram Aerospace Research#The client may have the files found in optional-files, but IF they do then they must match the SHA hash (this is where mods that do not affect other players should go, like EditorExtensions or part catalogue managers");
            sb.AppendLine("#You cannot use both resource-blacklist AND resource-whitelist in the same file.");
            sb.AppendLine("#resource-blacklist bans ONLY the files you specify");
            sb.AppendLine("#resource-whitelist bans ALL resources except those specified in the resource-whitelist section OR in the SHA sections. A file listed in resource-whitelist will NOT be checked for SHA hash. This is useful if you want a mod that modifies files in its own directory as you play.");
            sb.AppendLine("#Each section has its own type of formatting. Examples have been given.");
            sb.AppendLine("#Sections are defined as follows:");
            sb.AppendLine("");
            sb.AppendLine("!required-files");
            sb.AppendLine("#To generate the SHA256 of a file you can use a utility such as this one: http://hash.online-convert.com/sha256-generator (use the 'hex' string), or use sha256sum on linux.");
            sb.AppendLine("#File paths are read from inside GameData.");
            sb.AppendLine("#If there is no SHA256 hash listed here (i.e. blank after the equals sign or no equals sign), SHA matching will not be enforced.");
            sb.AppendLine("#You may not specify multiple SHAs for the same file. Do not put spaces around equals sign. Follow the example carefully.");
            sb.AppendLine("#Syntax:");
            sb.AppendLine("#[File Path]=[SHA] or [File Path]");
            sb.AppendLine("#Example: MechJeb2/Plugins/MechJeb2.dll=B84BB63AE740F0A25DA047E5EDA35B26F6FD5DF019696AC9D6AF8FC3E031F0B9");
            sb.AppendLine("#Example: MechJeb2/Plugins/MechJeb2.dll");
            sb.AppendLine("");
            sb.AppendLine("");
            sb.AppendLine("!optional-files");
            sb.AppendLine("#Formatting for this section is the same as the 'required-files' section");
            sb.AppendLine("");
            sb.AppendLine("");
            sb.AppendLine("!resource-blacklist");
            sb.AppendLine("#!resource-whitelist");
            sb.AppendLine("#Only select one of these modes.");
            sb.AppendLine("#Resource blacklist: clients will be allowed to use any dll's, So long as they are not listed in this section");
            sb.AppendLine("#Resource whitelist: clients will only be allowed to use dll's listed here or in the 'required-files' and 'optional-files' sections.");
            sb.AppendLine("#Syntax:");
            sb.AppendLine("#[File Path]");
            sb.AppendLine("#Example: MechJeb2/Plugins/MechJeb2.dll");
            sb.AppendLine("");
            sb.AppendLine("");
            sb.AppendLine("!partslist");
            sb.AppendLine("#This is a list of parts to allow users to put on their ships.");
            sb.AppendLine("#If a part the client has doesn't appear on this list, they can still join the server but not use the part.");
            sb.AppendLine("#The default stock parts have been added already for you.");
            sb.AppendLine("#To add a mod part, add the name from the part's .cfg file. The name is the name from the PART{} section, where underscores are replaced with periods.");
            sb.AppendLine("#[partname]");
            sb.AppendLine("#Example: mumech.MJ2.Pod (NOTE: In the part.cfg this MechJeb2 pod is named mumech_MJ2_Pod. The _ have been replaced with .)");
            sb.AppendLine("#You can use this application to generate partlists from a KSP installation if you want to add mod parts: http://forum.kerbalspaceprogram.com/threads/57284 ");
            sb.AppendLine("");
            List<string> partList = GetStockParts();
            foreach (string partLine in partList)
            {
                sb.AppendLine(partLine);
            }
            using (StreamWriter sw = new StreamWriter(modFile))
            {
                modFileData = sb.ToString();
                sw.Write(modFileData);
            }
        }

        private static List<string> GetStockParts()
        {
            List<string> stockPartList = new List<string>();
            stockPartList.Add("StandardCtrlSrf");
            stockPartList.Add("CanardController");
            stockPartList.Add("noseCone");
            stockPartList.Add("AdvancedCanard");
            stockPartList.Add("airplaneTail");
            stockPartList.Add("deltaWing");
            stockPartList.Add("noseConeAdapter");
            stockPartList.Add("rocketNoseCone");
            stockPartList.Add("smallCtrlSrf");
            stockPartList.Add("standardNoseCone");
            stockPartList.Add("sweptWing");
            stockPartList.Add("tailfin");
            stockPartList.Add("wingConnector");
            stockPartList.Add("winglet");
            stockPartList.Add("R8winglet");
            stockPartList.Add("winglet3");
            stockPartList.Add("Mark1Cockpit");
            stockPartList.Add("Mark2Cockpit");
            stockPartList.Add("Mark1-2Pod");
            stockPartList.Add("advSasModule");
            stockPartList.Add("asasmodule1-2");
            stockPartList.Add("avionicsNoseCone");
            stockPartList.Add("crewCabin");
            stockPartList.Add("cupola");
            stockPartList.Add("landerCabinSmall");
            stockPartList.Add("mark3Cockpit");
            stockPartList.Add("mk1pod");
            stockPartList.Add("mk2LanderCabin");
            stockPartList.Add("probeCoreCube");
            stockPartList.Add("probeCoreHex");
            stockPartList.Add("probeCoreOcto");
            stockPartList.Add("probeCoreOcto2");
            stockPartList.Add("probeCoreSphere");
            stockPartList.Add("probeStackLarge");
            stockPartList.Add("probeStackSmall");
            stockPartList.Add("sasModule");
            stockPartList.Add("seatExternalCmd");
            stockPartList.Add("rtg");
            stockPartList.Add("batteryBank");
            stockPartList.Add("batteryBankLarge");
            stockPartList.Add("batteryBankMini");
            stockPartList.Add("batteryPack");
            stockPartList.Add("ksp.r.largeBatteryPack");
            stockPartList.Add("largeSolarPanel");
            stockPartList.Add("solarPanels1");
            stockPartList.Add("solarPanels2");
            stockPartList.Add("solarPanels3");
            stockPartList.Add("solarPanels4");
            stockPartList.Add("solarPanels5");
            stockPartList.Add("JetEngine");
            stockPartList.Add("engineLargeSkipper");
            stockPartList.Add("ionEngine");
            stockPartList.Add("liquidEngine");
            stockPartList.Add("liquidEngine1-2");
            stockPartList.Add("liquidEngine2");
            stockPartList.Add("liquidEngine2-2");
            stockPartList.Add("liquidEngine3");
            stockPartList.Add("liquidEngineMini");
            stockPartList.Add("microEngine");
            stockPartList.Add("nuclearEngine");
            stockPartList.Add("radialEngineMini");
            stockPartList.Add("radialLiquidEngine1-2");
            stockPartList.Add("sepMotor1");
            stockPartList.Add("smallRadialEngine");
            stockPartList.Add("solidBooster");
            stockPartList.Add("solidBooster1-1");
            stockPartList.Add("toroidalAerospike");
            stockPartList.Add("turboFanEngine");
            stockPartList.Add("MK1Fuselage");
            stockPartList.Add("Mk1FuselageStructural");
            stockPartList.Add("RCSFuelTank");
            stockPartList.Add("RCSTank1-2");
            stockPartList.Add("rcsTankMini");
            stockPartList.Add("rcsTankRadialLong");
            stockPartList.Add("fuelTank");
            stockPartList.Add("fuelTank1-2");
            stockPartList.Add("fuelTank2-2");
            stockPartList.Add("fuelTank3-2");
            stockPartList.Add("fuelTank4-2");
            stockPartList.Add("fuelTankSmall");
            stockPartList.Add("fuelTankSmallFlat");
            stockPartList.Add("fuelTank.long");
            stockPartList.Add("miniFuelTank");
            stockPartList.Add("mk2Fuselage");
            stockPartList.Add("mk2SpacePlaneAdapter");
            stockPartList.Add("mk3Fuselage");
            stockPartList.Add("mk3spacePlaneAdapter");
            stockPartList.Add("radialRCSTank");
            stockPartList.Add("toroidalFuelTank");
            stockPartList.Add("xenonTank");
            stockPartList.Add("xenonTankRadial");
            stockPartList.Add("adapterLargeSmallBi");
            stockPartList.Add("adapterLargeSmallQuad");
            stockPartList.Add("adapterLargeSmallTri");
            stockPartList.Add("adapterSmallMiniShort");
            stockPartList.Add("adapterSmallMiniTall");
            stockPartList.Add("nacelleBody");
            stockPartList.Add("radialEngineBody");
            stockPartList.Add("smallHardpoint");
            stockPartList.Add("stationHub");
            stockPartList.Add("structuralIBeam1");
            stockPartList.Add("structuralIBeam2");
            stockPartList.Add("structuralIBeam3");
            stockPartList.Add("structuralMiniNode");
            stockPartList.Add("structuralPanel1");
            stockPartList.Add("structuralPanel2");
            stockPartList.Add("structuralPylon");
            stockPartList.Add("structuralWing");
            stockPartList.Add("strutConnector");
            stockPartList.Add("strutCube");
            stockPartList.Add("strutOcto");
            stockPartList.Add("trussAdapter");
            stockPartList.Add("trussPiece1x");
            stockPartList.Add("trussPiece3x");
            stockPartList.Add("CircularIntake");
            stockPartList.Add("landingLeg1");
            stockPartList.Add("landingLeg1-2");
            stockPartList.Add("RCSBlock");
            stockPartList.Add("stackDecoupler");
            stockPartList.Add("airScoop");
            stockPartList.Add("commDish");
            stockPartList.Add("decoupler1-2");
            stockPartList.Add("dockingPort1");
            stockPartList.Add("dockingPort2");
            stockPartList.Add("dockingPort3");
            stockPartList.Add("dockingPortLarge");
            stockPartList.Add("dockingPortLateral");
            stockPartList.Add("fuelLine");
            stockPartList.Add("ladder1");
            stockPartList.Add("largeAdapter");
            stockPartList.Add("largeAdapter2");
            stockPartList.Add("launchClamp1");
            stockPartList.Add("linearRcs");
            stockPartList.Add("longAntenna");
            stockPartList.Add("miniLandingLeg");
            stockPartList.Add("parachuteDrogue");
            stockPartList.Add("parachuteLarge");
            stockPartList.Add("parachuteRadial");
            stockPartList.Add("parachuteSingle");
            stockPartList.Add("radialDecoupler");
            stockPartList.Add("radialDecoupler1-2");
            stockPartList.Add("radialDecoupler2");
            stockPartList.Add("ramAirIntake");
            stockPartList.Add("roverBody");
            stockPartList.Add("sensorAccelerometer");
            stockPartList.Add("sensorBarometer");
            stockPartList.Add("sensorGravimeter");
            stockPartList.Add("sensorThermometer");
            stockPartList.Add("spotLight1");
            stockPartList.Add("spotLight2");
            stockPartList.Add("stackBiCoupler");
            stockPartList.Add("stackDecouplerMini");
            stockPartList.Add("stackPoint1");
            stockPartList.Add("stackQuadCoupler");
            stockPartList.Add("stackSeparator");
            stockPartList.Add("stackSeparatorBig");
            stockPartList.Add("stackSeparatorMini");
            stockPartList.Add("stackTriCoupler");
            stockPartList.Add("telescopicLadder");
            stockPartList.Add("telescopicLadderBay");
            stockPartList.Add("SmallGearBay");
            stockPartList.Add("roverWheel1");
            stockPartList.Add("roverWheel2");
            stockPartList.Add("roverWheel3");
            stockPartList.Add("wheelMed");
            stockPartList.Add("flag");
            stockPartList.Add("kerbalEVA");
            stockPartList.Add("mediumDishAntenna");
            stockPartList.Add("GooExperiment");
            stockPartList.Add("science.module");
            stockPartList.Add("RAPIER");
            stockPartList.Add("Large.Crewed.Lab");
            //0.23.5 parts
            stockPartList.Add("GrapplingDevice");
            stockPartList.Add("LaunchEscapeSystem");
            stockPartList.Add("MassiveBooster");
            stockPartList.Add("PotatoRoid");
            stockPartList.Add("Size2LFB");
            stockPartList.Add("Size3AdvancedEngine");
            stockPartList.Add("size3Decoupler");
            stockPartList.Add("Size3EngineCluster");
            stockPartList.Add("Size3LargeTank");
            stockPartList.Add("Size3MediumTank");
            stockPartList.Add("Size3SmallTank");
            stockPartList.Add("Size3to2Adapter");
            return stockPartList;
        }

        private static int GetLatestSubspace()
        {
            int latestID = 0;
            double latestPlanetTime = 0;
            long currentTime = DateTime.UtcNow.Ticks;
            foreach (KeyValuePair<int,Subspace> subspace in subspaces)
            {
                double currentPlanetTime = subspace.Value.planetTime + (((currentTime - subspace.Value.serverClock) / 10000000) * subspace.Value.subspaceSpeed);
                if (currentPlanetTime > latestPlanetTime)
                {
                    latestID = subspace.Key;
                }
            }
            return latestID;
        }

        private static void SaveLatestSubspace()
        {
            int latestID = GetLatestSubspace();
            SaveSubspace(latestID, subspaces[latestID]);
        }

        private static void SaveSubspace(int subspaceID, Subspace subspace)
        {
            string subspaceFile = Path.Combine(Server.universeDirectory, "subspace.txt");
            using (StreamWriter sw = new StreamWriter(subspaceFile))
            {
                sw.WriteLine("#Incorrectly editing this file will cause weirdness. If there is any errors, the universe time will be reset.");
                sw.WriteLine("#This file can only be edited if the server is stopped.");
                sw.WriteLine("#Each variable is on a new line. They are subspaceID, server clock (from DateTime.UtcNow.Ticks), universe time, and subspace speed.");
                sw.WriteLine(subspaceID);
                sw.WriteLine(subspace.serverClock);
                sw.WriteLine(subspace.planetTime);
                sw.WriteLine(subspace.subspaceSpeed);
            }
        }

        private static void SetupTCPServer()
        {
            try
            {
                TCPServer = new TcpListener(new IPEndPoint(IPAddress.Any, Settings.settingsStore.port));
                TCPServer.Start(4);
                TCPServer.BeginAcceptTcpClient(new AsyncCallback(NewClientCallback), null);
            }
            catch (Exception e)
            {
                DarkLog.Normal("Error setting up server, Exception: " + e);
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
                    DarkLog.Normal("New client connection from " + newClient.Client.RemoteEndPoint);
                }
                catch
                {
                    DarkLog.Normal("Error accepting client!");
                }
                TCPServer.BeginAcceptTcpClient(new AsyncCallback(NewClientCallback), null);
            }
        }

        private static void SetupClient(TcpClient newClientConnection)
        {
            ClientObject newClientObject = new ClientObject();
            newClientObject.subspace = GetLatestSubspace();
            newClientObject.playerStatus = new PlayerStatus();
            newClientObject.connectionStatus = ConnectionStatus.CONNECTED;
            newClientObject.playerName = "Unknown";
            newClientObject.activeVessel = "";
            newClientObject.subspaceRate = 1f;
            newClientObject.endpoint = newClientConnection.Client.RemoteEndPoint.ToString();
            newClientObject.ipAddress = (newClientConnection.Client.RemoteEndPoint as IPEndPoint).Address;
            newClientObject.GUID = Guid.Empty;
            //Keep the connection reference
            newClientObject.connection = newClientConnection;
            //Add the queues
            newClientObject.sendMessageQueueHigh = new Queue<ServerMessage>();
            newClientObject.sendMessageQueueSplit = new Queue<ServerMessage>();
            newClientObject.sendMessageQueueLow = new Queue<ServerMessage>();
            newClientObject.receiveMessageQueue = new Queue<ClientMessage>();
            newClientObject.sendLock = new object();
            StartReceivingIncomingMessages(newClientObject);
            addClients.Enqueue(newClientObject);
        }

        private static void SaveBans()
        {
            try
            {
                if (File.Exists(banlistFile))
                {
                    File.SetAttributes(banlistFile, FileAttributes.Normal);
                }

                using (StreamWriter sw = new StreamWriter(banlistFile))
                {

                    foreach (string name in bannedNames)
                    {
                        sw.WriteLine("{0}", name);
                    }
                }

                using (StreamWriter sw = new StreamWriter(ipBanlistFile))
                {
                    foreach (IPAddress ip in bannedIPs)
                    {
                        sw.WriteLine("{0}", ip);
                    }
                }

                using (StreamWriter sw = new StreamWriter(guidBanlistFile))
                {
                    foreach (Guid guid in bannedGUIDs)
                    {
                        sw.WriteLine("{0}", guid);
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Error saving bans!, Exception: " + e);
            }
        }

        private static void LoadBans()
        {
            DarkLog.Debug("Loading bans");

            bannedNames.Clear();
            bannedIPs.Clear();
            bannedGUIDs.Clear();
            banReasons.Clear();

            if (File.Exists(banlistFile))
            {
                foreach (string line in File.ReadAllLines(banlistFile))
                {
                    if (!bannedNames.Contains(line))
                    {
                        bannedNames.Add(line);
                    }
                }
            }
            else
            {
                File.Create(banlistFile);
            }

            if (File.Exists(ipBanlistFile))
            {
                foreach (string line in File.ReadAllLines(ipBanlistFile))
                {
                    IPAddress banIPAddr = null;
                    if (IPAddress.TryParse(line, out banIPAddr))
                    {
                        if (!bannedIPs.Contains(banIPAddr))
                        {
                            bannedIPs.Add(banIPAddr);
                        }
                    }
                    else
                    {
                        DarkLog.Error("Error in IP ban list file, " + line + " is not an IP address");
                    }
                }
            }
            else
            {
                File.Create(ipBanlistFile);
            }

            if (File.Exists(guidBanlistFile))
            {
                foreach (string line in File.ReadAllLines(guidBanlistFile))
                {
                    Guid bannedGUID = Guid.Empty;
                    if (Guid.TryParse(line, out bannedGUID))
                    {
                        if (!bannedGUIDs.Contains(bannedGUID))
                        {
                            bannedGUIDs.Add(bannedGUID);
                        }
                    }
                    else
                    {
                        DarkLog.Error("Error in GUID ban list file, " + line + " is not an valid player token");
                    }
                }
            }
            else
            {
                File.Create(guidBanlistFile);
            }
        }

        private static int GetActiveClientCount()
        {
            lock (clientLock)
            {
                return clients.Where(c => c.authenticated).Count();
            }
        }

        private static string GetActivePlayerNames()
        {
            string playerString = "";
            lock (clientLock)
            {
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
            }
            return playerString;
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

        private static void SendOutgoingMessages(ClientObject client)
        {
            lock (client.sendLock)
            {
                if (!client.isSendingToClient)
                {
                    ServerMessage message = null;
                    if (message == null && client.sendMessageQueueHigh.Count > 0)
                    {
                        message = client.sendMessageQueueHigh.Dequeue();
                    }
                    if (message == null && client.sendMessageQueueSplit.Count > 0)
                    {
                        message = client.sendMessageQueueSplit.Dequeue();
                    }
                    if (message == null && client.sendMessageQueueLow.Count > 0)
                    {
                        message = client.sendMessageQueueLow.Dequeue();
                        //Splits large messages to higher priority messages can get into the queue faster
                        SplitAndRewriteMessage(client, ref message);
                    }
                    if (message != null)
                    {
                        SendNetworkMessage(client, message);
                    }
                }
            }
        }

        private static void SendOutgoingHighPriorityMessages(ClientObject client)
        {
            if (!client.isSendingToClient)
            {
                ServerMessage message = null;
                if (client.sendMessageQueueHigh.Count > 0)
                {
                    message = client.sendMessageQueueHigh.Dequeue();
                }
                if (message != null)
                {
                    SendNetworkMessage(client, message);
                }
            }
        }

        private static void SplitAndRewriteMessage(ClientObject client, ref ServerMessage message)
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
                ServerMessage newSplitMessage = new ServerMessage();
                newSplitMessage.type = ServerMessageType.SPLIT_MESSAGE;
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
                    client.sendMessageQueueSplit.Enqueue(newSplitMessage);
                }

                int currentSplits = 1;

                while (splitBytesLeft > 0)
                {
                    ServerMessage currentSplitMessage = new ServerMessage();
                    currentSplitMessage.type = ServerMessageType.SPLIT_MESSAGE;
                    currentSplitMessage.data = new byte[Math.Min(splitBytesLeft, Common.SPLIT_MESSAGE_LENGTH)];
                    Array.Copy(message.data, message.data.Length - splitBytesLeft, currentSplitMessage.data, 0, currentSplitMessage.data.Length);
                    splitBytesLeft -= currentSplitMessage.data.Length;
                    currentSplits++;
                    client.sendMessageQueueSplit.Enqueue(currentSplitMessage);
                }
                message = client.sendMessageQueueSplit.Dequeue();
            }
        }

        private static void SendNetworkMessage(ClientObject client, ServerMessage message)
        {
            //Write the send times down in SYNC_TIME_REPLY packets
            if (message.type == ServerMessageType.SYNC_TIME_REPLY)
            {
                try
                {
                    using (MessageWriter mw = new MessageWriter())
                    {
                        using (MessageReader mr = new MessageReader(message.data, false))
                        {
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
                catch (Exception e)
                {
                    DarkLog.Debug("Error rewriting SYNC_TIME packet, Exception " + e);
                }
            }
            //Continue sending
            byte[] messageBytes;
            using (MessageWriter mw = new MessageWriter((int)message.type))
            {
                if (message.data != null)
                {
                    mw.Write<byte[]>(message.data);
                }
                messageBytes = mw.GetMessageBytes();
            }
            client.isSendingToClient = true;
            client.lastSendTime = Server.serverClock.ElapsedMilliseconds;
            if (client.connectionStatus == ConnectionStatus.CONNECTED)
            {
                try
                {
                    client.connection.GetStream().BeginWrite(messageBytes, 0, messageBytes.Length, new AsyncCallback(SendMessageCallback), client);
                }
                catch (Exception e)
                {
                    DarkLog.Normal("Client " + client.playerName + " disconnected, endpoint " + client.endpoint + " error: " + e.Message);
                    DisconnectClient(client);
                }
            }
            if (message.type == ServerMessageType.CONNECTION_END)
            {
                DarkLog.Normal("Client " + client.playerName + " disconnected, sent CONNECTION_END to endpoint " + client.endpoint);
                DisconnectClient(client);
            }
            if (message.type == ServerMessageType.HANDSHAKE_REPLY)
            {
                using (MessageReader mr = new MessageReader(message.data, false))
                {
                    int response = mr.Read<int>();
                    string reason = mr.Read<string>();
                    if (response != 0)
                    {
                        DarkLog.Normal("Client " + client.playerName + " disconnected, sent HANDSHAKE REPLY (" + reason + ") to endpoint " + client.endpoint);
                        DisconnectClient(client);
                    }
                }
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
                DarkLog.Normal("Client " + client.playerName + " disconnected, endpoint " + client.endpoint + ", error: " + e.Message);
                DisconnectClient(client);
            }
            client.isSendingToClient = false;
            SendOutgoingMessages(client);
        }

        private static void StartReceivingIncomingMessages(ClientObject client)
        {
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
                DarkLog.Normal("Connection error: " + e.Message);
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
                            if (mr.GetMessageType() > (Enum.GetNames(typeof(ClientMessageType)).Length - 1))
                            {
                                //Malformed message, most likely from a non DMP-client.
                                SendConnectionEnd(client, "Invalid DMP message. Disconnected.");
                                DarkLog.Normal("Invalid DMP message from " + client.endpoint);
                                //Returning from ReceiveCallback will break the receive loop and stop processing any further messages.
                                return;
                            }
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
                                if (length < Common.MAX_MESSAGE_SIZE)
                                {
                                    client.isReceivingMessage = true;
                                    client.receiveMessage.data = new byte[length];
                                    client.receiveMessageBytesLeft = client.receiveMessage.data.Length;
                                }
                                else
                                {
                                    //Malformed message, most likely from a non DMP-client.
                                    SendConnectionEnd(client, "Invalid DMP message. Disconnected.");
                                    DarkLog.Normal("Invalid DMP message from " + client.endpoint);
                                    //Returning from ReceiveCallback will break the receive loop and stop processing any further messages.
                                    return;
                                }
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
                if (client.connectionStatus == ConnectionStatus.CONNECTED)
                {
                    client.lastReceiveTime = Server.serverClock.ElapsedMilliseconds;
                    client.connection.GetStream().BeginRead(client.receiveMessage.data, client.receiveMessage.data.Length - client.receiveMessageBytesLeft, client.receiveMessageBytesLeft, new AsyncCallback(ReceiveCallback), client);
                }
            }
            catch (Exception e)
            {
                DarkLog.Normal("Connection error: " + e.Message);
                DisconnectClient(client);
            }
        }

        private static void DisconnectClient(ClientObject client)
        {
            if (client.connectionStatus != ConnectionStatus.DISCONNECTED)
            {
                if (client.playerName != null)
                {
                    if (playerChatChannels.ContainsKey(client.playerName))
                    {
                        playerChatChannels.Remove(client.playerName);
                    }
                    if (playerDownloadedScreenshotIndex.ContainsKey(client.playerName))
                    {
                        playerDownloadedScreenshotIndex.Remove(client.playerName);
                    }
                    if (playerUploadedScreenshotIndex.ContainsKey(client.playerName))
                    {
                        playerUploadedScreenshotIndex.Remove(client.playerName);
                    }
                    if (playerWatchScreenshot.ContainsKey(client.playerName))
                    {
                        playerWatchScreenshot.Remove(client.playerName);
                    }
                }
                client.connectionStatus = ConnectionStatus.DISCONNECTED;
                if (client.authenticated)
                {
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.PLAYER_DISCONNECT;
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<string>(client.playerName);
                        newMessage.data = mw.GetMessageBytes();
                    }
                    SendToAll(client, newMessage, true);
                    lockSystem.ReleasePlayerLocks(client.playerName);
                }
                deleteClients.Enqueue(client);
                if (client.connection != null)
                {
                    client.connection.Close();
                }
            }
        }
        #endregion
        #region Message handling
        private static void HandleMessage(ClientObject client, ClientMessage message)
        {
            //Clients can only send HEARTBEATS, HANDSHAKE_REQUEST or CONNECTION_END's until they are authenticated.
            if (!client.authenticated && !(message.type == ClientMessageType.HEARTBEAT || message.type == ClientMessageType.HANDSHAKE_REQUEST || message.type == ClientMessageType.CONNECTION_END))
            {
                SendConnectionEnd(client, "You must authenticate before attempting to send a " + message.type.ToString() + " message");
                return;
            }

            try
            {
                switch (message.type)
                {
                    case ClientMessageType.HEARTBEAT:
                        //Don't do anything for heartbeats, they just keep the connection alive
                        break;
                    case ClientMessageType.HANDSHAKE_REQUEST:
                        HandleHandshakeRequest(client, message.data);
                        break;
                    case ClientMessageType.CHAT_MESSAGE:
                        HandleChatMessage(client, message.data);
                        break;
                    case ClientMessageType.PLAYER_STATUS:
                        HandlePlayerStatus(client, message.data);
                        break;
                    case ClientMessageType.SCENARIO_DATA:
                        HandleScenarioModuleData(client, message.data);
                        break;
                    case ClientMessageType.SYNC_TIME_REQUEST:
                        HandleSyncTimeRequest(client, message.data);
                        break;
                    case ClientMessageType.KERBALS_REQUEST:
                        HandleKerbalsRequest(client);
                        break;
                    case ClientMessageType.KERBAL_PROTO:
                        HandleKerbalProto(client, message.data);
                        break;
                    case ClientMessageType.VESSELS_REQUEST:
                        HandleVesselsRequest(client, message.data);
                        break;
                    case ClientMessageType.VESSEL_PROTO:
                        HandleVesselProto(client, message.data);
                        break;
                    case ClientMessageType.VESSEL_UPDATE:
                        HandleVesselUpdate(client, message.data);
                        break;
                    case ClientMessageType.VESSEL_REMOVE:
                        HandleVesselRemoval(client, message.data);
                        break;
                    case ClientMessageType.CRAFT_LIBRARY:
                        HandleCraftLibrary(client, message.data);
                        break;
                    case ClientMessageType.SCREENSHOT_LIBRARY:
                        HandleScreenshotLibrary(client, message.data);
                        break;
                    case ClientMessageType.PING_REQUEST:
                        HandlePingRequest(client, message.data);
                        break;
                    case ClientMessageType.MOTD_REQUEST:
                        HandleMotdRequest(client);  
                        break;
                    case ClientMessageType.WARP_CONTROL:
                        HandleWarpControl(client, message.data);
                        break;
                    case ClientMessageType.LOCK_SYSTEM:
                        HandleLockSystemMessage(client, message.data);
                        break;
                    case ClientMessageType.SPLIT_MESSAGE:
                        HandleSplitMessage(client, message.data);
                        break;
                    case ClientMessageType.CONNECTION_END:
                        HandleConnectionEnd(client, message.data);
                        break;
                    default:
                        DarkLog.Debug("Unhandled message type " + message.type);
                        SendConnectionEnd(client, "Unhandled message type " + message.type);
                        break;
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling " + message.type + " from " + client.playerName + ", exception: " + e);
                SendConnectionEnd(client, "Server failed to process " + message.type + " message");
            }
        }

        private static void HandleHandshakeRequest(ClientObject client, byte[] messageData)
        {
            int protocolVersion;
            string playerName = "";
            string playerGuid = Guid.Empty.ToString();
            string reason = "";
            //0 - Success
            int handshakeReponse = 0;
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    protocolVersion = mr.Read<int>();
                    playerName = mr.Read<string>();
                    playerGuid = mr.Read<string>();
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error in HANDSHAKE_REQUEST from " + client.playerName + ": " + e);
                SendHandshakeReply(client, 99, "Malformed handshake");
                return;
            }
            if (protocolVersion != Common.PROTOCOL_VERSION)
            {
                //Protocol mismatch
                handshakeReponse = 1;
                reason = "Protocol mismatch";
            }
            if (handshakeReponse == 0)
            {
                //Check client isn't already connected
                ClientObject testClient = GetClientByName(playerName);
                if (testClient != null)
                {
                    SendHeartBeat(testClient);
                    Thread.Sleep(1000);
                }
                if (clients.Contains(testClient))
                {
                    handshakeReponse = 2;
                    reason = "Client already connected";
                }
            }
            if (handshakeReponse == 0)
            {
                bool reserveKick = false;
                //Check the client isn't using a reserved name
                if (playerName == "Initial")
                {
                    reserveKick = true;
                }
                if (playerName == Settings.settingsStore.consoleIdentifier)
                {
                    reserveKick = true;
                }
                if (reserveKick)
                {
                    handshakeReponse = 3;
                    reason = "Kicked for using a reserved name";
                }
            }
            if (handshakeReponse == 0)
            {
                //Check the client matches any database entry
                string storedPlayerFile = Path.Combine(Server.universeDirectory, "Players", playerName + ".txt");
                string storedPlayerGuid = "";
                if (File.Exists(storedPlayerFile))
                {
                    using (StreamReader sr = new StreamReader(storedPlayerFile))
                    {
                        storedPlayerGuid = sr.ReadLine();
                    }
                    if (playerGuid != storedPlayerGuid)
                    {
                        handshakeReponse = 4;
                        reason = "Invalid player token for user";
                    }
                }
                else
                {
                    DarkLog.Debug("Client " + playerName + " registered!");
                    using (StreamWriter sw = new StreamWriter(storedPlayerFile))
                    {
                        sw.WriteLine(playerGuid);
                    }
                }
            }

            client.playerName = playerName;
            client.GUID = Guid.Parse(playerGuid);

            if (handshakeReponse == 0)
            {
                if (bannedNames.Contains(client.playerName) || bannedIPs.Contains(client.ipAddress) || bannedGUIDs.Contains(client.GUID))
                {
                    handshakeReponse = 5;
                    reason = "You were banned from the server!";
                }
            }

            if (handshakeReponse == 0)
            {
                if (GetActiveClientCount() >= Settings.settingsStore.maxPlayers)
                {
                    handshakeReponse = 6;
                    reason = "Server is full";
                }
            }

            if (handshakeReponse == 0)
            {
                client.authenticated = true;
                DarkLog.Normal("Client " + playerName + " handshook successfully!");
                //SEND ALL THE THINGS!

                if (!Directory.Exists(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)))
                {
                    Directory.CreateDirectory(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName));
                    foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", "Initial")))
                    {
                        File.Copy(file, Path.Combine(Server.universeDirectory, "Scenarios", playerName, Path.GetFileName(file)));
                    }
                }
                SendHandshakeReply(client, handshakeReponse, "success");
                Server.playerCount = GetActiveClientCount();
                Server.players = GetActivePlayerNames();
            }
            else
            {
                DarkLog.Normal("Client " + playerName + " failed to handshake: " + reason);
                SendHandshakeReply(client, handshakeReponse, reason);
            }


        }

        private static void HandleChatMessage(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            newMessage.data = messageData;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                ChatMessageType messageType = (ChatMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                if (fromPlayer != client.playerName)
                {
                    SendConnectionEnd(client, "Kicked for sending a chat message for another player");
                    return;
                }
                switch (messageType)
                {
                    case ChatMessageType.JOIN:
                        {
                            string joinChannel = mr.Read<string>();
                            if (!playerChatChannels.ContainsKey(fromPlayer))
                            {
                                playerChatChannels.Add(fromPlayer, new List<string>());
                            }
                            if (!playerChatChannels[fromPlayer].Contains(joinChannel))
                            {
                                playerChatChannels[fromPlayer].Add(joinChannel);
                            }
                            DarkLog.Debug(fromPlayer + " joined channel: " + joinChannel);
                        }
                        SendToAll(client, newMessage, true);
                        break;
                    case ChatMessageType.LEAVE:
                        {
                            string leaveChannel = mr.Read<string>();
                            if (playerChatChannels.ContainsKey(fromPlayer))
                            {
                                if (playerChatChannels[fromPlayer].Contains(leaveChannel))
                                {
                                    playerChatChannels[fromPlayer].Remove(leaveChannel);
                                }
                                if (playerChatChannels[fromPlayer].Count == 0)
                                {
                                    playerChatChannels.Remove(fromPlayer);
                                }
                            }
                            DarkLog.Debug(fromPlayer + " left channel: " + leaveChannel);
                        }
                        SendToAll(client, newMessage, true);
                        break;
                    case ChatMessageType.CHANNEL_MESSAGE:
                        {
                            string channel = mr.Read<string>();
                            string message = mr.Read<string>();
                            if (channel != "")
                            {
                                foreach (KeyValuePair<string, List<string>> playerEntry in playerChatChannels)
                                {
                                    if (playerEntry.Value.Contains(channel))
                                    {
                                        ClientObject findClient = GetClientByName(playerEntry.Key);
                                        if (findClient != null)
                                        {
                                            SendToClient(findClient, newMessage, true);
                                        }
                                    }
                                }
                                DarkLog.ChatMessage(fromPlayer + " -> #" + channel + ": " + message);
                            }
                            else
                            {
                                SendToClient(client, newMessage, true);
                                SendToAll(client, newMessage, true);
                                DarkLog.ChatMessage(fromPlayer + " -> #Global: " + message);
                            }
                        }
                        break;
                    case ChatMessageType.PRIVATE_MESSAGE:
                        {
                            string toPlayer = mr.Read<string>();
                            string message = mr.Read<string>();
                            if (toPlayer != Settings.settingsStore.consoleIdentifier)
                            {
                                ClientObject findClient = GetClientByName(toPlayer);
                                if (findClient != null)
                                {
                                    SendToClient(client, newMessage, true);
                                    SendToClient(findClient, newMessage, true);
                                    DarkLog.ChatMessage(fromPlayer + " -> @" + toPlayer + ": " + message);
                                }
                                {
                                    DarkLog.ChatMessage(fromPlayer + " -X-> @" + toPlayer + ": " + message);
                                }
                            }
                            else
                            {
                                SendToClient(client, newMessage, true);
                                DarkLog.ChatMessage(fromPlayer + " -> @" + toPlayer + ": " + message);
                            }
                        }
                        break;
                }
            }
        }

        private static void HandleSyncTimeRequest(ClientObject client, byte[] messageData)
        {
            try
            {
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.SYNC_TIME_REPLY;
                using (MessageWriter mw = new MessageWriter())
                {
                    using (MessageReader mr = new MessageReader(messageData, false))
                    {
                        //Client send time
                        mw.Write<long>(mr.Read<long>());
                        //Server receive time
                        mw.Write<long>(DateTime.UtcNow.Ticks);
                        newMessage.data = mw.GetMessageBytes();
                    }
                }
                SendToClient(client, newMessage, true);

            }
            catch (Exception e)
            {
                DarkLog.Debug("Error in SYNC_TIME_REQUEST from " + client.playerName + ": " + e);
                DisconnectClient(client);
            }
        }

        private static void HandlePlayerStatus(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                string playerName = mr.Read<string>();
                if (playerName != client.playerName)
                {
                    DarkLog.Debug(client.playerName + " tried to send an update for " + playerName + ", kicking.");
                    SendConnectionEnd(client, "Kicked for sending an update for another player");
                    return;
                }
                client.playerStatus.vesselText = mr.Read<string>();
                client.playerStatus.statusText = mr.Read<string>();
            }
            //Relay the message
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.PLAYER_STATUS;
            newMessage.data = messageData;
            SendToAll(client, newMessage, false);
        }

        private static void HandleScenarioModuleData(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //Don't care about subspace / send time.
                string[] scenarioName = mr.Read<string[]>();
                string[] scenarioData = mr.Read<string[]>();
                DarkLog.Debug("Saving " + scenarioName.Length + " scenario modules from " + client.playerName);

                for (int i = 0; i < scenarioName.Length; i++)
                {
                    using (StreamWriter sw = new StreamWriter(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName, scenarioName[i] + ".txt")))
                    {
                        sw.Write(scenarioData[i]);
                    }
                }
            }
        }

        private static void HandleKerbalsRequest(ClientObject client)
        {
            //The time sensitive SYNC_TIME is over by this point.
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(client.playerName);
                ServerMessage joinMessage = new ServerMessage();
                joinMessage.type = ServerMessageType.PLAYER_JOIN;
                joinMessage.data = mw.GetMessageBytes();
                SendToAll(client, joinMessage, true);
            }
            SendServerSettings(client);
            SendSetSubspace(client);
            SendAllSubspaces(client);
            SendAllPlayerStatus(client);
            SendScenarioModules(client);
            SendAllReportedSkewRates(client);
            SendCraftList(client);
            SendPlayerChatChannels(client);
            SendAllLocks(client);
            //Send kerbals
            int kerbalCount = 0;
            while (File.Exists(Path.Combine(Server.universeDirectory, "Kerbals", kerbalCount + ".txt")))
            {
                string kerbalFile = Path.Combine(Server.universeDirectory, "Kerbals", kerbalCount + ".txt");
                byte[] kerbalData = File.ReadAllBytes(kerbalFile);
                SendKerbal(client, kerbalCount, kerbalData);
                kerbalCount++;
            }
            DarkLog.Debug("Sending " + client.playerName + " " + kerbalCount + " kerbals...");
            SendKerbalsComplete(client);
        }

        private static void HandleKerbalProto(ClientObject client, byte[] messageData)
        {
            //Send kerbal
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //Don't care about subspace / send time.
                mr.Read<int>();
                mr.Read<double>();
                int kerbalID = mr.Read<int>();
                DarkLog.Debug("Saving kerbal " + kerbalID + " from " + client.playerName);
                byte[] kerbalData = mr.Read<byte[]>();
                File.WriteAllBytes(Path.Combine(Server.universeDirectory, "Kerbals", kerbalID + ".txt"), kerbalData);
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.KERBAL_REPLY;
                newMessage.data = messageData;
                SendToAll(client, newMessage, false);
            }
        }

        private static void HandleVesselsRequest(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int sendVesselCount = 0;
                int cachedVesselCount = 0;
                List<string> clientRequested = new List<string>(mr.Read<string[]>());
                foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels")))
                {
                    byte[] vesselData = File.ReadAllBytes(file);
                    string vesselObject = Common.CalculateSHA256Hash(vesselData);
                    if (clientRequested.Contains(vesselObject))
                    {
                        sendVesselCount++;
                        SendVessel(client, vesselData);
                    }
                    else
                    {
                        cachedVesselCount++;
                    }
                }
                DarkLog.Debug("Sending " + client.playerName + " " + sendVesselCount + " vessels, cached: " + cachedVesselCount + "...");
                SendVesselsComplete(client);
            }
        }

        private static void HandleVesselProto(ClientObject client, byte[] messageData)
        {
            //Send vessel
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                int subspaceID = mr.Read<int>();
                double planetTime = mr.Read<double>();
                string vesselGuid = mr.Read<string>();
                bool isDockingUpdate = mr.Read<bool>();
                bool isFlyingUpdate = mr.Read<bool>();
                byte[] vesselData = mr.Read<byte[]>();
                if (!isFlyingUpdate)
                {
                    if (!isDockingUpdate)
                    {

                        DarkLog.Debug("Saving vessel " + vesselGuid + " from " + client.playerName);
                    }
                    else
                    {
                        DarkLog.Debug("Saving DOCKED vessel " + vesselGuid + " from " + client.playerName);
                    }
                    File.WriteAllBytes(Path.Combine(Server.universeDirectory, "Vessels", vesselGuid + ".txt"), vesselData);
                }
                else
                {
                    DarkLog.Debug("Relaying flying vessel " + vesselGuid + " from " + client.playerName);
                }
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>(subspaceID);
                    mw.Write<double>(planetTime);
                    mw.Write<bool>(isDockingUpdate);
                    mw.Write<bool>(isFlyingUpdate);
                    mw.Write<byte[]>(vesselData);
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.VESSEL_PROTO;
                    newMessage.data = mw.GetMessageBytes();
                    SendToAll(client, newMessage, false);
                }
            }
        }

        private static void HandleVesselUpdate(ClientObject client, byte[] messageData)
        {
            //We only relay this message.
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_UPDATE;
            newMessage.data = messageData;
            SendToAll(client, newMessage, false);
        }

        private static void HandleVesselRemoval(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //Don't care about the subspace on the server.
                mr.Read<int>();
                mr.Read<double>();
                string vesselID = mr.Read<string>();
                bool isDockingUpdate = mr.Read<bool>();
                if (!isDockingUpdate)
                {
                    DarkLog.Debug("Removing vessel " + vesselID + " from " + client.playerName);
                }
                else
                {
                    DarkLog.Debug("Removing DOCKED vessel " + vesselID + " from " + client.playerName);
                }
                if (File.Exists(Path.Combine(Server.universeDirectory, "Vessels", vesselID + ".txt")))
                {
                    File.Delete(Path.Combine(Server.universeDirectory, "Vessels", vesselID + ".txt"));
                }
                //Relay the message.
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.VESSEL_REMOVE;
                newMessage.data = messageData;
                SendToAll(client, newMessage, false);
            }
        }

        private static void HandleCraftLibrary(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                CraftMessageType craftMessageType = (CraftMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                if (fromPlayer != client.playerName)
                {
                    SendConnectionEnd(client, "Kicked for sending an craft library message for another player");
                    return;
                }
                switch (craftMessageType)
                {

                    case CraftMessageType.UPLOAD_FILE:
                        {
                            CraftType uploadType = (CraftType)mr.Read<int>();
                            string uploadName = mr.Read<string>();
                            byte[] uploadData = mr.Read<byte[]>();
                            string playerPath = Path.Combine(Path.Combine(Server.universeDirectory, "Crafts"), fromPlayer);
                            if (!Directory.Exists(playerPath))
                            {
                                Directory.CreateDirectory(playerPath);
                            }
                            string typePath = Path.Combine(playerPath, uploadType.ToString());
                            if (!Directory.Exists(typePath))
                            {
                                Directory.CreateDirectory(typePath);
                            }
                            string craftFile = Path.Combine(typePath, uploadName + ".craft");
                            File.WriteAllBytes(craftFile, uploadData);
                            DarkLog.Debug("Saving " + uploadName + ", type: " + uploadType.ToString() + " from " + fromPlayer);
                            using (MessageWriter mw = new MessageWriter())
                            {
                                ServerMessage newMessage = new ServerMessage();
                                newMessage.type = ServerMessageType.CRAFT_LIBRARY;
                                mw.Write<int>((int)CraftMessageType.ADD_FILE);
                                mw.Write<string>(fromPlayer);
                                mw.Write<int>((int)uploadType);
                                mw.Write<string>(uploadName);
                                newMessage.data = mw.GetMessageBytes();
                                SendToAll(client, newMessage, false);
                            }
                        }
                        break;
                    case CraftMessageType.REQUEST_FILE:
                        {
                            string craftOwner = mr.Read<string>();
                            CraftType requestedType = (CraftType)mr.Read<int>();
                            bool hasCraft = false;
                            string requestedName = mr.Read<string>();
                            string playerPath = Path.Combine(Path.Combine(Server.universeDirectory, "Crafts"), craftOwner);
                            string typePath = Path.Combine(playerPath, requestedType.ToString());
                            string craftFile = Path.Combine(typePath, requestedName + ".craft");
                            if (Directory.Exists(playerPath))
                            {
                                if (Directory.Exists(typePath))
                                {
                                    if (File.Exists(craftFile))
                                    {
                                        hasCraft = true;
                                    }
                                }
                            }
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.CRAFT_LIBRARY;
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write<int>((int)CraftMessageType.RESPOND_FILE);
                                mw.Write<string>(craftOwner);
                                mw.Write<int>((int)requestedType);
                                mw.Write<string>(requestedName);
                                mw.Write<bool>(hasCraft);
                                if (hasCraft)
                                {
                                    mw.Write<byte[]>(File.ReadAllBytes(craftFile));
                                    DarkLog.Debug("Sending " + fromPlayer + " " + requestedName + " from " + craftOwner);
                                }
                                newMessage.data = mw.GetMessageBytes();
                            }
                            SendToClient(client, newMessage, false);
                        }
                        break;
                    case CraftMessageType.DELETE_FILE:
                        {
                            CraftType craftType = (CraftType)mr.Read<int>();
                            string craftName = mr.Read<string>();
                            string playerPath = Path.Combine(Path.Combine(Server.universeDirectory, "Crafts"), fromPlayer);
                            string typePath = Path.Combine(playerPath, craftType.ToString());
                            string craftFile = Path.Combine(typePath, craftName + ".craft");
                            if (Directory.Exists(playerPath))
                            {
                                if (Directory.Exists(typePath))
                                {
                                    if (File.Exists(craftFile))
                                    {
                                        File.Delete(craftFile);
                                        DarkLog.Debug("Removing " + craftName + ", type: " + craftType.ToString() + " from " + fromPlayer);
                                    }
                                }
                            }
                            if (Directory.Exists(playerPath))
                            {
                                if (Directory.GetFiles(typePath).Length == 0)
                                {
                                    Directory.Delete(typePath);
                                }
                            }
                            if (Directory.GetDirectories(playerPath).Length == 0)
                            {
                                Directory.Delete(playerPath);
                            }
                            //Relay the delete message to other clients
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.CRAFT_LIBRARY;
                            newMessage.data = messageData;
                            SendToAll(client, newMessage, false);
                        }
                        break;
                }
            }
        }

        private static void HandleScreenshotLibrary(ClientObject client, byte[] messageData)
        {
            string screenshotDirectory = Path.Combine(Server.universeDirectory, "Screenshots");
            if (Settings.settingsStore.screenshotDirectory != "")
            {
                if (Directory.Exists(Settings.settingsStore.screenshotDirectory))
                {
                    screenshotDirectory = Settings.settingsStore.screenshotDirectory;
                }
            }
            if (!Directory.Exists(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
            }
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                ScreenshotMessageType messageType = (ScreenshotMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                switch (messageType)
                {
                    case ScreenshotMessageType.SCREENSHOT:
                        {
                            if (Settings.settingsStore.screenshotsPerPlayer > -1)
                            {
                                string playerScreenshotDirectory = Path.Combine(screenshotDirectory, fromPlayer);
                                if (!Directory.Exists(playerScreenshotDirectory))
                                {
                                    Directory.CreateDirectory(playerScreenshotDirectory);
                                }
                                string screenshotFile = Path.Combine(playerScreenshotDirectory, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
                                DarkLog.Debug("Saving screenshot from " + fromPlayer);

                                byte[] screenshotData = mr.Read<byte[]>();

                                File.WriteAllBytes(screenshotFile, screenshotData);
                                if (Settings.settingsStore.screenshotsPerPlayer != 0)
                                {
                                    while (Directory.GetFiles(playerScreenshotDirectory).Length > Settings.settingsStore.screenshotsPerPlayer)
                                    {
                                        string[] currentFiles = Directory.GetFiles(playerScreenshotDirectory);
                                        string deleteFile = currentFiles[0];
                                        //Find oldest file
                                        foreach (string testFile in currentFiles)
                                        {
                                            if (File.GetCreationTime(testFile) < File.GetCreationTime(deleteFile))
                                            {
                                                deleteFile = testFile;
                                            }
                                        }
                                        File.Delete(deleteFile);
                                        DarkLog.Debug("Removing old screenshot " + Path.GetFileName(deleteFile));
                                    }
                                }

                                //Notify players that aren't watching that there's a new screenshot availabe. This only works if there's a file available on the server.
                                //The server does not keep the screenshots in memory.
                                ServerMessage notifyMessage = new ServerMessage();
                                notifyMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write<int>((int)ScreenshotMessageType.NOTIFY);
                                    mw.Write(fromPlayer);
                                    notifyMessage.data = mw.GetMessageBytes();
                                    SendToAll(client, notifyMessage, false);
                                }
                            }
                            if (!playerUploadedScreenshotIndex.ContainsKey(fromPlayer))
                            {
                                playerUploadedScreenshotIndex.Add(fromPlayer, 0);
                            }
                            else
                            {
                                playerUploadedScreenshotIndex[fromPlayer]++;
                            }
                            if (!playerDownloadedScreenshotIndex.ContainsKey(fromPlayer))
                            {
                                playerDownloadedScreenshotIndex.Add(fromPlayer, new Dictionary<string, int>());
                            }
                            if (!playerDownloadedScreenshotIndex[fromPlayer].ContainsKey(fromPlayer))
                            {
                                playerDownloadedScreenshotIndex[fromPlayer].Add(fromPlayer, playerUploadedScreenshotIndex[fromPlayer]);
                            }
                            else
                            {
                                playerDownloadedScreenshotIndex[fromPlayer][fromPlayer] = playerUploadedScreenshotIndex[fromPlayer];
                            }
                            newMessage.data = messageData;
                            foreach (KeyValuePair<string, string> entry in playerWatchScreenshot)
                            {
                                if (entry.Key != fromPlayer)
                                {
                                    if (entry.Value == fromPlayer && entry.Key != client.playerName)
                                    {
                                        ClientObject toClient = GetClientByName(entry.Key);
                                        if (toClient != null && toClient != client)
                                        {
                                            if (!playerDownloadedScreenshotIndex.ContainsKey(entry.Key))
                                            {
                                                playerDownloadedScreenshotIndex.Add(entry.Key, new Dictionary<string, int>());
                                            }
                                            if (!playerDownloadedScreenshotIndex[entry.Key].ContainsKey(fromPlayer))
                                            {
                                                playerDownloadedScreenshotIndex[entry.Key].Add(fromPlayer, 0);
                                            }
                                            playerDownloadedScreenshotIndex[entry.Key][fromPlayer] = playerUploadedScreenshotIndex[fromPlayer];
                                            DarkLog.Debug("Sending screenshot from " + fromPlayer + " to " + entry.Key);
                                            using (MessageWriter mw = new MessageWriter())
                                            {
                                                ServerMessage sendStartMessage = new ServerMessage();
                                                sendStartMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
                                                mw.Write<int>((int)ScreenshotMessageType.SEND_START_NOTIFY);
                                                mw.Write<string>(fromPlayer);
                                                sendStartMessage.data = mw.GetMessageBytes();
                                                SendToClient(toClient, sendStartMessage, true);
                                            }
                                            SendToClient(toClient, newMessage, false);
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    case ScreenshotMessageType.WATCH:
                        {
                            newMessage.data = messageData;
                            string watchPlayer = mr.Read<string>();
                            if (watchPlayer == "")
                            {
                                if (playerWatchScreenshot.ContainsKey(fromPlayer))
                                {
                                    DarkLog.Debug(fromPlayer + " is no longer watching screenshots from " + playerWatchScreenshot[fromPlayer]);
                                    playerWatchScreenshot.Remove(fromPlayer);
                                }
                            }
                            else
                            {
                                DarkLog.Debug(fromPlayer + " is watching screenshots from " + watchPlayer);
                                playerWatchScreenshot[fromPlayer] = watchPlayer;
                                if (!playerDownloadedScreenshotIndex.ContainsKey(fromPlayer))
                                {
                                    playerDownloadedScreenshotIndex.Add(fromPlayer, new Dictionary<string, int>());
                                }
                                string watchPlayerScreenshotDirectory = Path.Combine(screenshotDirectory, watchPlayer);
                                //Find latest screenshot
                                string sendFile = null;
                                if (Directory.Exists(watchPlayerScreenshotDirectory))
                                {
                                    string[] playerScreenshots = Directory.GetFiles(watchPlayerScreenshotDirectory);
                                    if (playerScreenshots.Length > 0)
                                    {
                                        sendFile = playerScreenshots[0];
                                        foreach (string testFile in playerScreenshots)
                                        {
                                            if (File.GetCreationTime(testFile) > File.GetCreationTime(sendFile))
                                            {
                                                sendFile = testFile;
                                            }
                                        }
                                        if (!playerUploadedScreenshotIndex.ContainsKey(watchPlayer))
                                        {
                                            playerUploadedScreenshotIndex.Add(watchPlayer, 0);
                                        }
                                    }
                                }
                                //Send screenshot if needed
                                if (sendFile != null)
                                {
                                    bool sendScreenshot = false;
                                    if (!playerDownloadedScreenshotIndex[fromPlayer].ContainsKey(watchPlayer))
                                    {
                                        playerDownloadedScreenshotIndex[fromPlayer].Add(watchPlayer, playerUploadedScreenshotIndex[watchPlayer]);
                                        sendScreenshot = true;
                                    }
                                    else
                                    {
                                        if (playerDownloadedScreenshotIndex[fromPlayer][watchPlayer] != playerUploadedScreenshotIndex[watchPlayer])
                                        {
                                            sendScreenshot = true;
                                            playerDownloadedScreenshotIndex[fromPlayer][watchPlayer] = playerUploadedScreenshotIndex[watchPlayer];
                                        }
                                    }
                                    if (sendScreenshot)
                                    {
                                        ServerMessage sendStartMessage = new ServerMessage();
                                        sendStartMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
                                        using (MessageWriter mw = new MessageWriter())
                                        {
                                            mw.Write<int>((int)ScreenshotMessageType.SEND_START_NOTIFY);
                                            mw.Write<string>(fromPlayer);
                                            sendStartMessage.data = mw.GetMessageBytes();
                                        }
                                        ServerMessage screenshotMessage = new ServerMessage();
                                        screenshotMessage.type = ServerMessageType.SCREENSHOT_LIBRARY;
                                        using (MessageWriter mw = new MessageWriter())
                                        {
                                            mw.Write<int>((int)ScreenshotMessageType.SCREENSHOT);
                                            mw.Write<string>(watchPlayer);
                                            mw.Write<byte[]>(File.ReadAllBytes(sendFile));
                                            screenshotMessage.data = mw.GetMessageBytes();
                                        }
                                        ClientObject toClient = GetClientByName(fromPlayer);
                                        if (toClient != null)
                                        {
                                            DarkLog.Debug("Sending saved screenshot from " + watchPlayer + " to " + fromPlayer);
                                            SendToClient(toClient, sendStartMessage, false);
                                            SendToClient(toClient, screenshotMessage, false);
                                        }
                                    }
                                }
                            }
                            //Relay the message
                            SendToAll(client, newMessage, false);
                        }
                        break;
                }
            }
        }

        private static void HandlePingRequest(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.PING_REPLY;
            newMessage.data = messageData;
            SendToClient(client, newMessage, true);
        }

        private static void HandleMotdRequest(ClientObject client)
        {
            SendMotdReply(client);
        }

        private static void HandleWarpControl(ClientObject client, byte[] messageData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.WARP_CONTROL;
            newMessage.data = messageData;
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                WarpMessageType warpType = (WarpMessageType)mr.Read<int>();
                string fromPlayer = mr.Read<string>();
                if (fromPlayer == client.playerName)
                {
                    if (warpType == WarpMessageType.NEW_SUBSPACE)
                    {
                        int newSubspaceID = mr.Read<int>();
                        if (subspaces.ContainsKey(newSubspaceID))
                        {
                            DarkLog.Debug("Kicked for trying to create an existing subspace");
                            SendConnectionEnd(client, "Kicked for trying to create an existing subspace");
                            return;
                        }
                        else
                        {
                            Subspace newSubspace = new Subspace();
                            newSubspace.serverClock = mr.Read<long>();
                            newSubspace.planetTime = mr.Read<double>();
                            newSubspace.subspaceSpeed = mr.Read<float>();
                            subspaces.Add(newSubspaceID, newSubspace);
                            client.subspace = newSubspaceID;
                            SaveLatestSubspace();
                        }
                    }
                    if (warpType == WarpMessageType.CHANGE_SUBSPACE)
                    {
                        client.subspace = mr.Read<int>();
                    }
                    if (warpType == WarpMessageType.REPORT_RATE)
                    {
                        int reportedSubspace = mr.Read<int>();
                        if (reportedSubspace == client.subspace)
                        {
                            float newSubspaceRateTotal = mr.Read<float>();
                            int newSubspaceRateCount = 1;
                            foreach (ClientObject otherClient in clients)
                            {
                                if (otherClient.authenticated && otherClient.subspace == reportedSubspace)
                                {
                                    newSubspaceRateTotal += otherClient.subspaceRate;
                                    newSubspaceRateCount++;
                                }
                            }
                            float newAverageRate = newSubspaceRateTotal / (float)newSubspaceRateCount;
                            if (newAverageRate < 0.5f)
                            {
                                newAverageRate = 0.5f;
                            }
                            if (newAverageRate > 1f)
                            {
                                newAverageRate = 1f;
                            }
                            //Relock the subspace if the rate is more than 3% out of the average
                            //DarkLog.Debug("New average rate: " + newAverageRate + " for subspace " + client.subspace);
                            if (Math.Abs(subspaces[reportedSubspace].subspaceSpeed - newAverageRate) > 0.03f)
                            {
                                //New time = Old time + (seconds since lock * subspace rate)
                                long newServerClockTime = DateTime.UtcNow.Ticks;
                                float timeSinceLock = (DateTime.UtcNow.Ticks - subspaces[client.subspace].serverClock) / 10000000f;
                                double newPlanetariumTime = subspaces[client.subspace].planetTime + (timeSinceLock * subspaces[client.subspace].subspaceSpeed);
                                subspaces[client.subspace].serverClock = newServerClockTime;
                                subspaces[client.subspace].planetTime = newPlanetariumTime;
                                subspaces[client.subspace].subspaceSpeed = newAverageRate;
                                ServerMessage relockMessage = new ServerMessage();
                                relockMessage.type = ServerMessageType.WARP_CONTROL;
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write<int>((int)WarpMessageType.RELOCK_SUBSPACE);
                                    mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                                    mw.Write<int>(client.subspace);
                                    mw.Write<long>(DateTime.UtcNow.Ticks);
                                    mw.Write<double>(newPlanetariumTime);
                                    mw.Write<float>(newAverageRate);
                                    relockMessage.data = mw.GetMessageBytes();
                                }
                                SaveLatestSubspace();
                                DarkLog.Debug("Subspace " + client.subspace + " locked to " + newAverageRate + "x speed.");
                                SendToClient(client, relockMessage, true);
                                SendToAll(client, relockMessage, true);
                            }
                        }
                    }
                }
                else
                {
                    DarkLog.Debug(client.playerName + " tried to send an update for " + fromPlayer + ", kicking.");
                    SendConnectionEnd(client, "Kicked for sending an update for another player");
                    return;
                }
            }
            SendToAll(client, newMessage, true);
        }

        private static void HandleLockSystemMessage(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData, false))
            {
                //All of the messages need replies, let's create a message for it.
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.LOCK_SYSTEM;
                //Read the lock-system message type
                LockMessageType lockMessageType = (LockMessageType)mr.Read<int>();
                switch (lockMessageType)
                {
                    case LockMessageType.ACQUIRE:
                        {
                            string playerName = mr.Read<string>();
                            string lockName = mr.Read<string>();
                            bool force = mr.Read<bool>();
                            if (playerName != client.playerName)
                            {
                                SendConnectionEnd(client, "Kicked for sending a lock message for another player");
                            }
                            bool lockResult = lockSystem.AcquireLock(lockName, playerName, force);
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write((int)LockMessageType.ACQUIRE);
                                mw.Write(playerName);
                                mw.Write(lockName);
                                mw.Write(lockResult);
                                newMessage.data = mw.GetMessageBytes();
                            }
                            //Send to all clients
                            SendToAll(null, newMessage, true);
                            if (lockResult)
                            {
                                DarkLog.Debug(playerName + " acquired lock " + lockName);
                            }
                            else
                            {
                                DarkLog.Debug(playerName + " failed to acquire lock " + lockName);
                            }
                        }
                        break;
                    case LockMessageType.RELEASE:
                        {
                            string playerName = mr.Read<string>();
                            string lockName = mr.Read<string>();
                            if (playerName != client.playerName)
                            {
                                SendConnectionEnd(client, "Kicked for sending a lock message for another player");
                            }
                            bool lockResult = lockSystem.ReleaseLock(lockName, playerName);
                            if (!lockResult)
                            {
                                SendConnectionEnd(client, "Kicked for releasing a lock you do not own");
                            }
                            else
                            {
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write((int)LockMessageType.RELEASE);
                                    mw.Write(playerName);
                                    mw.Write(lockName);
                                    mw.Write(lockResult);
                                    newMessage.data = mw.GetMessageBytes();
                                }
                                //Send to all clients
                                SendToAll(null, newMessage, true);
                            }
                            if (lockResult)
                            {
                                DarkLog.Debug(playerName + " released lock " + lockName);
                            }
                            else
                            {
                                DarkLog.Debug(playerName + " failed to release lock " + lockName);
                            }
                        }
                        break;
                }
            }
        }

        private static void HandleSplitMessage(ClientObject client, byte[] messageData)
        {
            if (!client.isReceivingSplitMessage)
            {
                //New split message
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    client.receiveSplitMessage = new ClientMessage();
                    client.receiveSplitMessage.type = (ClientMessageType)mr.Read<int>();
                    client.receiveSplitMessage.data = new byte[mr.Read<int>()];
                    client.receiveSplitMessageBytesLeft = client.receiveSplitMessage.data.Length;
                    byte[] firstSplitData = mr.Read<byte[]>();
                    firstSplitData.CopyTo(client.receiveSplitMessage.data, 0);
                    client.receiveSplitMessageBytesLeft -= firstSplitData.Length;
                }
                client.isReceivingSplitMessage = true;
            }
            else
            {
                //Continued split message
                messageData.CopyTo(client.receiveSplitMessage.data, client.receiveSplitMessage.data.Length - client.receiveSplitMessageBytesLeft);
                client.receiveSplitMessageBytesLeft -= messageData.Length;
            }
            if (client.receiveSplitMessageBytesLeft == 0)
            {
                HandleMessage(client, client.receiveSplitMessage);
                client.receiveSplitMessage = null;
                client.isReceivingSplitMessage = false;
            }
        }

        private static void HandleConnectionEnd(ClientObject client, byte[] messageData)
        {
            string reason = "Unknown";
            try
            {
                using (MessageReader mr = new MessageReader(messageData, false))
                {
                    reason = mr.Read<string>();
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error handling CONNECTION_END message from " + client.playerName + ":" + e);
            }
            DarkLog.Debug(client.playerName + " sent connection end message, reason: " + reason);
            DisconnectClient(client);
        }
        #endregion
        #region Message sending
        //Call with null client to send to all clients. Also called from Dekessler and NukeKSC.
        public static void SendToAll(ClientObject ourClient, ServerMessage message, bool highPriority)
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
            if (!Server.serverRunning && !highPriority)
            {
                //Skip sending low priority messages during a server shutdown.
                return;
            }
            if (message == null)
            {
                Exception up = new Exception("Cannot send a null message to a client!");
                throw up;
            }
            else
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
        }

        public static ClientObject GetClientByName(string playerName)
        {
            ClientObject findClient = null;
            foreach (ClientObject testClient in clients)
            {
                if (testClient.authenticated && testClient.playerName == playerName)
                {
                    findClient = testClient;
                    break;
                }
            }
            return findClient;
        }

        public static ClientObject GetClientByIP(IPAddress ipAddress)
        {
            ClientObject findClient = null;
            foreach (ClientObject testClient in clients)
            {
                if (testClient.authenticated && testClient.ipAddress == ipAddress)
                {
                    findClient = testClient;
                    break;
                }
            }
            return findClient;
        }

        public static ClientObject GetClientByGuid(Guid guid)
        {
            ClientObject findClient = null;
            foreach (ClientObject testClient in clients)
            {
                if (testClient.authenticated && testClient.GUID == guid)
                {
                    findClient = testClient;
                    break;
                }
            }
            return findClient;
        }

        private static void SendHeartBeat(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.HEARTBEAT;
            SendToClient(client, newMessage, true);
        }

        private static void SendHandshakeReply(ClientObject client, int response, string reason)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.HANDSHAKE_REPLY;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(response);
                mw.Write<string>(reason);
                mw.Write<int>(Common.PROTOCOL_VERSION);
                mw.Write<string>(Common.PROGRAM_VERSION);
                if (response == 0)
                {
                    mw.Write<bool>(Settings.settingsStore.modControl);
                    if (Settings.settingsStore.modControl)
                    {
                        mw.Write<string>(modFileData);
                    }
                }
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendChatMessageToClient(ClientObject client, string messageText)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.PRIVATE_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                mw.Write<string>(client.playerName);
                mw.Write(messageText);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        public static void SendChatMessageToAll(string messageText)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CHAT_MESSAGE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.CHANNEL_MESSAGE);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                //Global channel
                mw.Write<string>("");
                mw.Write(messageText);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToAll(null, newMessage, true);
        }

        private static void SendServerSettings(ClientObject client)
        {
            int numberOfKerbals = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Kerbals")).Length;
            int numberOfVessels = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels")).Length;
            int numberOfScenarioModules = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)).Length;
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SERVER_SETTINGS;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)Settings.settingsStore.warpMode);
                mw.Write<int>((int)Settings.settingsStore.gameMode);
                mw.Write<bool>(Settings.settingsStore.cheats);
                //Tack the amount of kerbals, vessels and scenario modules onto this message
                mw.Write<int>(numberOfKerbals);
                mw.Write<int>(numberOfVessels);
                //mw.Write<int>(numberOfScenarioModules);
                mw.Write<int>(Settings.settingsStore.screenshotHeight);
                mw.Write<int>(Settings.settingsStore.numberOfAsteroids);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendCraftList(ClientObject client)
        {
            int numberOfCrafts = 0;
            string craftDirectory = Path.Combine(Server.universeDirectory, "Crafts");
            if (!Directory.Exists(craftDirectory))
            {
                Directory.CreateDirectory(craftDirectory);
            }
            string[] players = Directory.GetDirectories(craftDirectory);
            for (int i = 0; i < players.Length; i++)
            {
                players[i] = players[i].Substring(players[i].LastIndexOf(Path.DirectorySeparatorChar) + 1);
            }
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CRAFT_LIBRARY;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)CraftMessageType.LIST);
                mw.Write<string[]>(players);
                foreach (string player in players)
                {
                    string playerPath = Path.Combine(craftDirectory, player);
                    string vabPath = Path.Combine(playerPath, "VAB");
                    string sphPath = Path.Combine(playerPath, "SPH");
                    string subassemblyPath = Path.Combine(playerPath, "SUBASSEMBLY");
                    bool vabExists = Directory.Exists(vabPath);
                    bool sphExists = Directory.Exists(sphPath);
                    bool subassemblyExists = Directory.Exists(subassemblyPath);
                    mw.Write<bool>(vabExists);
                    mw.Write<bool>(sphExists);
                    mw.Write<bool>(subassemblyExists);
                    if (vabExists)
                    {
                        string[] vabCraftNames = Directory.GetFiles(vabPath);
                        for (int i = 0; i < vabCraftNames.Length; i++)
                        {
                            //We only want the craft names
                            vabCraftNames[i] = Path.GetFileNameWithoutExtension(vabCraftNames[i]);
                            numberOfCrafts++;
                        }
                        mw.Write<string[]>(vabCraftNames);
                    }

                    if (sphExists)
                    {
                        string[] sphCraftNames = Directory.GetFiles(sphPath);
                        for (int i = 0; i < sphCraftNames.Length; i++)
                        {
                            //We only want the craft names
                            sphCraftNames[i] = Path.GetFileNameWithoutExtension(sphCraftNames[i]);
                            numberOfCrafts++;
                        }
                        mw.Write<string[]>(sphCraftNames);
                    }

                    if (subassemblyExists)
                    {
                        string[] subassemblyCraftNames = Directory.GetFiles(subassemblyPath);
                        for (int i = 0; i < subassemblyCraftNames.Length; i++)
                        {
                            //We only want the craft names
                            subassemblyCraftNames[i] = Path.GetFileNameWithoutExtension(subassemblyCraftNames[i]);
                            numberOfCrafts++;
                        }
                        mw.Write<string[]>(subassemblyCraftNames);
                    }
                }
                newMessage.data = mw.GetMessageBytes();
                SendToClient(client, newMessage, true);
                DarkLog.Debug("Sending " + client.playerName + " " + numberOfCrafts + " craft library entries");
            }
        }

        private static void SendPlayerChatChannels(ClientObject client)
        {
            List<string> playerList = new List<string>();
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ChatMessageType.LIST);
                foreach (KeyValuePair<string, List<string>> playerEntry in playerChatChannels)
                {
                    playerList.Add(playerEntry.Key);
                }
                mw.Write<string[]>(playerList.ToArray());
                foreach (string player in playerList)
                {
                    mw.Write<string[]>(playerChatChannels[player].ToArray());
                }
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.CHAT_MESSAGE;
                newMessage.data = mw.GetMessageBytes();
                SendToClient(client, newMessage, true);
            }
        }

        private static void SendAllLocks(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.LOCK_SYSTEM;
            //Send the dictionary as 2 string[]'s.
            Dictionary<string,string> lockList = lockSystem.GetLockList();
            List<string> lockKeys = new List<string>(lockList.Keys);
            List<string> lockValues = new List<string>(lockList.Values);
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write((int)LockMessageType.LIST);
                mw.Write<string[]>(lockKeys.ToArray());
                mw.Write<string[]>(lockValues.ToArray());
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendAllPlayerStatus(ClientObject client)
        {
            foreach (ClientObject otherClient in clients)
            {
                if (otherClient.authenticated)
                {
                    if (otherClient != client)
                    {
                        ServerMessage newMessage = new ServerMessage();
                        newMessage.type = ServerMessageType.PLAYER_STATUS;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<string>(otherClient.playerName);
                            mw.Write<string>(otherClient.playerStatus.vesselText);
                            mw.Write<string>(otherClient.playerStatus.statusText);
                            newMessage.data = mw.GetMessageBytes();
                        }
                        SendToClient(client, newMessage, true);
                    }
                }
            }
        }

        private static void SendAllSubspaces(ClientObject client)
        {
            //Send all the locks.
            foreach (KeyValuePair<int, Subspace> subspace in subspaces)
            {
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.WARP_CONTROL;
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)WarpMessageType.NEW_SUBSPACE);
                    mw.Write<string>("");
                    mw.Write<int>(subspace.Key);
                    mw.Write<long>(subspace.Value.serverClock);
                    mw.Write<double>(subspace.Value.planetTime);
                    mw.Write<float>(subspace.Value.subspaceSpeed);
                    newMessage.data = mw.GetMessageBytes();
                }
                SendToClient(client, newMessage, true);
            }
            //Tell the player "when" everyone is.
            foreach (ClientObject otherClient in clients)
            {
                if (otherClient.authenticated && (otherClient.playerName != client.playerName))
                {
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.WARP_CONTROL;
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)WarpMessageType.CHANGE_SUBSPACE);
                        mw.Write<string>(otherClient.playerName);
                        mw.Write<int>(otherClient.subspace);
                        newMessage.data = mw.GetMessageBytes();
                    }
                    SendToClient(client, newMessage, true);
                }
            }
        }

        private static void SendScenarioModules(ClientObject client)
        {
            int numberOfScenarioModules = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)).Length;
            int currentScenarioModule = 0;
            string[] scenarioName = new string[numberOfScenarioModules];
            string[] scenarioData = new string[numberOfScenarioModules];
            foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)))
            {
                using (StreamReader sr = new StreamReader(file))
                {
                    //Remove the .txt part for the name
                    scenarioName[currentScenarioModule] = Path.GetFileNameWithoutExtension(file);
                    scenarioData[currentScenarioModule] = sr.ReadToEnd();
                    currentScenarioModule++;
                }
            }
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SCENARIO_DATA;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string[]>(scenarioName);
                mw.Write<string[]>(scenarioData);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendSetSubspace(ClientObject client)
        {
            int latestSubspace = -1;
            double latestPlanetTime = 0;
            foreach (KeyValuePair<int, Subspace> subspace in subspaces)
            {
                double subspaceTime = (((DateTime.UtcNow.Ticks - subspace.Value.serverClock) / 10000000d) * subspace.Value.subspaceSpeed) + subspace.Value.planetTime;
                if (subspaceTime > latestPlanetTime)
                {
                    latestSubspace = subspace.Key;
                    latestPlanetTime = subspaceTime;
                }
            }
            DarkLog.Debug("Sending " + client.playerName + " to subspace " + latestSubspace + ", time: " + latestPlanetTime);
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SET_SUBSPACE;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(latestSubspace);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendAllReportedSkewRates(ClientObject client)
        {
            foreach (ClientObject otherClient in clients)
            {
                if (otherClient.authenticated)
                {
                    if (otherClient != client)
                    {
                        ServerMessage newMessage = new ServerMessage();
                        newMessage.type = ServerMessageType.WARP_CONTROL;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<int>((int)WarpMessageType.REPORT_RATE);
                            mw.Write<string>(otherClient.playerName);
                            mw.Write<float>(otherClient.subspace);
                            mw.Write<float>(otherClient.subspaceRate);
                            newMessage.data = mw.GetMessageBytes();
                        }
                        SendToClient(client, newMessage, true);
                    }
                }
            }
        }

        private static void SendVesselList(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_LIST;
            string[] vesselFiles = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels"));
            string[] vesselObjects = new string[vesselFiles.Length];
            for (int i = 0; i < vesselFiles.Length; i++)
            {
                vesselObjects[i] = Common.CalculateSHA256Hash(vesselFiles[i]);
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string[]>(vesselObjects);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, false);
        }

        private static void SendKerbal(ClientObject client, int kerbalID, byte[] kerbalData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.KERBAL_REPLY;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(GetLatestSubspace());
                //Send the vessel with a send time of 0 so it instantly loads on the client.
                mw.Write<double>(0);
                mw.Write<int>(kerbalID);
                mw.Write<byte[]>(kerbalData);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, false);
        }

        private static void SendKerbalsComplete(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.KERBAL_COMPLETE;
            SendToClient(client, newMessage, false);
            //Send vessel list needed for sync to the client
            SendVesselList(client);
        }

        private static void SendVessel(ClientObject client, byte[] vesselData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_PROTO;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>(GetLatestSubspace());
                //Send the vessel with a send time of 0 so it instantly loads on the client.
                mw.Write<double>(0);
                mw.Write<bool>(false);
                mw.Write<bool>(false);
                mw.Write<byte[]>(vesselData);
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

        private static void SendMotdReply(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.MOTD_REPLY;

            string newMotd = Settings.settingsStore.serverMotd;
            newMotd = newMotd.Replace("%name%", client.playerName);
            newMotd = newMotd.Replace("%servername%", Settings.settingsStore.shortname);

            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(newMotd);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        private static void SendConnectionEnd(ClientObject client, string reason)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.CONNECTION_END;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(reason);
                newMessage.data = mw.GetMessageBytes();
            }
            SendToClient(client, newMessage, true);
        }

        public static void SendConnectionEndToAll(string reason)
        {
            foreach (ClientObject client in clients)
            {
                if (client.authenticated)
                {
                    SendConnectionEnd(client, reason);
                }
            }
        }
        #endregion
        #region Server commands
        public static void KickPlayer(string commandArgs)
        {
            string playerName = commandArgs;
            string reason = "";
            if (commandArgs.Contains(" "))
            {
                playerName = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" "));
            }
            ClientObject player = null;

            if (playerName != "")
            {
                player = GetClientByName(playerName);
                if (player != null)
                {
                    DarkLog.Normal("Kicking " + playerName + " from the server");
                    if (reason != "")
                    {
                        SendConnectionEnd(player, "Kicked from the server, " + reason);
                    }
                    else
                    {
                        SendConnectionEnd(player, "Kicked from the server");
                    }
                }
            }
            else
            {
                DarkLog.Error("Syntax error. Usage: /kick playername [reason]");
            }
        }

        public static void BanPlayer(string commandArgs)
        {
            string playerName = commandArgs;
            string reason = "";

            if (commandArgs.Contains(" "))
            {
                playerName = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" "));
            }

            if (playerName != "")
            {

                ClientObject player = GetClientByName(playerName);

                if (reason == "")
                {
                    reason = "no reason specified";
                }

                if (player != null)
                {
                    SendConnectionEnd(player, "You were banned from the server!");
                }

                DarkLog.Normal("Player '" + playerName + "' was banned from the server: " + reason);
                bannedNames.Add(playerName);
                banReasons.Add(reason);
                SaveBans();
            }

        }

        public static void BanIP(string commandArgs)
        {
            string ip = commandArgs;
            string reason = "";

            if (commandArgs.Contains(" "))
            {
                ip = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" "));
            }

            IPAddress ipAddress;
            if (IPAddress.TryParse(ip, out ipAddress))
            {

                ClientObject player = GetClientByIP(ipAddress);

                if (reason == "")
                {
                    reason = "no reason specified";
                }

                if (player != null)
                {
                    SendConnectionEnd(player, "You were banned from the server!");
                }
                bannedIPs.Add(ipAddress);
                banReasons.Add(reason);
                SaveBans();

                DarkLog.Normal("IP Address '" + ip + "' was banned from the server: " + reason);
            }
            else
            {
                DarkLog.Normal(ip + " is not a valid IP address");
            }

        }

        public static void BanGuid(string commandArgs)
        {
            string guid = commandArgs;
            string reason = "";

            if (commandArgs.Contains(" "))
            {
                guid = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" "));
            }

            Guid bguid;
            if (Guid.TryParse(guid, out bguid))
            {

                ClientObject player = GetClientByGuid(bguid);

                if (reason == "")
                {
                    reason = "no reason specified";
                }

                if (player != null)
                {
                    SendConnectionEnd(player, "You were banned from the server!");
                }
                bannedGUIDs.Add(bguid);
                banReasons.Add(reason);
                SaveBans();

                DarkLog.Normal("GUID '" + guid + "' was banned from the server: " + reason);
            }
            else
            {
                DarkLog.Normal(guid + " is not a valid player token");
            }
        }

        public static void PMCommand(string commandArgs)
        {
            ClientObject pmPlayer = null;
            int matchedLength = 0;
            lock (clientLock)
            {
                foreach (ClientObject testPlayer in clients)
                {
                    //Only search authenticated players
                    if (testPlayer.authenticated)
                    {
                        //Try to match the longest player name
                        if (commandArgs.StartsWith(testPlayer.playerName) && testPlayer.playerName.Length > matchedLength)
                        {
                            //Double check there is a space after the player name
                            if ((commandArgs.Length > (testPlayer.playerName.Length + 1)) ? commandArgs[testPlayer.playerName.Length] == ' ' : false)
                            {
                                pmPlayer = testPlayer;
                                matchedLength = testPlayer.playerName.Length;
                            }
                        }
                    }
                }
            }
            if (pmPlayer != null)
            {
                string messageText = commandArgs.Substring(pmPlayer.playerName.Length + 1);
                SendChatMessageToClient(pmPlayer, messageText);
            }
            else
            {
                DarkLog.Normal("Player not found!");
            }
        }
        #endregion
    }

    public class ClientObject
    {
        public bool authenticated;
        public string playerName;
        public bool isBanned;
        public IPAddress ipAddress;
        public Guid GUID;
        //subspace tracking
        public int subspace;
        public float subspaceRate;
        //vessel tracking
        public string activeVessel;
        //connection
        public string endpoint;
        public TcpClient connection;
        //Send buffer
        public long lastSendTime;
        public bool isSendingToClient;
        public Queue<ServerMessage> sendMessageQueueHigh;
        public Queue<ServerMessage> sendMessageQueueSplit;
        public Queue<ServerMessage> sendMessageQueueLow;
        public Queue<ClientMessage> receiveMessageQueue;
        public long lastReceiveTime;
        //Receive buffer
        public bool isReceivingMessage;
        public int receiveMessageBytesLeft;
        public ClientMessage receiveMessage;
        //Receive split buffer
        public bool isReceivingSplitMessage;
        public int receiveSplitMessageBytesLeft;
        public ClientMessage receiveSplitMessage;
        //State tracking
        public ConnectionStatus connectionStatus;
        public PlayerStatus playerStatus;
        //Send lock
        public object sendLock;
    }
}

