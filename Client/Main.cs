using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;
using DarkMultiPlayer.Utilities;
using MessageStream2;
using System.Net;

namespace DarkMultiPlayer
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class Client : MonoBehaviour
    {
        private bool showGUI = true;
        public static bool toolbarShowGUI = true;
        public static bool modDisabled = false;
        public static bool warnDuplicateInstall = false;
        private bool dmpSaveChecked = false;
        private int facilitiesAdded = 0;
        private int stockSitesAdded = 0;
        private int modSitesAdded = 0;
        //Disconnect message
        public static bool displayDisconnectMessage;
        public static ScreenMessage disconnectMessage;
        public static float lastDisconnectMessageCheck;
        //Chosen by a 2147483647 sided dice roll. Guaranteed to be random.
        public const int WINDOW_OFFSET = 1664952404;
        //Hack gravity fix.
        private Dictionary<CelestialBody, double> bodiesGees = new Dictionary<CelestialBody, double>();
        //Command line connect
        private static ServerEntry commandLineConnect;
        //Thread safe RealTimeSinceStartup
        private static float lastRealTimeSinceStartup;
        private static long lastClockTicks;
        //This singleton is only for other mod access to the following services.
        public static Client dmpClient;
        //Services
        public Settings dmpSettings;
        public ToolbarSupport toolbarSupport;
        public UniverseSyncCache universeSyncCache;
        public ModWorker modWorker;
        public ModWindow modWindow;
        public ConnectionWindow connectionWindow;
        public OptionsWindow optionsWindow;
        public UniverseConverter universeConverter;
        public UniverseConverterWindow universeConverterWindow;
        public DisclaimerWindow disclaimerWindow;
        public ServersWindow serversWindow;
        public ServerListDisclaimerWindow serverListDisclaimerWindow;
        public ServerListConnection serverListConnection;
        public DMPModInterface dmpModInterface;
        //Profiler state
        public Profiler profiler;
        private long kspTime;
        private long kspMemory;
        public DMPGame dmpGame;
        public string dmpDir;
        public string dmpDataDir;
        public string gameDataDir;
        public string kspRootPath;
        //16kB, 512kB, 6MB, 64MB.
        public const int SMALL_MESSAGE_SIZE = 16 * 1024;
        public const int MEDIUM_MESSAGE_SIZE = 512 * 1024;
        public const int LARGE_MESSAGE_SIZE = 6 * 1024 * 1024;
        public const int EXTRA_LARGE_MESSAGE_SIZE = 64 * 1024 * 1024;

        public Client fetch
        {
            get
            {
                return dmpClient;
            }
        }

        public DMPGame fetchGame
        {
            get
            {
                return dmpGame;
            }
        }

        public static float realtimeSinceStartup
        {
            get
            {
                long ticksDiff = DateTime.UtcNow.Ticks - lastClockTicks;
                double secondsSinceUpdate = ticksDiff / (double)TimeSpan.TicksPerSecond;
                return lastRealTimeSinceStartup + (float)secondsSinceUpdate;
            }
        }

        public void Start()
        {
            //Set buffered UDPMesh
            UDPMeshLib.UdpMeshCommon.USE_BUFFERS = true;
            //Set pool sizes for ByteRecycler
            ByteRecycler.AddPoolSize(SMALL_MESSAGE_SIZE);
            ByteRecycler.AddPoolSize(MEDIUM_MESSAGE_SIZE);
            ByteRecycler.AddPoolSize(LARGE_MESSAGE_SIZE);
            ByteRecycler.AddPoolSize(EXTRA_LARGE_MESSAGE_SIZE);
            MessageWriter.RegisterType<ByteArray>(WriteByteArrayToStream);
            MessageReader.RegisterType<ByteArray>(ReadByteArrayFromStream);

            //Prevent loads if multiple copies of DMP are installed. KSP will instantate us twice.
            if (dmpClient != null)
            {
                warnDuplicateInstall = true;
                return;
            }

            if (!CompatibilityChecker.IsCompatible() || !InstallChecker.IsCorrectlyInstalled())
            {
                modDisabled = true;
                enabled = false;
                return;
            }

            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, TimingManagerFixedUpdate);
            dmpDir = Path.Combine(Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "GameData"), "DarkMultiPlayer"), "Plugins");
            dmpDataDir = Path.Combine(dmpDir, "Data");
            gameDataDir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData");
            kspRootPath = KSPUtil.ApplicationRootPath;

            //Fix DarkLog time/thread marker in the log during init.
            DarkLog.SetMainThread();
            lastClockTicks = DateTime.UtcNow.Ticks;
            lastRealTimeSinceStartup = 0f;

            dmpClient = this;
            profiler = new Profiler();
            kspTime = profiler.GetCurrentTime;
            kspMemory = profiler.GetCurrentMemory;

            dmpSettings = new Settings();
            toolbarSupport = new ToolbarSupport(dmpSettings);
            universeSyncCache = new UniverseSyncCache(dmpSettings);
            modWindow = new ModWindow();
            modWorker = new ModWorker(modWindow);
            modWindow.SetDependenices(modWorker);
            universeConverter = new UniverseConverter(dmpSettings);
            universeConverterWindow = new UniverseConverterWindow(universeConverter);
            serverListDisclaimerWindow = new ServerListDisclaimerWindow(dmpSettings);
            optionsWindow = new OptionsWindow(dmpSettings, universeSyncCache, modWorker, universeConverterWindow, toolbarSupport, serverListDisclaimerWindow);
            serverListConnection = new ServerListConnection(dmpSettings);
            serversWindow = new ServersWindow(dmpSettings, optionsWindow, serverListConnection);
            serverListConnection.SetDependancy(serversWindow);
            connectionWindow = new ConnectionWindow(dmpSettings, optionsWindow, serversWindow, serverListDisclaimerWindow);
            disclaimerWindow = new DisclaimerWindow(dmpSettings);
            dmpModInterface = new DMPModInterface();
            //SafetyBubble.RegisterDefaultLocations();


            if (dmpSettings.disclaimerAccepted != 1)
            {
                modDisabled = true;
                disclaimerWindow.SpawnDialog();
            }

            Application.wantsToQuit += WantsToQuit;
            DontDestroyOnLoad(this);

            // Prevents symlink warning for development.
            SetupDirectoriesIfNeeded();

            // UniverseSyncCache needs to run expiry here
            universeSyncCache.ExpireCache();

            GameEvents.onHideUI.Add(() =>
            {
                showGUI = false;
            });
            GameEvents.onShowUI.Add(() =>
            {
                showGUI = true;
            });

            HandleCommandLineArgs();
            DarkLog.Debug("DarkMultiPlayer " + Common.PROGRAM_VERSION + ", protocol " + Common.PROTOCOL_VERSION + " Initialized!");
        }

        internal static byte[] ReverseIfLittleEndian(byte[] input)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(input);
            }
            return input;
        }

        public void WriteByteArrayToStream(object inputData, Stream outputStream)
        {
            ByteArray data = (ByteArray)inputData;
            outputStream.Write(ReverseIfLittleEndian(BitConverter.GetBytes(data.Length)), 0, 4);
            outputStream.Write(data.data, 0, data.Length);
        }

        byte[] intReverser = new byte[4];
        public object ReadByteArrayFromStream(Stream inputStream)
        {
            inputStream.Read(intReverser, 0, 4);
            ReverseIfLittleEndian(intReverser);
            int length = BitConverter.ToInt32(intReverser, 0);
            ByteArray returnBytes = ByteRecycler.GetObject(length);
            inputStream.Read(returnBytes.data, 0, length);
            return returnBytes;
        }

        private void HandleCommandLineArgs()
        {
            bool nextLineIsAddress = false;
            bool valid = false;
            string address = null;
            int port = 6702;
            foreach (string commandLineArg in Environment.GetCommandLineArgs())
            {
                //Supporting IPv6 is FUN!
                if (nextLineIsAddress)
                {
                    valid = true;
                    nextLineIsAddress = false;
                    if (commandLineArg.Contains("dmp://"))
                    {
                        if (commandLineArg.Contains("[") && commandLineArg.Contains("]"))
                        {
                            //IPv6 literal
                            address = commandLineArg.Substring("dmp://[".Length);
                            address = address.Substring(0, address.LastIndexOf("]", StringComparison.Ordinal));
                            if (commandLineArg.Contains("]:"))
                            {
                                //With port
                                string portString = commandLineArg.Substring(commandLineArg.LastIndexOf("]:", StringComparison.Ordinal) + 1);
                                if (!Int32.TryParse(portString, out port))
                                {
                                    valid = false;
                                }
                            }
                        }
                        else
                        {
                            //IPv4 literal or hostname
                            if (commandLineArg.Substring("dmp://".Length).Contains(":"))
                            {
                                //With port
                                address = commandLineArg.Substring("dmp://".Length);
                                address = address.Substring(0, address.LastIndexOf(":", StringComparison.Ordinal));
                                string portString = commandLineArg.Substring(commandLineArg.LastIndexOf(":", StringComparison.Ordinal) + 1);
                                if (!Int32.TryParse(portString, out port))
                                {
                                    valid = false;
                                }
                            }
                            else
                            {
                                //Without port
                                address = commandLineArg.Substring("dmp://".Length);
                            }
                        }
                    }
                    else
                    {
                        valid = false;
                    }
                }

                if (commandLineArg == "-dmp")
                {
                    nextLineIsAddress = true;
                }
            }
            if (valid)
            {
                commandLineConnect = new ServerEntry();
                commandLineConnect.address = address;
                commandLineConnect.port = port;
                DarkLog.Debug("Connecting via command line to: " + address + ", port: " + port);
            }
        }

        public void Update()
        {
            profiler.Report("KSP", kspTime, kspMemory);
            DarkLog.Update();
            ByteRecycler.GarbageCollect(50, 100);
            Recycler<VesselUpdate>.GarbageCollect(50, 100);
            long profilerStartTime = profiler.GetCurrentTime;
            long profilerStartMemory = profiler.GetCurrentMemory;
            lastClockTicks = DateTime.UtcNow.Ticks;
            lastRealTimeSinceStartup = Time.realtimeSinceStartup;
            if (warnDuplicateInstall && HighLogic.LoadedScene == GameScenes.MAINMENU)
            {
                warnDuplicateInstall = false;
                string message = "Please remove the duplicate install of DarkMultiPlayer.";
                PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "InstallChecker", "Incorrect Install Detected", message, "OK", true, HighLogic.UISkin);
            }
            if (modDisabled)
            {
                return;
            }
            try
            {
                if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                {
                    if (!dmpSaveChecked)
                    {
                        dmpSaveChecked = true;
                        SetupBlankGameIfNeeded();
                    }
                }

                if (HighLogic.LoadedScene == GameScenes.SPACECENTER && PSystemSetup.Instance != null && Time.timeSinceLevelLoad > 1f)
                {
                    if (PSystemSetup.Instance.SpaceCenterFacilities.Length != facilitiesAdded)
                    {
                        facilitiesAdded = PSystemSetup.Instance.SpaceCenterFacilities.Length;
                        foreach (PSystemSetup.SpaceCenterFacility spaceCenterFacility in PSystemSetup.Instance.SpaceCenterFacilities)
                        {
                            foreach (PSystemSetup.SpaceCenterFacility.SpawnPoint spawnPoint in spaceCenterFacility.spawnPoints)
                            {
                                if (spawnPoint.latitude != 0 && spawnPoint.longitude != 0 && spawnPoint.altitude != 0)
                                {
                                    DarkLog.Debug("Adding facility spawn point: " + spaceCenterFacility.name + ":" + spawnPoint.name);
                                    SafetyBubble.RegisterLocation(spawnPoint.latitude, spawnPoint.longitude, spawnPoint.altitude, spaceCenterFacility.hostBody.name);
                                    DarkLog.Debug("LLA: [" + spawnPoint.latitude + ", " + spawnPoint.longitude + ", " + spawnPoint.altitude + "]");
                                }
                            }
                        }
                    }
                    if (PSystemSetup.Instance.LaunchSites.Count != modSitesAdded)
                    {
                        modSitesAdded = PSystemSetup.Instance.LaunchSites.Count;
                        foreach (LaunchSite launchSite in PSystemSetup.Instance.LaunchSites)
                        {
                            foreach (LaunchSite.SpawnPoint spawnPoint in launchSite.spawnPoints)
                            {
                                if (spawnPoint.latitude != 0 && spawnPoint.longitude != 0 && spawnPoint.altitude != 0)
                                {
                                    DarkLog.Debug("Adding mod spawn point: " + launchSite.name + ":" + spawnPoint.name);
                                    SafetyBubble.RegisterLocation(spawnPoint.latitude, spawnPoint.longitude, spawnPoint.altitude, launchSite.Body.name);
                                    DarkLog.Debug("LLA: [" + spawnPoint.latitude + ", " + spawnPoint.longitude + ", " + spawnPoint.altitude + "]");
                                }
                            }
                        }
                    }
                    if (PSystemSetup.Instance.StockLaunchSites.Length != stockSitesAdded)
                    {
                        stockSitesAdded = PSystemSetup.Instance.StockLaunchSites.Length;
                        foreach (LaunchSite launchSite in PSystemSetup.Instance.StockLaunchSites)
                        {
                            foreach (LaunchSite.SpawnPoint spawnPoint in launchSite.spawnPoints)
                            {
                                if (spawnPoint.latitude != 0 && spawnPoint.longitude != 0 && spawnPoint.altitude != 0)
                                {
                                    DarkLog.Debug("Adding stock spawn point: " + launchSite.name + ":" + spawnPoint.name);
                                    SafetyBubble.RegisterLocation(spawnPoint.latitude, spawnPoint.longitude, spawnPoint.altitude, launchSite.Body.name);
                                    DarkLog.Debug("LLA: [" + spawnPoint.latitude + ", " + spawnPoint.longitude + ", " + spawnPoint.altitude + "]");
                                }
                            }
                        }
                    }
                }



                //Handle GUI events

                if (!connectionWindow.renameEventHandled)
                {
                    dmpSettings.SaveSettings();
                    connectionWindow.renameEventHandled = true;
                }
                if (!connectionWindow.addEventHandled)
                {
                    dmpSettings.servers.Add(connectionWindow.addEntry);
                    connectionWindow.addEntry = null;
                    dmpSettings.SaveSettings();
                    connectionWindow.addingServer = false;
                    connectionWindow.addEventHandled = true;
                }
                if (!connectionWindow.editEventHandled)
                {
                    dmpSettings.servers[connectionWindow.selected].name = connectionWindow.editEntry.name;
                    dmpSettings.servers[connectionWindow.selected].address = connectionWindow.editEntry.address;
                    dmpSettings.servers[connectionWindow.selected].port = connectionWindow.editEntry.port;
                    connectionWindow.editEntry = null;
                    dmpSettings.SaveSettings();
                    connectionWindow.addingServer = false;
                    connectionWindow.editEventHandled = true;
                }
                if (!connectionWindow.removeEventHandled)
                {
                    dmpSettings.servers.RemoveAt(connectionWindow.selected);
                    connectionWindow.selected = -1;
                    dmpSettings.SaveSettings();
                    connectionWindow.removeEventHandled = true;
                }
                if (!connectionWindow.connectEventHandled)
                {
                    connectionWindow.connectEventHandled = true;
                    ConnectToServer(dmpSettings.servers[connectionWindow.selected].address, dmpSettings.servers[connectionWindow.selected].port);
                }
                if (commandLineConnect != null && HighLogic.LoadedScene == GameScenes.MAINMENU && Time.timeSinceLevelLoad > 1f)
                {
                    ConnectToServer(commandLineConnect.address, commandLineConnect.port);
                    commandLineConnect = null;
                }

                if (!connectionWindow.disconnectEventHandled)
                {
                    connectionWindow.disconnectEventHandled = true;
                    if (dmpGame != null)
                    {
                        if (dmpGame.networkWorker.state == ClientState.CONNECTING)
                        {
                            dmpGame.networkWorker.Disconnect("Cancelled connection to server");
                        }
                        else
                        {
                            dmpGame.networkWorker.SendDisconnect("Quit during initial sync");
                        }
                        dmpGame.Stop();
                        dmpGame = null;
                    }
                }

                connectionWindow.Update();
                serverListConnection.Update();
                serversWindow.Update();
                modWindow.Update();
                optionsWindow.Update();
                universeConverterWindow.Update();
                profiler.Update();
                dmpModInterface.Update();

                if (dmpGame != null)
                {
                    foreach (NamedAction updateAction in dmpGame.updateEvent)
                    {
#if !DEBUG
                        try
                        {
#endif
                        long profilerUpdateStartTime = profiler.GetCurrentTime;
                        long profilerUpdateStartMemory = profiler.GetCurrentMemory;
                        updateAction.action();
                        profiler.Report(updateAction.name, profilerUpdateStartTime, profilerUpdateStartMemory);
#if !DEBUG
                        }
                        catch (Exception e)
                        {
                            DarkLog.Debug("Threw in UpdateEvent, exception: " + e);
                            if (dmpGame.networkWorker.state != ClientState.RUNNING)
                            {
                                if (dmpGame.networkWorker.state != ClientState.DISCONNECTED)
                                {
                                    dmpGame.networkWorker.SendDisconnect("Unhandled error while syncing!");
                                }
                                else
                                {
                                    dmpGame.networkWorker.Disconnect("Unhandled error while syncing!");
                                }
                            }
                        }
#endif
                    }
                }
                //Force quit
                if (dmpGame != null && dmpGame.forceQuit)
                {
                    dmpGame.forceQuit = false;
                    dmpGame.Stop();
                    dmpGame = null;
                    StopGame();
                }


                if (displayDisconnectMessage)
                {
                    if (HighLogic.LoadedScene != GameScenes.MAINMENU)
                    {
                        if ((Client.realtimeSinceStartup - lastDisconnectMessageCheck) > 1f)
                        {
                            lastDisconnectMessageCheck = Client.realtimeSinceStartup;
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
                if (dmpGame != null && dmpGame.running)
                {
                    if (!dmpGame.playerStatusWindow.disconnectEventHandled)
                    {
                        dmpGame.playerStatusWindow.disconnectEventHandled = true;
                        dmpGame.forceQuit = true;
                        dmpGame.scenarioWorker.SendScenarioModules(true); // Send scenario modules before disconnecting
                        dmpGame.networkWorker.SendDisconnect("Quit");
                    }

                    if (dmpGame.screenshotWorker.uploadScreenshot)
                    {
                        dmpGame.screenshotWorker.uploadScreenshot = false;
                        StartCoroutine(UploadScreenshot());
                    }

                    if (HighLogic.CurrentGame.flagURL != dmpSettings.selectedFlag)
                    {
                        DarkLog.Debug("Saving selected flag");
                        dmpSettings.selectedFlag = HighLogic.CurrentGame.flagURL;
                        dmpSettings.SaveSettings();
                        dmpGame.flagSyncer.flagChangeEvent = true;
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
                    if (!dmpGame.serverAllowCheats)
                    {
                        CheatOptions.InfinitePropellant = false;
                        CheatOptions.NoCrashDamage = false;
                        CheatOptions.IgnoreAgencyMindsetOnContracts = false;
                        CheatOptions.IgnoreMaxTemperature = false;
                        CheatOptions.InfiniteElectricity = false;
                        CheatOptions.NoCrashDamage = false;
                        CheatOptions.UnbreakableJoints = false;

                        foreach (KeyValuePair<CelestialBody, double> gravityEntry in bodiesGees)
                        {
                            gravityEntry.Key.GeeASL = gravityEntry.Value;
                        }
                    }

                    if (HighLogic.LoadedScene == GameScenes.FLIGHT && FlightGlobals.ready)
                    {
                        HighLogic.CurrentGame.Parameters.Flight.CanLeaveToSpaceCenter = !dmpGame.vesselWorker.isSpectating && dmpSettings.revertEnabled || (PauseMenu.canSaveAndExit == ClearToSaveStatus.CLEAR);
                    }
                    else
                    {
                        HighLogic.CurrentGame.Parameters.Flight.CanLeaveToSpaceCenter = true;
                    }

                    if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                    {
                        dmpGame.networkWorker.SendDisconnect("Quit to main menu");
                        dmpGame.Stop();
                        dmpGame = null;
                    }
                }

                if (dmpGame != null && dmpGame.startGame)
                {
                    dmpGame.startGame = false;
                    StartGame();
                }
            }
            catch (Exception e)
            {
                if (dmpGame != null)
                {
                    DarkLog.Debug("Threw in Update, state " + dmpGame.networkWorker.state.ToString() + ", exception: " + e);
                    if (dmpGame.networkWorker.state != ClientState.RUNNING)
                    {
                        if (dmpGame.networkWorker.state != ClientState.DISCONNECTED)
                        {
                            dmpGame.networkWorker.SendDisconnect("Unhandled error while syncing!");
                        }
                        else
                        {
                            dmpGame.networkWorker.Disconnect("Unhandled error while syncing!");
                        }
                    }
                }
                else
                {
                    DarkLog.Debug("Threw in Update, state NO_NETWORKWORKER, exception: " + e);
                }
            }
            DarkLog.Update();
            profiler.Report("Update", profilerStartTime, profilerStartMemory);
            kspTime = profiler.GetCurrentTime;
            kspMemory = profiler.GetCurrentMemory;
        }

        public void ConnectToServer(string address, int port)
        {
            dmpGame = new DMPGame(dmpSettings, universeSyncCache, modWorker, connectionWindow, dmpModInterface, toolbarSupport, optionsWindow, profiler);
            dmpGame.networkWorker.ConnectToServer(address, port);
        }


        public IEnumerator<WaitForEndOfFrame> UploadScreenshot()
        {
            yield return new WaitForEndOfFrame();
            if (dmpGame != null)
            {
                dmpGame.screenshotWorker.SendScreenshot();
                dmpGame.screenshotWorker.screenshotTaken = true;
            }
        }

        public void TimingManagerFixedUpdate()
        {
            profiler.Report("KSP", kspTime, kspMemory);
            long profilerStartTime = profiler.GetCurrentTime;
            long profilerStartMemory = profiler.GetCurrentMemory;
            DarkLog.Update();
            if (modDisabled)
            {
                return;
            }
            dmpModInterface.FixedUpdate();

            if (dmpGame != null)
            {
                foreach (NamedAction fixedUpdateAction in dmpGame.fixedUpdateEvent)
                {
#if !DEBUG
                    try
                    {
#endif
                    long profilerFixedUpdateStartTime = profiler.GetCurrentTime;
                    long profilerFixedUpdateStartMemory = profiler.GetCurrentMemory;
                    fixedUpdateAction.action();
                    profiler.Report(fixedUpdateAction.name, profilerFixedUpdateStartTime, profilerFixedUpdateStartMemory);
#if !DEBUG
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("Threw in FixedUpdate event, exception: " + e);
                        if (dmpGame.networkWorker != null)
                        {
                            if (dmpGame.networkWorker.state != ClientState.RUNNING)
                            {
                                if (dmpGame.networkWorker.state != ClientState.DISCONNECTED)
                                {
                                    dmpGame.networkWorker.SendDisconnect("Unhandled error while syncing!");
                                }
                                else
                                {
                                    dmpGame.networkWorker.Disconnect("Unhandled error while syncing!");
                                }
                            }
                        }
                    }
#endif
                }

            }
            DarkLog.Update();
            profiler.Report("FixedUpdate", profilerStartTime, profilerStartMemory);
            kspTime = profiler.GetCurrentTime;
            kspMemory = profiler.GetCurrentMemory;
        }

        public void OnGUI()
        {
            profiler.Report("KSP", kspTime, kspMemory);
            long profilerStartTime = profiler.GetCurrentTime;
            long profilerStartMemory = profiler.GetCurrentMemory;
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
            if (showGUI)
            {
                if (connectionWindow != null)
                {
                    connectionWindow.Draw();
                }
                if (modWindow != null)
                {
                    modWindow.Draw();
                }
                if (optionsWindow != null)
                {
                    optionsWindow.Draw();
                }
                if (serversWindow != null)
                {
                    serversWindow.Draw();
                }
                if (universeConverterWindow != null)
                {
                    universeConverterWindow.Draw();
                }
                if (dmpGame != null)
                {
                    foreach (NamedAction drawAction in dmpGame.drawEvent)
                    {
#if !DEBUG
                        try
                        {
#endif
                        // Don't hide the connectionWindow if we disabled DMP GUI
                        if (toolbarShowGUI || (!toolbarShowGUI && drawAction.name == "DarkMultiPlayer.ConnectionWindow.Draw"))
                        {
                            drawAction.action();
                        }
#if !DEBUG
                        }
                        catch (Exception e)
                        {
                            DarkLog.Debug("Threw in OnGUI event, exception: " + e);
                        }
#endif
                    }
                }
            }
            profiler.Report("Draw", profilerStartTime, profilerStartMemory);
            kspTime = profiler.GetCurrentTime;
            kspMemory = profiler.GetCurrentMemory;
        }

        private void StartGame()
        {
            //Create new game object for our DMP session.
            HighLogic.CurrentGame = CreateBlankGame();

            //Set the game mode
            HighLogic.CurrentGame.Mode = ConvertGameMode(dmpGame.gameMode);

            //Set difficulty
            HighLogic.CurrentGame.Parameters = dmpGame.serverParameters;

            //Set universe time
            HighLogic.CurrentGame.flightState.universalTime = dmpGame.timeSyncer.GetUniverseTime();
            //Load DMP stuff
            dmpGame.vesselWorker.LoadKerbalsIntoGame();
            dmpGame.vesselWorker.LoadVesselsIntoGame();

            //Load the scenarios from the server
            dmpGame.scenarioWorker.LoadScenarioDataIntoGame();

            //Load the missing scenarios as well (Eg, Contracts and stuff for career mode
            dmpGame.scenarioWorker.LoadMissingScenarioDataIntoGame();

            //This only makes KSP complain
            HighLogic.CurrentGame.CrewRoster.ValidateAssignments(HighLogic.CurrentGame);
            DarkLog.Debug("Starting " + dmpGame.gameMode + " game...");

            //.Start() seems to stupidly .Load() somewhere - Let's overwrite it so it loads correctly.
            GamePersistence.SaveGame(HighLogic.CurrentGame, "persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            HighLogic.CurrentGame.Start();
            dmpGame.chatWorker.display = true;
            DarkLog.Debug("Started!");
        }

        private void StopGame()
        {
            HighLogic.SaveFolder = "DarkMultiPlayer";
            if (HighLogic.LoadedScene != GameScenes.MAINMENU)
            {
                HighLogic.LoadScene(GameScenes.MAINMENU);
            }
            //HighLogic.CurrentGame = null; This is no bueno
            bodiesGees.Clear();
        }

        public static Game.Modes ConvertGameMode(GameMode inputMode)
        {
            if (inputMode == GameMode.SANDBOX)
            {
                return Game.Modes.SANDBOX;
            }
            if (inputMode == GameMode.SCIENCE)
            {
                return Game.Modes.SCIENCE_SANDBOX;
            }
            if (inputMode == GameMode.CAREER)
            {
                return Game.Modes.CAREER;
            }
            return Game.Modes.SANDBOX;
        }

        private bool WantsToQuit()
        {
            if (dmpGame != null && dmpGame.networkWorker.state == ClientState.RUNNING)
            {
                dmpGame.scenarioWorker.SendScenarioModules(true);
                HighLogic.LoadScene(GameScenes.MAINMENU);
                return false;
            }
            return true;
        }

        private void SetupDirectoriesIfNeeded()
        {
            string darkMultiPlayerSavesDirectory = Path.Combine(Path.Combine(kspRootPath, "saves"), "DarkMultiPlayer");
            CreateIfNeeded(darkMultiPlayerSavesDirectory);
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, "Ships"));
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, Path.Combine("Ships", "VAB")));
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, Path.Combine("Ships", "SPH")));
            CreateIfNeeded(Path.Combine(darkMultiPlayerSavesDirectory, "Subassemblies"));
            string darkMultiPlayerCacheDirectory = Path.Combine(Path.Combine(gameDataDir, "DarkMultiPlayer"), "Cache");
            CreateIfNeeded(darkMultiPlayerCacheDirectory);
            string darkMultiPlayerIncomingCacheDirectory = Path.Combine(Path.Combine(Path.Combine(gameDataDir, "DarkMultiPlayer"), "Cache"), "Incoming");
            CreateIfNeeded(darkMultiPlayerIncomingCacheDirectory);
            string darkMultiPlayerFlagsDirectory = Path.Combine(Path.Combine(gameDataDir, "DarkMultiPlayer"), "Flags");
            CreateIfNeeded(darkMultiPlayerFlagsDirectory);
        }

        private void SetupBlankGameIfNeeded()
        {
            string persistentFile = Path.Combine(Path.Combine(Path.Combine(kspRootPath, "saves"), "DarkMultiPlayer"), "persistent.sfs");
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
            if (returnGame.flightState.mapViewFilterState == 0)
            {
                returnGame.flightState.mapViewFilterState = -1026;
            }

            //DMP stuff
            returnGame.startScene = GameScenes.SPACECENTER;
            returnGame.flagURL = dmpSettings.selectedFlag;
            returnGame.Title = "DarkMultiPlayer";
            // Disable everything if we're in main menu
            // I'm not sure why we need to create a blank game when we're not connected
            if (HighLogic.LoadedScene == GameScenes.MAINMENU)
            {
                returnGame.Parameters.Flight.CanQuickLoad = false;
                returnGame.Parameters.Flight.CanRestart = false;
                returnGame.Parameters.Flight.CanLeaveToEditor = false;
            }
            else
            {
                if (dmpGame.warpWorker.warpMode == WarpMode.SUBSPACE)
                {
                    returnGame.Parameters.Flight.CanQuickLoad = true;
                    returnGame.Parameters.Flight.CanRestart = true;
                    returnGame.Parameters.Flight.CanLeaveToEditor = true;
                }
                else
                {
                    returnGame.Parameters.Flight.CanQuickLoad = false;
                    returnGame.Parameters.Flight.CanRestart = false;
                    returnGame.Parameters.Flight.CanLeaveToEditor = false;
                }
            }
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

