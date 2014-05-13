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
        public static string universeDirectory;
        public static Stopwatch serverClock;

        public static void Main()
        {
            //Start the server clock
            serverClock = new Stopwatch();
            serverClock.Start();
            //Register the ctrl+c event
            Console.CancelKeyPress += new ConsoleCancelEventHandler(CatchExit);

            DarkLog.Normal("Loading universe... ");
            CheckUniverse();
            DarkLog.Normal("Done!");

			//Load settings
            DarkLog.Normal("Loading settings... ");
			Settings.RegisterEnumConfigKey("warpmode", WarpMode.SUBSPACE, typeof(WarpMode), new string[]{ "Specify the warp tyoe" });
			Settings.RegisterConfigKey("port", 6702, Settings.ConfigType.INTEGER, new string[]{ "The port the server listens on" });
			Settings.RegisterEnumConfigKey("gamemode", GameMode.SANDBOX, typeof(GameMode), new string[]{ "Specify the game type" });
			Settings.RegisterConfigKey("modcontrol", true, Settings.ConfigType.BOOLEAN, new string[]{ "Enable mod control", "WARNING: Only consider turning off mod control for private servers.", "The game will constantly complain about missing parts if there are missing mods." });
            Settings.Load();
            DarkLog.Normal("Done!");

			DarkLog.Normal("Starting " + Settings.getEnum("warpmode") + " server on port " + Settings.getInteger("port") + "... ");
            serverStarting = true;
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

            CommandHandler.RegisterCommand("exit", (x) => ShutDown(), "Shuts down the server");

            while (serverRunning)
            {
                Thread.Sleep(500);
            }

            DarkLog.Normal("Shutting down... ");
            commandThread.Join();
            clientThread.Join();
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
        private static void ShutDown()
        {
            serverStarting = false;
            serverRunning = false;
        }
        //Gracefully shut down
        private static void CatchExit(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            ShutDown();
        }
    }
}

