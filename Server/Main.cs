using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Net;
using System.Text;
using DarkMultiPlayerCommon;
using SettingsParser;

namespace DarkMultiPlayerServer
{
    public class Server
    {
        public static bool serverRunning;
        public static bool serverStarting;
        public static bool serverRestarting;
        public static string universeDirectory;
        public static string configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
        public static Stopwatch serverClock;
        public static HttpListener httpListener;
        private static long ctrlCTime;
        public static int playerCount = 0;
        public static string players = "";
        public static long lastPlayerActivity;
        public static object universeSizeLock = new object();
        public static string modFile;
        private static int day;

        public static void Main()
        {
#if !DEBUG
            try
            {
#endif
            //Start the server clock
            serverClock = new Stopwatch();
            serverClock.Start();

            Settings.Reset();

            //Set the last player activity time to server start
            lastPlayerActivity = serverClock.ElapsedMilliseconds;

            //Periodic garbage collection
            long lastGarbageCollect = 0;

            //Periodic screenshot check
            long lastScreenshotExpiredCheck = 0;

            //Periodic log check
            long lastLogExpiredCheck = 0;

            //Periodic day check
            long lastDayCheck = 0;

            //Set universe directory
            universeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Universe");

            if (!Directory.Exists(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            string oldSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPServerSettings.txt");
            string newSettingsFile = Path.Combine(configDirectory, "Settings.txt");
            string oldGameplayFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPGameplaySettings.txt");
            string newGameplayFile = Path.Combine(configDirectory, "GameplaySettings.txt");

            // Run the conversion
            BackwardsCompatibility.ConvertSettings(oldSettingsFile, newSettingsFile);
            if (File.Exists(oldGameplayFile))
            {
                if (!File.Exists(newGameplayFile))
                {
                    File.Move(oldGameplayFile, newGameplayFile);
                }
                File.Delete(oldGameplayFile);
            }

            string oldModFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPModControl.txt");
            modFile = Path.Combine(configDirectory, "mod-control.txt");

            // Move mod control file to Config/mod-control.txt
            if (File.Exists(oldModFile))
            {
                if (!File.Exists(modFile))
                {
                    File.Move(oldModFile, modFile);
                }
                File.Delete(modFile);
            }

            // Move DMPPlayerBans to Config/banned-players.txt
            string oldBanlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPPlayerBans.txt");
            string newBanlistFile = Path.Combine(configDirectory, "banned-players.txt");
            if (File.Exists(oldBanlistFile))
            {
                if (!File.Exists(newBanlistFile))
                {
                    File.Move(oldBanlistFile, newBanlistFile);
                }
                File.Delete(oldBanlistFile);
            }

            // Move DMPIPBans to Config/banned-ips.txt
            oldBanlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPIPBans.txt");
            newBanlistFile = Path.Combine(configDirectory, "banned-ips.txt");
            if (File.Exists(oldBanlistFile))
            {
                if (!File.Exists(newBanlistFile))
                {
                    File.Move(oldBanlistFile, newBanlistFile);
                }
                File.Delete(oldBanlistFile);
            }

            // Move DMPKeyBans to Config/banned-keys.txt
            oldBanlistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPKeyBans.txt");
            newBanlistFile = Path.Combine(configDirectory, "banned-keys.txt");
            if (File.Exists(oldBanlistFile))
            {
                if (!File.Exists(newBanlistFile))
                {
                    File.Move(oldBanlistFile, newBanlistFile);
                }
                File.Delete(oldBanlistFile);
            }

            // Move DMPAdmins to Config/admins.txt
            string oldAdminsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPAdmins.txt");
            string newAdminsFile = Path.Combine(configDirectory, "admins.txt");
            if (File.Exists(oldAdminsFile))
            {
                if (!File.Exists(newAdminsFile))
                {
                    File.Move(oldAdminsFile, newAdminsFile);
                }
                File.Delete(oldAdminsFile);
            }

            //Register the server commands
            CommandHandler.RegisterCommand("exit", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("quit", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("shutdown", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("restart", Server.Restart, "Restarts the server");
            CommandHandler.RegisterCommand("kick", KickCommand.KickPlayer, "Kicks a player from the server");
            CommandHandler.RegisterCommand("ban", BanSystem.fetch.BanPlayer, "Bans a player from the server");
            CommandHandler.RegisterCommand("banip", BanSystem.fetch.BanIP, "Bans an IP Address from the server");
            CommandHandler.RegisterCommand("bankey", BanSystem.fetch.BanPublicKey, "Bans a Guid from the server");
            CommandHandler.RegisterCommand("pm", PMCommand.HandleCommand, "Sends a message to a player");
            CommandHandler.RegisterCommand("admin", AdminCommand.HandleCommand, "Sets a player as admin/removes admin from the player");
            CommandHandler.RegisterCommand("whitelist", WhitelistCommand.HandleCommand, "Change the server whitelist");
            //Register the ctrl+c event
            Console.CancelKeyPress += new ConsoleCancelEventHandler(CatchExit);
            serverStarting = true;

            //Fix kerbals from 0.23.5 to 0.24 (Now indexed by string, thanks Squad!
            BackwardsCompatibility.FixKerbals();

            //Remove player tokens
            BackwardsCompatibility.RemoveOldPlayerTokens();

            //Add new stock parts
            BackwardsCompatibility.UpdateModcontrolPartList();

            if (System.Net.Sockets.Socket.OSSupportsIPv6)
            {
                Settings.settingsStore.address = "::";
            }

            DarkLog.Debug("Loading settings...");
            Settings.Load();
            if (Settings.settingsStore.gameDifficulty == GameDifficulty.CUSTOM)
            {
                GameplaySettings.Reset();
                GameplaySettings.Load();
            }

            //Test compression
            if (Settings.settingsStore.compressionEnabled)
            {
                Compression.compressionEnabled = true;
            }

            //Set day for log change
            day = DateTime.Now.Day;

            //Load plugins
            DMPPluginHandler.LoadPlugins();

            Console.Title = "DMPServer " + Common.PROGRAM_VERSION + ", protocol " + Common.PROTOCOL_VERSION;

            while (serverStarting || serverRestarting)
            {
                if (serverRestarting)
                {
                    DarkLog.Debug("Reloading settings...");
                    Settings.Reset();
                    Settings.Load();
                    if (Settings.settingsStore.gameDifficulty == GameDifficulty.CUSTOM)
                    {
                        DarkLog.Debug("Reloading gameplay settings...");
                        GameplaySettings.Reset();
                        GameplaySettings.Load();
                    }
                }

                serverRestarting = false;
                DarkLog.Normal("Starting DMPServer " + Common.PROGRAM_VERSION + ", protocol " + Common.PROTOCOL_VERSION);

                if (Settings.settingsStore.gameDifficulty == GameDifficulty.CUSTOM)
                {
                    //Generate the config file by accessing the object.
                    DarkLog.Debug("Loading gameplay settings...");
                    GameplaySettings.Load();
                }

                //Load universe
                DarkLog.Normal("Loading universe... ");
                CheckUniverse();

                DarkLog.Normal("Starting " + Settings.settingsStore.warpMode + " server on port " + Settings.settingsStore.port + "... ");

                serverRunning = true;
                Thread commandThread = new Thread(new ThreadStart(CommandHandler.ThreadMain));
                Thread clientThread = new Thread(new ThreadStart(ClientHandler.ThreadMain));
                commandThread.Start();
                clientThread.Start();
                while (serverStarting)
                {
                    Thread.Sleep(500);
                }

                StartHTTPServer();
                DarkLog.Normal("Ready!");
                DMPPluginHandler.FireOnServerStart();
                while (serverRunning)
                {
                    //Run a garbage collection every 30 seconds.
                    if ((serverClock.ElapsedMilliseconds - lastGarbageCollect) > 30000)
                    {
                        lastGarbageCollect = serverClock.ElapsedMilliseconds;
                        GC.Collect();
                    }
                    //Run the screenshot expire function every 10 minutes
                    if ((serverClock.ElapsedMilliseconds - lastScreenshotExpiredCheck) > 600000)
                    {
                        lastScreenshotExpiredCheck = serverClock.ElapsedMilliseconds;
                        ScreenshotExpire.ExpireScreenshots();
                    }
                    //Run the log expire function every 10 minutes
                    if ((serverClock.ElapsedMilliseconds - lastLogExpiredCheck) > 600000)
                    {
                        lastLogExpiredCheck = serverClock.ElapsedMilliseconds;
                        LogExpire.ExpireLogs();
                    }
                    // Check if the day has changed, every minute
                    if ((serverClock.ElapsedMilliseconds - lastDayCheck) > 60000)
                    {
                        lastDayCheck = serverClock.ElapsedMilliseconds;
                        if (day != DateTime.Now.Day)
                        {
                            DarkLog.LogFilename = Path.Combine(DarkLog.LogFolder, "dmpserver " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log");
                            DarkLog.WriteToLog("Continued from logfile " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log");
                            day = DateTime.Now.Day;
                        }
                    }

                    Thread.Sleep(500);
                }
                DMPPluginHandler.FireOnServerStop();
                commandThread.Abort();
                clientThread.Join();
            }
            DarkLog.Normal("Goodbye!");
            Environment.Exit(0);
#if !DEBUG
            }
            catch (Exception e)
            {
                DarkLog.Fatal("Error in main server thread, Exception: " + e);
                throw;
            }
#endif
        }

        // Check universe folder size
        public static long GetUniverseSize()
        {
            lock (universeSizeLock)
            {
                long directorySize = 0;
                string[] kerbals = Directory.GetFiles(Path.Combine(universeDirectory, "Kerbals"), "*.*");
                string[] vessels = Directory.GetFiles(Path.Combine(universeDirectory, "Vessels"), "*.*");

                foreach (string kerbal in kerbals)
                {
                    FileInfo kInfo = new FileInfo(kerbal);
                    directorySize += kInfo.Length;
                }

                foreach (string vessel in vessels)
                {
                    FileInfo vInfo = new FileInfo(vessel);
                    directorySize += vInfo.Length;
                }

                return directorySize;
            }
        }
        //Get last disconnect time
        public static long GetLastPlayerActivity()
        {
            if (playerCount > 0)
            {
                return 0;
            }
            return (serverClock.ElapsedMilliseconds - lastPlayerActivity) / 1000;
        }
        //Create universe directories
        private static void CheckUniverse()
        {

            if (!File.Exists(modFile))
            {
                GenerateNewModFile();
            }
            if (!Directory.Exists(universeDirectory))
            {
                Directory.CreateDirectory(universeDirectory);
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Crafts")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Crafts"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Flags")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Flags"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Players")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Players"));
            }
            if (!Settings.settingsStore.sendPlayerToLatestSubspace)
            {
                if (!Directory.Exists(Path.Combine(universeDirectory, "OfflinePlayerTimes")))
                {
                    Directory.CreateDirectory(Path.Combine(universeDirectory, "OfflinePlayerTimes"));
                }
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Kerbals")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Kerbals"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Vessels")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Vessels"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Scenarios")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Scenarios"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Scenarios", "Initial")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Scenarios", "Initial"));
            }
        }
        //Get mod file SHA
        public static string GetModControlSHA()
        {
            return Common.CalculateSHA256Hash(modFile);
        }

        public static void GenerateNewModFile()
        {
            if (File.Exists(modFile))
            {
                File.Move(modFile, modFile + ".bak");
            }
            string modFileData = Common.GenerateModFileStringData(new string[0], new string[0], false, new string[0], Common.GetStockParts().ToArray());
            using (StreamWriter sw = new StreamWriter(modFile))
            {
                sw.Write(modFileData);
            }
        }
        //Shutdown
        public static void ShutDown(string commandArgs)
        {
            if (commandArgs != "")
            {
                DarkLog.Normal("Shutting down - " + commandArgs);
                Messages.ConnectionEnd.SendConnectionEndToAll("Server is shutting down - " + commandArgs);
            }
            else
            {
                DarkLog.Normal("Shutting down");
                Messages.ConnectionEnd.SendConnectionEndToAll("Server is shutting down");
            }
            serverStarting = false;
            serverRunning = false;
            StopHTTPServer();
        }
        //Restart
        private static void Restart(string commandArgs)
        {
            if (commandArgs != "")
            {
                DarkLog.Normal("Restarting - " + commandArgs);
                Messages.ConnectionEnd.SendConnectionEndToAll("Server is restarting - " + commandArgs);
            }
            else
            {
                DarkLog.Normal("Restarting");
                Messages.ConnectionEnd.SendConnectionEndToAll("Server is restarting");
            }
            serverRestarting = true;
            serverStarting = false;
            serverRunning = false;
            ForceStopHTTPServer();
        }
        //Gracefully shut down
        private static void CatchExit(object sender, ConsoleCancelEventArgs args)
        {
            //If control+c not pressed within 5 seconds, catch it and shutdown gracefully.
            if ((DateTime.UtcNow.Ticks - ctrlCTime) > 50000000)
            {
                ctrlCTime = DateTime.UtcNow.Ticks;
                args.Cancel = true;
                ShutDown("Caught Ctrl+C");
            }
            else
            {
                DarkLog.Debug("Terminating!");
            }
        }

        private static void StartHTTPServer()
        {
            string OS = Environment.OSVersion.Platform.ToString();
            if (Settings.settingsStore.httpPort > 0)
            {
                DarkLog.Normal("Starting HTTP server...");
                httpListener = new HttpListener();
                try
                {
                    if (Settings.settingsStore.address != "0.0.0.0" && Settings.settingsStore.address != "::")
                    {
                        string listenAddress = Settings.settingsStore.address;
                        if (listenAddress.Contains(":"))
                        {
                            //Sorry
                            DarkLog.Error("Error: The server status port does not support specific IPv6 addresses. Sorry.");
                            //listenAddress = "[" + listenAddress + "]";
                            return;

                        }

                        httpListener.Prefixes.Add("http://" + listenAddress + ":" + Settings.settingsStore.httpPort + '/');
                    }
                    else
                    {
                        httpListener.Prefixes.Add("http://*:" + Settings.settingsStore.httpPort + '/');
                    }
                    httpListener.Start();
                    httpListener.BeginGetContext(asyncHTTPCallback, httpListener);
                }
                catch (HttpListenerException e)
                {
                    if (OS == "Win32NT" || OS == "Win32S" || OS == "Win32Windows" || OS == "WinCE") // if OS is Windows
                    {
                        if (e.ErrorCode == 5) // Access Denied
                        {
                            DarkLog.Debug("HTTP Server: access denied.");
                            DarkLog.Debug("Prompting user to switch to administrator mode.");

                            ProcessStartInfo startInfo = new ProcessStartInfo("DMPServer.exe") { Verb = "runas" };
                            Process.Start(startInfo);

                            Environment.Exit(0);
                        }
                    }
                    else
                    {
                        DarkLog.Fatal("Error while starting HTTP server.\n" + e);
                    }
                    throw;
                }
            }
        }

        private static void StopHTTPServer()
        {
            if (Settings.settingsStore.httpPort > 0)
            {
                DarkLog.Normal("Stopping HTTP server...");
                httpListener.Stop();
            }
        }

        private static void ForceStopHTTPServer()
        {
            if (Settings.settingsStore.httpPort > 0)
            {
                DarkLog.Normal("Force stopping HTTP server...");
                if (httpListener != null)
                {
                    try
                    {
                        httpListener.Abort();
                    }
                    catch (Exception e)
                    {
                        DarkLog.Fatal("Error trying to shutdown HTTP server: " + e);
                        throw;
                    }
                }
            }
        }

        private static void asyncHTTPCallback(IAsyncResult result)
        {
            try
            {
                HttpListener listener = (HttpListener)result.AsyncState;

                HttpListenerContext context = listener.EndGetContext(result);
                string responseText = "";
                bool handled = false;

                if (context.Request.Url.PathAndQuery.StartsWith("/modcontrol"))
                {
                    if (!File.Exists(modFile))
                    {
                        GenerateNewModFile();
                    }
                    responseText = File.ReadAllText(modFile);
                    handled = true;
                }
                if (!handled)
                {
                    responseText = new ServerInfo(Settings.settingsStore).GetJSON();
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                context.Response.ContentLength64 = buffer.LongLength;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();

                listener.BeginGetContext(asyncHTTPCallback, listener);
            }
            catch (Exception e)
            {
                //Ignore the EngGetContext throw while shutting down the HTTP server.
                if (serverRunning)
                {
                    DarkLog.Error("Exception while listening to HTTP server!, Exception:\n" + e);
                    Thread.Sleep(1000);
                    httpListener.BeginGetContext(asyncHTTPCallback, httpListener);
                }
            }
        }
    }
}

