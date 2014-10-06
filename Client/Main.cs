using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;
using System.Reflection;

namespace DarkMultiPlayer
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class Client : MonoBehaviour
    {
        private static Client singleton;
        //Global state vars
        public string status;
        public bool startGame;
        public bool forceQuit;
        public bool showGUI = true;
        public bool incorrectlyInstalled = false;
        public bool modDisabled = false;
        public bool displayedIncorrectMessage = false;
        public bool dmpSaveChecked = false;
        public string assemblyPath;
        public string assemblyShouldBeInstalledAt;
        //Game running is directly set from NetworkWorker.fetch after a successful connection
        public bool gameRunning;
        public bool fireReset;
        public GameMode gameMode;
        public bool serverAllowCheats = true;
        //Disconnect message
        public bool displayDisconnectMessage;
        private ScreenMessage disconnectMessage;
        private float lastDisconnectMessageCheck;
        public static List<Action> updateEvent = new List<Action>();
        public static List<Action> fixedUpdateEvent = new List<Action>();
        public static List<Action> drawEvent = new List<Action>();
        public static List<Action> resetEvent = new List<Action>();
        public static object eventLock = new object();
        //Chosen by a 2147483647 sided dice roll. Guaranteed to be random.
        public const int WINDOW_OFFSET = 1664952404;
        //Hack gravity fix.
        private Dictionary<CelestialBody, double> bodiesGees = new Dictionary<CelestialBody,double>();

        public Client()
        {
            singleton = this;
        }

        public static Client fetch
        {
            get
            {
                return singleton;
            }
        }

        public void Awake()
        {
            Profiler.DMPReferenceTime.Start();
            GameObject.DontDestroyOnLoad(this);
            assemblyPath = new DirectoryInfo(Assembly.GetExecutingAssembly().Location).FullName;
            string kspPath = new DirectoryInfo(KSPUtil.ApplicationRootPath).FullName;
            //I find my abuse of Path.Combine distrubing.
            assemblyShouldBeInstalledAt = Path.Combine(Path.Combine(Path.Combine(Path.Combine(kspPath, "GameData"), "DarkMultiPlayer"), "Plugins"), "DarkMultiPlayer.dll");
            UnityEngine.Debug.Log("KSP installed at " + kspPath);
            UnityEngine.Debug.Log("DMP installed at " + assemblyPath);
            incorrectlyInstalled = (assemblyPath.ToLower() != assemblyShouldBeInstalledAt.ToLower());
            if (incorrectlyInstalled)
            {
                UnityEngine.Debug.LogError("DMP is installed at '" + assemblyPath + "', It should be installed at '" + assemblyShouldBeInstalledAt + "'");
                return;
            }
            if (Settings.fetch.disclaimerAccepted != 1)
            {
                modDisabled = true;
                DisclaimerWindow.Enable();
            }
            SetupDirectoriesIfNeeded();
            //Register events needed to bootstrap the workers.
            lock (eventLock)
            {
                resetEvent.Add(LockSystem.Reset);
                resetEvent.Add(AdminSystem.Reset);
                resetEvent.Add(AsteroidWorker.Reset);
                resetEvent.Add(ChatWorker.Reset);
                resetEvent.Add(CraftLibraryWorker.Reset);
                resetEvent.Add(DebugWindow.Reset);
                resetEvent.Add(DynamicTickWorker.Reset);
                resetEvent.Add(FlagSyncer.Reset);
                resetEvent.Add(PlayerColorWorker.Reset);
                resetEvent.Add(PlayerStatusWindow.Reset);
                resetEvent.Add(PlayerStatusWorker.Reset);
                resetEvent.Add(QuickSaveLoader.Reset);
                resetEvent.Add(ScenarioWorker.Reset);
                resetEvent.Add(ScreenshotWorker.Reset);
                resetEvent.Add(TimeSyncer.Reset);
                resetEvent.Add(VesselWorker.Reset);
                resetEvent.Add(WarpWorker.Reset);
                GameEvents.onHideUI.Add(() =>
                {
                    showGUI = false;
                });
                GameEvents.onShowUI.Add(() =>
                {
                    showGUI = true;
                });
            }
            FireResetEvent();
            DarkLog.Debug("DarkMultiPlayer " + Common.PROGRAM_VERSION + ", protocol " + Common.PROTOCOL_VERSION + " Initialized!");
        }

        public void Update()
        {
            long startClock = Profiler.DMPReferenceTime.ElapsedTicks;
            DarkLog.Update();
            if (modDisabled)
            {
                return;
            }
            if (incorrectlyInstalled)
            {
                if (!displayedIncorrectMessage)
                {
                    displayedIncorrectMessage = true;
                    IncorrectInstallWindow.Enable();
                }
                return;
            }
            try
            {
                if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                {
                    if (!ModWorker.fetch.dllListBuilt)
                    {
                        ModWorker.fetch.dllListBuilt = true;
                        ModWorker.fetch.BuildDllFileList();
                    }
                    if (!dmpSaveChecked)
                    {
                        dmpSaveChecked = true;
                        SetupBlankGameIfNeeded();
                    }
                }

                //Handle GUI events
                if (!PlayerStatusWindow.fetch.disconnectEventHandled)
                {
                    PlayerStatusWindow.fetch.disconnectEventHandled = true;
                    forceQuit = true;
                    NetworkWorker.fetch.SendDisconnect("Quit");
                }
                if (!ConnectionWindow.fetch.renameEventHandled)
                {
                    PlayerStatusWorker.fetch.myPlayerStatus.playerName = Settings.fetch.playerName;
                    Settings.fetch.SaveSettings();
                    ConnectionWindow.fetch.renameEventHandled = true;
                }
                if (!ConnectionWindow.fetch.addEventHandled)
                {
                    Settings.fetch.servers.Add(ConnectionWindow.fetch.addEntry);
                    ConnectionWindow.fetch.addEntry = null;
                    Settings.fetch.SaveSettings();
                    ConnectionWindow.fetch.addingServer = false;
                    ConnectionWindow.fetch.addEventHandled = true;
                }
                if (!ConnectionWindow.fetch.editEventHandled)
                {
                    Settings.fetch.servers[ConnectionWindow.fetch.selected].name = ConnectionWindow.fetch.editEntry.name;
                    Settings.fetch.servers[ConnectionWindow.fetch.selected].address = ConnectionWindow.fetch.editEntry.address;
                    Settings.fetch.servers[ConnectionWindow.fetch.selected].port = ConnectionWindow.fetch.editEntry.port;
                    ConnectionWindow.fetch.editEntry = null;
                    Settings.fetch.SaveSettings();
                    ConnectionWindow.fetch.addingServer = false;
                    ConnectionWindow.fetch.editEventHandled = true;
                }
                if (!ConnectionWindow.fetch.removeEventHandled)
                {
                    Settings.fetch.servers.RemoveAt(ConnectionWindow.fetch.selected);
                    ConnectionWindow.fetch.selected = -1;
                    Settings.fetch.SaveSettings();
                    ConnectionWindow.fetch.removeEventHandled = true;
                }
                if (!ConnectionWindow.fetch.connectEventHandled)
                {
                    ConnectionWindow.fetch.connectEventHandled = true;
                    NetworkWorker.fetch.ConnectToServer(Settings.fetch.servers[ConnectionWindow.fetch.selected].address, Settings.fetch.servers[ConnectionWindow.fetch.selected].port);
                }

                if (!ConnectionWindow.fetch.disconnectEventHandled)
                {
                    ConnectionWindow.fetch.disconnectEventHandled = true;
                    gameRunning = false;
                    fireReset = true;
                    if (NetworkWorker.fetch.state == ClientState.CONNECTING)
                    {
                        NetworkWorker.fetch.Disconnect("Cancelled connection to server");
                    }
                    else
                    {
                        NetworkWorker.fetch.SendDisconnect("Quit during initial sync");
                    }
                }

                foreach (Action updateAction in updateEvent)
                {
                    try
                    {
                        updateAction();
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("Threw in UpdateEvent, exception: " + e);
                        if (NetworkWorker.fetch.state != ClientState.RUNNING)
                        {
                            if (NetworkWorker.fetch.state != ClientState.DISCONNECTED)
                            {
                                NetworkWorker.fetch.SendDisconnect("Unhandled error while syncing!");
                            }
                            else
                            {
                                NetworkWorker.fetch.Disconnect("Unhandled error while syncing!");
                            }
                        }
                    }
                }
                //Force quit
                if (forceQuit)
                {
                    forceQuit = false;
                    gameRunning = false;
                    fireReset = true;
                    StopGame();
                }

                if (displayDisconnectMessage)
                {
                    if (HighLogic.LoadedScene != GameScenes.MAINMENU)
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
                    else
                    {
                        displayDisconnectMessage = false;
                    }
                }

                //Normal quit
                if (gameRunning)
                {
                    if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                    {
                        gameRunning = false;
                        fireReset = true;
                        NetworkWorker.fetch.SendDisconnect("Quit to main menu");
                    }

                    if (ScreenshotWorker.fetch.uploadScreenshot)
                    {
                        ScreenshotWorker.fetch.uploadScreenshot = false;
                        StartCoroutine(UploadScreenshot());
                    }

                    if (HighLogic.CurrentGame.flagURL != Settings.fetch.selectedFlag)
                    {
                        DarkLog.Debug("Saving selected flag");
                        Settings.fetch.selectedFlag = HighLogic.CurrentGame.flagURL;
                        Settings.fetch.SaveSettings();
                        FlagSyncer.fetch.flagChangeEvent = true;
                    }

                    // save every GeeASL from each body in FlightGlobals
                    if (HighLogic.LoadedScene == GameScenes.FLIGHT && bodiesGees.Count == 0)
                    {
                        foreach (CelestialBody body in FlightGlobals.fetch.bodies)
                        {
                            bodiesGees.Add(body, body.GeeASL);
                        }
                    }

                    //handle use of cheats
                    if (!serverAllowCheats)
                    {
                        CheatOptions.InfiniteFuel = false;
                        CheatOptions.InfiniteEVAFuel = false;
                        CheatOptions.InfiniteRCS = false;
                        CheatOptions.NoCrashDamage = false;

                        foreach (KeyValuePair<CelestialBody, double> gravityEntry in bodiesGees)
                        {
                            gravityEntry.Key.GeeASL = gravityEntry.Value;
                        }
                    }

                    if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.fetch.activeVessel != null)
                    {
                        HighLogic.CurrentGame.Parameters.Flight.CanLeaveToSpaceCenter = (FlightGlobals.ClearToSave() == ClearToSaveStatus.CLEAR);
                    }
                    else
                    {
                        HighLogic.CurrentGame.Parameters.Flight.CanLeaveToSpaceCenter = true;
                    }
                }

                if (fireReset)
                {
                    fireReset = false;
                    FireResetEvent();
                }

                if (startGame)
                {
                    startGame = false;
                    StartGame();
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Threw in Update, state " + NetworkWorker.fetch.state.ToString() + ", exception" + e);
                if (NetworkWorker.fetch.state != ClientState.RUNNING)
                {
                    if (NetworkWorker.fetch.state != ClientState.DISCONNECTED)
                    {
                        NetworkWorker.fetch.SendDisconnect("Unhandled error while syncing!");
                    }
                    else
                    {
                        NetworkWorker.fetch.Disconnect("Unhandled error while syncing!");
                    }
                }
            }
            Profiler.updateData.ReportTime(startClock);
        }

        public IEnumerator<WaitForEndOfFrame> UploadScreenshot()
        {
            yield return new WaitForEndOfFrame();
            ScreenshotWorker.fetch.SendScreenshot();
            ScreenshotWorker.fetch.screenshotTaken = true;
        }

        public void FixedUpdate()
        {
            long startClock = Profiler.DMPReferenceTime.ElapsedTicks;
            if (modDisabled)
            {
                return;
            }
            foreach (Action fixedUpdateAction in fixedUpdateEvent)
            {
                try
                {
                    fixedUpdateAction();
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Threw in FixedUpdate event, exception: " + e);
                    if (NetworkWorker.fetch.state != ClientState.RUNNING)
                    {
                        if (NetworkWorker.fetch.state != ClientState.DISCONNECTED)
                        {
                            NetworkWorker.fetch.SendDisconnect("Unhandled error while syncing!");
                        }
                        else
                        {
                            NetworkWorker.fetch.Disconnect("Unhandled error while syncing!");
                        }
                    }
                }
            }
            Profiler.fixedUpdateData.ReportTime(startClock);
        }

        public void OnGUI()
        {
            //Window ID's - Doesn't include "random" offset.
            //Connection window: 6702
            //Status window: 6703
            //Chat window: 6704
            //Debug window: 6705
            //Mod windw: 6706
            //Craft library window: 6707
            //Craft upload window: 6708
            //Screenshot window: 6710
            //Options window: 6711
            //Converter window: 6712
            //Disclaimer window: 6713
            long startClock = Profiler.DMPReferenceTime.ElapsedTicks;
            if (showGUI)
            {
                foreach (Action drawAction in drawEvent)
                {
                    try
                    {
                        drawAction();
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("Threw in OnGUI event, exception: " + e);
                    }
                }
            }
            Profiler.guiData.ReportTime(startClock);
        }

        private void StartGame()
        {
            //Create new game object for our DMP session.
            HighLogic.CurrentGame = CreateBlankGame();

            //Set the game mode
            SetGameMode();

            //Found in KSP's files. Makes a crapton of sense :)
            if (HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
            {
                HighLogic.CurrentGame.Parameters.Difficulty.AllowStockVessels = false;
            }
            HighLogic.CurrentGame.flightState.universalTime = TimeSyncer.fetch.GetUniverseTime();

            //Load DMP stuff
            VesselWorker.fetch.LoadKerbalsIntoGame();
            VesselWorker.fetch.LoadVesselsIntoGame();

            //Load the scenarios from the server
            ScenarioWorker.fetch.LoadScenarioDataIntoGame();

            //Load the missing scenarios as well (Eg, Contracts and stuff for career mode
            ScenarioWorker.fetch.LoadMissingScenarioDataIntoGame();

            //This only makes KSP complain
            HighLogic.CurrentGame.CrewRoster.ValidateAssignments(HighLogic.CurrentGame);
            DarkLog.Debug("Starting " + gameMode + " game...");

            //.Start() seems to stupidly .Load() somewhere - Let's overwrite it so it loads correctly.
            GamePersistence.SaveGame(HighLogic.CurrentGame, "persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            HighLogic.CurrentGame.Start();
            ChatWorker.fetch.display = true;
            DarkLog.Debug("Started!");
        }

        private void StopGame()
        {
            HighLogic.SaveFolder = "DarkMultiPlayer";
            if (HighLogic.LoadedScene != GameScenes.MAINMENU)
            {
                HighLogic.LoadScene(GameScenes.MAINMENU);
            }
            HighLogic.CurrentGame = null;
            bodiesGees.Clear();
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
                case GameMode.SCIENCE:
                    HighLogic.CurrentGame.Mode = Game.Modes.SCIENCE_SANDBOX;
                    break;
            }
        }

        private void FireResetEvent()
        {
            foreach (Action resetAction in resetEvent)
            {
                try
                {
                    resetAction();
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Threw in FireResetEvent, exception: " + e);
                }
            }
        }

        private void SetupDirectoriesIfNeeded()
        {
            string darkMultiPlayerSavesDirectory = Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "saves"), "DarkMultiPlayer");
            CreateIfNeeded(darkMultiPlayerSavesDirectory);
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, "Ships"));
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, Path.Combine("Ships", "VAB")));
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, Path.Combine("Ships", "SPH")));
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, "Subassemblies"));
            string darkMultiPlayerCacheDirectory = Path.Combine(Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "GameData"), "DarkMultiPlayer"), "Cache");
            CreateIfNeeded(darkMultiPlayerCacheDirectory);
            string darkMultiPlayerFlagsDirectory = Path.Combine(Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "GameData"), "DarkMultiPlayer"), "Flags");
            CreateIfNeeded(darkMultiPlayerFlagsDirectory);
        }

        private void SetupBlankGameIfNeeded()
        {
            string persistentFile = Path.Combine(Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "saves"), "DarkMultiPlayer"), "persistent.sfs");
            if (!File.Exists(persistentFile))
            {
                DarkLog.Debug("Creating new blank persistent.sfs file");
                Game blankGame = CreateBlankGame();
                HighLogic.SaveFolder = "DarkMultiPlayer";
                GamePersistence.SaveGame(blankGame, "persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            }
        }

        private Game CreateBlankGame()
        {
            Game returnGame = new Game();
            //KSP complains about a missing message system if we don't do this.
            returnGame.additionalSystems = new ConfigNode();
            returnGame.additionalSystems.AddNode("MESSAGESYSTEM");

            //Flightstate is null on new Game();
            returnGame.flightState = new FlightState();

            //DMP stuff
            returnGame.startScene = GameScenes.SPACECENTER;
            returnGame.flagURL = Settings.fetch.selectedFlag;
            returnGame.Title = "DarkMultiPlayer";
            returnGame.Parameters.Flight.CanQuickLoad = false;
            returnGame.Parameters.Flight.CanRestart = false;
            returnGame.Parameters.Flight.CanLeaveToEditor = false;
            HighLogic.SaveFolder = "DarkMultiPlayer";

            return returnGame;
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

