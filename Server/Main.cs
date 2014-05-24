using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    public class Server
    {
        public static bool serverRunning;
        public static bool serverStarting;
        public static bool serverRestarting;
        public static string universeDirectory;
        public static Stopwatch serverClock;
        private static long ctrlCTime;

        public static void Main()
        {
            //Start the server clock
            serverClock = new Stopwatch();
            serverClock.Start();
            //Register the server commands
            CommandHandler.RegisterCommand("exit", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("quit", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("shutdown", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("restart", Server.Restart, "Restarts the server");
            CommandHandler.RegisterCommand("kick", ClientHandler.KickPlayer, "Kicks a player from the server");
            CommandHandler.RegisterCommand("ban", ClientHandler.BanPlayer, "Bans a player from the server");
            CommandHandler.RegisterCommand("banip", ClientHandler.BanIP, "Bans an IP Address from the server");
            CommandHandler.RegisterCommand("banguid", ClientHandler.BanGuid, "Bans a Guid from the server");
            CommandHandler.RegisterCommand("whisper", ClientHandler.WhisperCommand, "Whispers a message to a player");
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

                while (serverRunning)
                {
                    Thread.Sleep(500);
                }
                commandThread.Abort();
                clientThread.Join();
            }
            DarkLog.Normal("Goodbye!");
        }
        //Create universe directories
        private static void CheckUniverse()
        {
            universeDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Universe");
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
    }
}

