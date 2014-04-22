using System;
using System.IO;
using UnityEngine;

namespace DarkMultiPlayer
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class Client : MonoBehaviour
    {
        //Global state vars
        public string status;
        public bool forceQuit;
        //Game running is directly set from networkWorker after a successful connection
        public bool gameRunning;
        //Singletons
        public TimeSyncer timeSyncer;
        public VesselWorker vesselWorker;
        public NetworkWorker networkWorker;
        public PlayerStatusWorker playerStatusWorker;
        public WarpWorker warpWorker;
        public Settings settings;
        private ConnectionWindow connectionWindow;
        private PlayerStatusWindow playerStatusWindow;

        public void Awake()
        {
            GameObject.DontDestroyOnLoad(this);
            SetupDirectoriesIfNeeded();
            timeSyncer = new TimeSyncer(this);
            vesselWorker = new VesselWorker(this);
            networkWorker = new NetworkWorker(this);
            warpWorker = new WarpWorker(this);
            settings = new Settings();
            connectionWindow = new ConnectionWindow(this);
            playerStatusWorker = new PlayerStatusWorker(this);
            playerStatusWindow = new PlayerStatusWindow(this);
            DarkLog.Debug("DarkMultiPlayer Initialized!");
        }

        public void Start()
        {
        }

        public void Update()
        {
            //Write new log entries
            DarkLog.Update();

            //Handle GUI events
            if (!playerStatusWindow.disconnectEventHandled)
            {
                playerStatusWindow.disconnectEventHandled = true;
                networkWorker.SendDisconnect("Quit");
            }
            if (!connectionWindow.renameEventHandled)
            {
                playerStatusWorker.myPlayerStatus.playerName = settings.playerName;
                settings.SaveSettings();
                connectionWindow.renameEventHandled = true;
            }
            if (!connectionWindow.addEventHandled)
            {
                settings.servers.Add(connectionWindow.addEntry);
                connectionWindow.addEntry = null;
                settings.SaveSettings();
                connectionWindow.addingServer = false;
                connectionWindow.addEventHandled = true;
            }
            if (!connectionWindow.editEventHandled)
            {
                settings.servers[connectionWindow.selected].name = connectionWindow.editEntry.name;
                settings.servers[connectionWindow.selected].address = connectionWindow.editEntry.address;
                settings.servers[connectionWindow.selected].port = connectionWindow.editEntry.port;
                connectionWindow.editEntry = null;
                settings.SaveSettings();
                connectionWindow.addingServer = false;
                connectionWindow.editEventHandled = true;
            }
            if (!connectionWindow.removeEventHandled)
            {
                settings.servers.RemoveAt(connectionWindow.selected);
                connectionWindow.selected = -1;
                settings.SaveSettings();
                connectionWindow.removeEventHandled = true;
            }
            if (!connectionWindow.connectEventHandled)
            {
                networkWorker.ConnectToServer(settings.servers[connectionWindow.selected].address, settings.servers[connectionWindow.selected].port);
                connectionWindow.connectEventHandled = true;
            }

            //Stop GUI from freaking out
            connectionWindow.status = status;
            connectionWindow.selectedSafe = connectionWindow.selected;
            connectionWindow.addingServerSafe = connectionWindow.addingServer;
            connectionWindow.display = (HighLogic.LoadedScene == GameScenes.MAINMENU);
            playerStatusWindow.display = gameRunning;

            //Call network worker
            networkWorker.Update();
            playerStatusWorker.Update();
            warpWorker.Update();

            //Force quit
            if (forceQuit)
            {
                forceQuit = false;
                gameRunning = false;
                ResetWorkers();
                networkWorker.SendDisconnect("Force quit to main menu");
                StopGame();
            }

            //Normal quit
            if (gameRunning == true && HighLogic.LoadedScene == GameScenes.MAINMENU)
            {
                gameRunning = false;
                ResetWorkers();
                networkWorker.SendDisconnect("Quit to main menu");
            }
        }

        public void FixedUpdate()
        {
            timeSyncer.FixedUpdate();
            vesselWorker.FixedUpdate();
        }

        public void OnGUI()
        {
            connectionWindow.Draw();
            playerStatusWindow.Draw();
        }

        public void OnDestroy()
        {
        }
        //WARNING: Called from NetworkWorker.
        public void StartGame()
        {
            HighLogic.CurrentGame = new Game();
            HighLogic.CurrentGame.flightState = new FlightState();
            HighLogic.CurrentGame.CrewRoster = new CrewRoster();
            HighLogic.CurrentGame.startScene = GameScenes.SPACECENTER;
            HighLogic.CurrentGame.Title = "DarkMultiPlayer";
            HighLogic.SaveFolder = "DarkMultiPlayer";
            HighLogic.CurrentGame.flightState.universalTime = timeSyncer.GetUniverseTime();
            vesselWorker.LoadKerbalsIntoGame();
            vesselWorker.LoadVesselsIntoGame();
            HighLogic.CurrentGame.Start();
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
        }

        private void StopGame()
        {
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            if (HighLogic.LoadedScene != GameScenes.MAINMENU)
            {
                HighLogic.LoadScene(GameScenes.MAINMENU);
            }
        }

        private void ResetWorkers()
        {
            timeSyncer.Reset();
            vesselWorker.Reset();
            playerStatusWorker.Reset();
            warpWorker.Reset();
        }

        private void SetupDirectoriesIfNeeded()
        {
            string darkMultiPlayerSavesDirectory = Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("saves", "DarkMultiPlayer"));
            CreateIfNeeded(darkMultiPlayerSavesDirectory);
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, "Ships"));
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, Path.Combine("Ships", "VAB")));
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, Path.Combine("Ships", "SPH")));
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, "Subassemblies"));
            string darkMultiPlayerDataDirectory = Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("GameData", Path.Combine("DarkMultiPlayer", Path.Combine("Plugins", "Data"))));
            CreateIfNeeded(darkMultiPlayerDataDirectory);
        }

        private void CreateIfNeeded(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}

