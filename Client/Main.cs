using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;

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
        public GameMode gameMode;
        //Disconnect message
        public bool displayDisconnectMessage;
        private ScreenMessage disconnectMessage;
        private float lastDisconnectMessageCheck;
        //Singletons
        public TimeSyncer timeSyncer;
        public VesselWorker vesselWorker;
        public NetworkWorker networkWorker;
        public PlayerStatusWorker playerStatusWorker;
        public WarpWorker warpWorker;
        public ScenarioWorker scenarioWorker;
        public DynamicTickWorker dynamicTickWorker;
        public Settings settings;
        private ConnectionWindow connectionWindow;
        private PlayerStatusWindow playerStatusWindow;
        public ChatWindow chatWindow;
        public DebugWindow debugWindow;
        private QuickSaveLoader quickSaveLoader;

        public void Awake()
        {
            GameObject.DontDestroyOnLoad(this);
            SetupDirectoriesIfNeeded();
            settings = new Settings();
            timeSyncer = new TimeSyncer(this);
            networkWorker = new NetworkWorker(this);
            vesselWorker = new VesselWorker(this);
            warpWorker = new WarpWorker(this);
            scenarioWorker = new ScenarioWorker(this);
            dynamicTickWorker = new DynamicTickWorker(this);
            connectionWindow = new ConnectionWindow(this);
            playerStatusWorker = new PlayerStatusWorker(this);
            playerStatusWindow = new PlayerStatusWindow(this);
            quickSaveLoader = new QuickSaveLoader(this);
            chatWindow = new ChatWindow(this);
            debugWindow = new DebugWindow(this);
            DarkLog.Debug("DarkMultiPlayer Initialized!");
        }

        public void Start()
        {
        }

        public void Update()
        {
            try
            {
                //Write new log entries
                DarkLog.Update();
                if (displayDisconnectMessage)
                {
                    if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                    {
                        displayDisconnectMessage = false;
                    }
                    else
                    {
                        if ((UnityEngine.Time.realtimeSinceStartup - lastDisconnectMessageCheck) > 1f)
                        {
                            lastDisconnectMessageCheck = UnityEngine.Time.realtimeSinceStartup;
                            if (disconnectMessage != null)
                            {
                                disconnectMessage.duration = 0;
                            }
                            disconnectMessage = ScreenMessages.PostScreenMessage("You have been disconnected!", 2f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                }
                //Handle GUI events
                if (!playerStatusWindow.disconnectEventHandled)
                {
                    playerStatusWindow.disconnectEventHandled = true;
                    forceQuit = true;
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
                playerStatusWindow.safeMinimized = playerStatusWindow.minmized;

                //Call the update hooks
                networkWorker.Update();
                playerStatusWorker.Update();
                warpWorker.Update();
                playerStatusWindow.Update();
                chatWindow.Update();
                debugWindow.Update();
                quickSaveLoader.Update();
                scenarioWorker.Update();
                dynamicTickWorker.Update();

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
            catch (Exception e)
            {
                DarkLog.Debug("Threw in Update, exception" + e);
            }
        }

        public void FixedUpdate()
        {
            try
            {
                timeSyncer.FixedUpdate();
                vesselWorker.FixedUpdate();
            }
            catch (Exception e)
            {
                DarkLog.Debug("Threw in FixedUpdate, exception: " + e);
            }
        }

        public void OnGUI()
        {
            try
            {
                connectionWindow.Draw();
                playerStatusWindow.Draw();
                chatWindow.Draw();
                debugWindow.Draw();
            }
            catch (Exception e)
            {
                DarkLog.Debug("Threw in OnGUI, exception: " + e);
            }
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
            HighLogic.CurrentGame.scenarios = new List<ProtoScenarioModule>();
            HighLogic.CurrentGame.startScene = GameScenes.SPACECENTER;
            HighLogic.CurrentGame.Title = "DarkMultiPlayer";
            HighLogic.CurrentGame.Parameters.Flight.CanQuickLoad = false;
            HighLogic.SaveFolder = "DarkMultiPlayer";
            HighLogic.CurrentGame.flightState.universalTime = timeSyncer.GetUniverseTime();
            SetGameMode();
            scenarioWorker.LoadScenarioDataIntoGame();
            vesselWorker.LoadKerbalsIntoGame();
            vesselWorker.LoadVesselsIntoGame();
            DarkLog.Debug("Starting " + gameMode + " game...");
            HighLogic.CurrentGame.Start();
            DarkLog.Debug("Started!");
            Planetarium.SetUniversalTime(timeSyncer.GetUniverseTime());
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            chatWindow.display = true;
        }

        private void StopGame()
        {
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            if (HighLogic.LoadedScene != GameScenes.MAINMENU)
            {
                HighLogic.LoadScene(GameScenes.MAINMENU);
            }
        }

        private void SetGameMode()
        {
            switch (gameMode)
            {
                case GameMode.CAREER:
                    HighLogic.CurrentGame.Mode = Game.Modes.CAREER;
                    break;
                case GameMode.SANDBOX:
                    HighLogic.CurrentGame.Mode = Game.Modes.SANDBOX;
                    break;
            }
        }

        private void ResetWorkers()
        {
            chatWindow.display = false;
            debugWindow.display = false;
            timeSyncer.Reset();
            vesselWorker.Reset();
            playerStatusWorker.Reset();
            warpWorker.Reset();
            chatWindow.Reset();
            scenarioWorker.Reset();
            dynamicTickWorker.Reset();
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

