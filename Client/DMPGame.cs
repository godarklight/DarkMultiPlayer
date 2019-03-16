using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class DMPGame
    {
        public bool running;
        public bool startGame;
        public bool forceQuit;
        public GameMode gameMode;
        public bool serverAllowCheats = true;
        // Server setting
        public GameDifficulty serverDifficulty;
        public GameParameters serverParameters;
        public List<Action> updateEvent = new List<Action>();
        public List<Action> fixedUpdateEvent = new List<Action>();
        public List<Action> drawEvent = new List<Action>();
        private List<Action> stopEvent = new List<Action>();
        public readonly Settings dmpSettings;
        public readonly UniverseSyncCache universeSyncCache;
        public readonly ModWorker modWorker;
        public readonly ConnectionWindow connectionWindow;
        public readonly PlayerStatusWindow playerStatusWindow;
        public readonly ConfigNodeSerializer configNodeSerializer;
        public readonly ScenarioWorker scenarioWorker;
        public readonly NetworkWorker networkWorker;
        public readonly ScreenshotWorker screenshotWorker;
        public readonly FlagSyncer flagSyncer;
        public readonly VesselWorker vesselWorker;
        public readonly TimeSyncer timeSyncer;
        public readonly AdminSystem adminSystem;
        public readonly ChatWorker chatWorker;
        public readonly WarpWorker warpWorker;
        public readonly PlayerStatusWorker playerStatusWorker;
        public readonly DynamicTickWorker dynamicTickWorker;
        public readonly DebugWindow debugWindow;
        public readonly CraftLibraryWorker craftLibraryWorker;
        public readonly PlayerColorWorker playerColorWorker;
        public readonly LockSystem lockSystem;
        public readonly HackyInAtmoLoader hackyInAtmoLoader;
        public readonly PartKiller partKiller;
        public readonly KerbalReassigner kerbalReassigner;
        public readonly AsteroidWorker asteroidWorker;
        public readonly VesselRecorder vesselRecorder;
        public readonly PosistionStatistics posistionStatistics;
        public readonly VesselInterFrameUpdater vesselPackedUpdater;
        private DMPModInterface dmpModInterface;

        public DMPGame(Settings dmpSettings, UniverseSyncCache universeSyncCache, ModWorker modWorker, ConnectionWindow connectionWindow, DMPModInterface dmpModInterface, ToolbarSupport toolbarSupport, OptionsWindow optionsWindow)
        {
            this.dmpSettings = dmpSettings;
            this.universeSyncCache = universeSyncCache;
            this.modWorker = modWorker;
            this.connectionWindow = connectionWindow;
            this.dmpModInterface = dmpModInterface;
            this.configNodeSerializer = new ConfigNodeSerializer();
            this.posistionStatistics = new PosistionStatistics();
            this.networkWorker = new NetworkWorker(this, dmpSettings, connectionWindow, modWorker, configNodeSerializer);
            this.adminSystem = new AdminSystem(dmpSettings);
            this.flagSyncer = new FlagSyncer(this, dmpSettings, networkWorker);
            this.lockSystem = new LockSystem(dmpSettings, networkWorker);
            this.partKiller = new PartKiller(lockSystem);
            this.dynamicTickWorker = new DynamicTickWorker(this, networkWorker);
            this.kerbalReassigner = new KerbalReassigner();
            this.vesselPackedUpdater = new VesselInterFrameUpdater(lockSystem, posistionStatistics, dmpSettings);
            this.vesselWorker = new VesselWorker(this, dmpSettings, modWorker, lockSystem, networkWorker, configNodeSerializer, dynamicTickWorker, kerbalReassigner, partKiller, posistionStatistics, vesselPackedUpdater);
            this.scenarioWorker = new ScenarioWorker(this, vesselWorker, configNodeSerializer, networkWorker);
            this.playerStatusWorker = new PlayerStatusWorker(this, dmpSettings, vesselWorker, lockSystem, networkWorker);
            this.timeSyncer = new TimeSyncer(this, networkWorker, vesselWorker);
            this.warpWorker = new WarpWorker(this, dmpSettings, timeSyncer, networkWorker, playerStatusWorker);
            this.chatWorker = new ChatWorker(this, dmpSettings, networkWorker, adminSystem, playerStatusWorker);
            this.screenshotWorker = new ScreenshotWorker(this, dmpSettings, chatWorker, networkWorker, playerStatusWorker);
            this.vesselRecorder = new VesselRecorder(this, warpWorker, vesselWorker, networkWorker, dmpSettings);
            this.debugWindow = new DebugWindow(this, dmpSettings, timeSyncer, networkWorker, vesselWorker, dynamicTickWorker, warpWorker, vesselRecorder, posistionStatistics);
            this.craftLibraryWorker = new CraftLibraryWorker(this, dmpSettings, networkWorker);
            this.hackyInAtmoLoader = new HackyInAtmoLoader(this, lockSystem, vesselWorker);
            this.asteroidWorker = new AsteroidWorker(this, lockSystem, networkWorker, vesselWorker);
            this.playerColorWorker = new PlayerColorWorker(dmpSettings, lockSystem, networkWorker);
            this.playerStatusWindow = new PlayerStatusWindow(this, dmpSettings, warpWorker, chatWorker, craftLibraryWorker, debugWindow, screenshotWorker, timeSyncer, playerStatusWorker, optionsWindow, playerColorWorker);
            this.playerColorWorker.SetDependencies(playerStatusWindow);
            this.vesselWorker.SetDependencies(hackyInAtmoLoader, timeSyncer, asteroidWorker, chatWorker, playerStatusWorker);
            this.networkWorker.SetDependencies(timeSyncer, warpWorker, chatWorker, playerColorWorker, flagSyncer, partKiller, kerbalReassigner, asteroidWorker, vesselWorker, hackyInAtmoLoader, playerStatusWorker, scenarioWorker, dynamicTickWorker, craftLibraryWorker, screenshotWorker, toolbarSupport, adminSystem, lockSystem, dmpModInterface, universeSyncCache, vesselRecorder);
            //this.vesselPackedUpdater.SetVesselRecoder(this.vesselRecorder);
            optionsWindow.SetDependencies(this, networkWorker, playerColorWorker);
            this.dmpModInterface.DMPRun(networkWorker);
            this.stopEvent.Add(this.chatWorker.Stop);
            this.stopEvent.Add(this.craftLibraryWorker.Stop);
            this.stopEvent.Add(this.debugWindow.Stop);
            this.stopEvent.Add(this.dynamicTickWorker.Stop);
            this.stopEvent.Add(this.flagSyncer.Stop);
            this.stopEvent.Add(this.hackyInAtmoLoader.Stop);
            this.stopEvent.Add(this.kerbalReassigner.Stop);
            this.stopEvent.Add(this.playerColorWorker.Stop);
            this.stopEvent.Add(this.playerStatusWindow.Stop);
            this.stopEvent.Add(this.playerStatusWorker.Stop);
            this.stopEvent.Add(this.partKiller.Stop);
            this.stopEvent.Add(this.scenarioWorker.Stop);
            this.stopEvent.Add(this.screenshotWorker.Stop);
            this.stopEvent.Add(this.timeSyncer.Stop);
            this.stopEvent.Add(toolbarSupport.Stop);
            this.stopEvent.Add(optionsWindow.Stop);
            this.stopEvent.Add(this.vesselWorker.Stop);
            this.stopEvent.Add(this.warpWorker.Stop);
            this.stopEvent.Add(this.asteroidWorker.Stop);
            this.stopEvent.Add(this.vesselRecorder.Stop);
        }

        public void Stop()
        {
            dmpModInterface.DMPStop();
            foreach (Action stopAction in stopEvent)
            {
                try
                {
                    stopAction();
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Threw in DMPGame.Stop, exception: " + e);
                }
            }
        }
    }
}
