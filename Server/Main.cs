using System;
using System.IO;
using System.Threading;
using System.Diagnostics;

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
            //Load settings
            DarkLog.Normal("Loading universe... ");
            CheckUniverse();
            DarkLog.Normal("Done!");
            DarkLog.Normal("Loading settings... ");
            Settings.Load();
            DarkLog.Normal("Done!");
            DarkLog.Normal("Starting " + Settings.warpMode + " server on port " + Settings.port + "... ");
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

