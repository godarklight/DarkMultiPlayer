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
        private static long ctrlCTime;

        public static void Main()
        {
            //Start the server clock
            serverClock = new Stopwatch();
            serverClock.Start();
            //Register the exit/shutdown commands
            CommandHandler.RegisterCommand("exit", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("quit", Server.ShutDown, "Shuts down the server");
            CommandHandler.RegisterCommand("shutdown", Server.ShutDown, "Shuts down the server");
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
            commandThread.Abort();
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
        private static void ShutDown(string commandArgs)
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

        // KICK COMMAND
        private static void KickPlayer(string[] commandArgs)
        {
            /* What I think this command needs:
             * - find ClientObject by player name
             * - split the command args into 2
             */
            ClientObject kickPlayer = new ClientObject();
            if (commandArgs[1] != "")
            {
                DarkLog.Normal(String.Format("kicking {0} from the server: {1}", commandArgs[0], commandArgs[1]));
                ClientHandler.SendConnectionEndToClient(kickPlayer, commandArgs[1]);
            }
            else
            {
                DarkLog.Normal(String.Format("kicking {0} from the server: no reason specified", commandArgs[0]));
                ClientHandler.SendConnectionEndToClient(kickPlayer, "no reason specified");
            }
        }
    }
}

