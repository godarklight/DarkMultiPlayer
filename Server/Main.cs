using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Net;
using System.Text;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    public class Server
    {
        public static bool serverRunning;
        public static bool serverStarting;
        public static bool serverRestarting;
        public static string universeDirectory;
        public static string pluginDirectory;
        public static Stopwatch serverClock;
        public static HttpListener httpListener;
        private static long ctrlCTime;
        public static int playerCount = 0;
        public static string players = "";
        public static long lastPlayerActivity;
        public static object universeSizeLock = new object();

        public static void Main()
        {
            try
            {
                //Start the server clock
                serverClock = new Stopwatch();
                serverClock.Start();
                lastPlayerActivity = serverClock.ElapsedTicks;
                //Register the server commands
                CommandHandler.RegisterCommand("exit", Server.ShutDown, "Shuts down the server");
                CommandHandler.RegisterCommand("quit", Server.ShutDown, "Shuts down the server");
                CommandHandler.RegisterCommand("shutdown", Server.ShutDown, "Shuts down the server");
                CommandHandler.RegisterCommand("restart", Server.Restart, "Restarts the server");
                CommandHandler.RegisterCommand("kick", ClientHandler.KickPlayer, "Kicks a player from the server");
                CommandHandler.RegisterCommand("ban", ClientHandler.BanPlayer, "Bans a player from the server");
                CommandHandler.RegisterCommand("banip", ClientHandler.BanIP, "Bans an IP Address from the server");
                CommandHandler.RegisterCommand("banguid", ClientHandler.BanGuid, "Bans a Guid from the server");
                CommandHandler.RegisterCommand("pm", ClientHandler.PMCommand, "Sends a message to a player");
                CommandHandler.RegisterCommand("admin", ClientHandler.AdminCommand, "Sets a player as admin/removes admin from the player");
                //Register the ctrl+c event
                Console.CancelKeyPress += new ConsoleCancelEventHandler(CatchExit);
                serverStarting = true;
                while (serverStarting || serverRestarting)
                {
                    serverRestarting = false;
                    DarkLog.Normal("Starting DMPServer " + Common.PROGRAM_VERSION + ", protocol " + Common.PROTOCOL_VERSION);

                    //Load settings
                    DarkLog.Normal("Loading universe... ");
                    CheckUniverse();
                    DarkLog.Normal("Done!");

                    //Load plugins
                    DMPPluginHandler.LoadPlugins();

                    DarkLog.Normal("Loading settings... ");
                    Settings.Load();
                    DarkLog.Normal("Done!");

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
                    DarkLog.Normal("Done!");

                    StartHTTPServer();
                    DarkLog.Normal("Done!");
                    DMPPluginHandler.FireOnServerStart();
                    while (serverRunning)
                    {
                        Thread.Sleep(500);
                    }
                    DMPPluginHandler.FireOnServerStop();
                    commandThread.Abort();
                    clientThread.Join();
                }
                DarkLog.Normal("Goodbye!");
            }
            catch (Exception e)
            {
                DarkLog.Fatal("Error in main server thread, Exception: " + e);
                throw;
            }
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
            return (serverClock.ElapsedTicks - lastPlayerActivity) / 10000000;
        }

        //Create universe directories
        private static void CheckUniverse()
        {
            universeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Universe");
            pluginDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginDirectory))
            {
                Directory.CreateDirectory(pluginDirectory);
            }
            if (!Directory.Exists(universeDirectory))
            {
                Directory.CreateDirectory(universeDirectory);
            }
            if (!Directory.Exists(Path.Combine(Server.universeDirectory, "Crafts")))
            {
                Directory.CreateDirectory(Path.Combine(Server.universeDirectory, "Crafts"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Players")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Players"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Kerbals")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Kerbals"));
            }
            if (!Directory.Exists(Path.Combine(universeDirectory, "Vessels")))
            {
                Directory.CreateDirectory(Path.Combine(universeDirectory, "Vessels"));
            }
            if (!Directory.Exists(Path.Combine(Server.universeDirectory, "Scenarios")))
            {
                Directory.CreateDirectory(Path.Combine(Server.universeDirectory, "Scenarios"));
            }
            if (!Directory.Exists(Path.Combine(Server.universeDirectory, "Scenarios", "Initial")))
            {
                Directory.CreateDirectory(Path.Combine(Server.universeDirectory, "Scenarios", "Initial"));
            }

        }
        //Shutdown
        public static void ShutDown(string commandArgs)
        {
            if (commandArgs != "")
            {
                DarkLog.Normal("Shutting down - " + commandArgs);
                ClientHandler.SendConnectionEndToAll("Server is shutting down - " + commandArgs);
            }
            else
            {
                DarkLog.Normal("Shutting down");
                ClientHandler.SendConnectionEndToAll("Server is shutting down");
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
                ClientHandler.SendConnectionEndToAll("Server is restarting - " + commandArgs);
            }
            else
            {
                DarkLog.Normal("Restarting");
                ClientHandler.SendConnectionEndToAll("Server is restarting");
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
                ShutDown("Caught Crtl+C");
            }
            else
            {
                DarkLog.Debug("Terminating!");
            }
        }

        private static void StartHTTPServer()
        {
            if (Settings.settingsStore.httpPort > 0)
            {
                DarkLog.Normal("Starting HTTP server...");
                httpListener = new HttpListener();
                try
                {
                    if (Settings.settingsStore.address != "0.0.0.0")
                    {
                        httpListener.Prefixes.Add("http://" + Settings.settingsStore.address + ":" + Settings.settingsStore.httpPort + '/');
                    }
                    else
                    {
                        httpListener.Prefixes.Add("http://*:" + Settings.settingsStore.httpPort + '/');
                    }
                    httpListener.Start();
                    httpListener.BeginGetContext(asyncHTTPCallback, httpListener);
                }
                catch (Exception e)
                {
                    DarkLog.Fatal("Error while starting HTTP server: " + e + "\nPlease try running the server as an administrator.");
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
                        httpListener.Stop();
                        httpListener.Close();
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

                string responseText = new ServerInfo(Settings.settingsStore).GetJSON();

                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                context.Response.ContentLength64 = buffer.LongLength;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();

                listener.BeginGetContext(asyncHTTPCallback, listener);
            }
            catch (Exception e)
            {
                //Ignore the EngGetContext throw while shutting down the HTTP server.
                if (Server.serverRunning)
                {
                    DarkLog.Error("Exception while listening to HTTP server!, Exception:\n" + e);
                    Thread.Sleep(1000);
                    httpListener.BeginGetContext(asyncHTTPCallback, httpListener);
                }
            }
        }
    }
}

