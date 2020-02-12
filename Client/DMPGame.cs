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
        public List<NamedAction> updateEvent = new List<NamedAction>();
        public List<NamedAction> fixedUpdateEvent = new List<NamedAction>();
        public List<NamedAction> drawEvent = new List<NamedAction>();
        private List<Action> stopEvent = new List<Action>();
        public readonly Settings dmpSettings;
        public readonly ModpackWorker modpackWorker;
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
        public readonly Groups groups;
        public readonly GroupsWindow groupsWindow;
        public readonly Permissions permissions;
        public readonly PermissionsWindow permissionsWindow;
        public readonly AdminSystem adminSystem;
        public readonly ChatWorker chatWorker;
        public readonly WarpWorker warpWorker;
        public readonly PlayerStatusWorker playerStatusWorker;
        public readonly DynamicTickWorker dynamicTickWorker;
        public readonly DebugWindow debugWindow;
        public readonly CraftLibraryWorker craftLibraryWorker;
        public readonly PlayerColorWorker playerColorWorker;
        public readonly LockSystem lockSystem;
        public readonly PartKiller partKiller;
        public readonly KerbalReassigner kerbalReassigner;
        public readonly AsteroidWorker asteroidWorker;
        public readonly VesselRecorder vesselRecorder;
        public readonly PosistionStatistics posistionStatistics;
        public readonly Profiler profiler;
        public readonly VesselRangeBumper vesselRangeBumper;
        private DMPModInterface dmpModInterface;

        public DMPGame(Settings dmpSettings, UniverseSyncCache universeSyncCache, ModWorker modWorker, ConnectionWindow connectionWindow, DMPModInterface dmpModInterface, ToolbarSupport toolbarSupport, OptionsWindow optionsWindow, Profiler profiler)
        {
            this.dmpSettings = dmpSettings;
            this.universeSyncCache = universeSyncCache;
            this.modWorker = modWorker;
            this.connectionWindow = connectionWindow;
            this.dmpModInterface = dmpModInterface;
            this.profiler = profiler;
            this.configNodeSerializer = new ConfigNodeSerializer();
            this.posistionStatistics = new PosistionStatistics();
            this.vesselRangeBumper = new VesselRangeBumper(this);
            this.networkWorker = new NetworkWorker(this, dmpSettings, connectionWindow, modWorker, configNodeSerializer, profiler, vesselRangeBumper);
            this.adminSystem = new AdminSystem(dmpSettings);
            this.flagSyncer = new FlagSyncer(this, dmpSettings, networkWorker);
            this.lockSystem = new LockSystem(dmpSettings, networkWorker);
            this.groups = new Groups(this, networkWorker, dmpSettings);
            this.groupsWindow = new GroupsWindow(this, dmpSettings, groups);
            this.permissions = new Permissions(this, networkWorker, dmpSettings, groups);
            this.permissionsWindow = new PermissionsWindow(this, dmpSettings, groups, permissions, lockSystem);
            this.partKiller = new PartKiller(lockSystem);
            this.dynamicTickWorker = new DynamicTickWorker(this, networkWorker);
            this.kerbalReassigner = new KerbalReassigner();
            this.playerColorWorker = new PlayerColorWorker(this, dmpSettings, lockSystem, networkWorker);
            this.vesselWorker = new VesselWorker(this, dmpSettings, modWorker, lockSystem, networkWorker, configNodeSerializer, dynamicTickWorker, kerbalReassigner, partKiller, posistionStatistics, permissions, profiler, vesselRangeBumper, playerColorWorker);
            this.scenarioWorker = new ScenarioWorker(this, vesselWorker, configNodeSerializer, networkWorker);
            this.playerStatusWorker = new PlayerStatusWorker(this, dmpSettings, vesselWorker, lockSystem, networkWorker, permissions);
            this.timeSyncer = new TimeSyncer(this, networkWorker, vesselWorker);
            this.warpWorker = new WarpWorker(this, dmpSettings, timeSyncer, networkWorker, playerStatusWorker);
            this.chatWorker = new ChatWorker(this, dmpSettings, networkWorker, adminSystem, playerStatusWorker);
            this.modpackWorker = new ModpackWorker(this, dmpSettings, modWorker, networkWorker, chatWorker, adminSystem);
            this.screenshotWorker = new ScreenshotWorker(this, dmpSettings, chatWorker, networkWorker, playerStatusWorker);
            this.vesselRecorder = new VesselRecorder(this, warpWorker, vesselWorker, networkWorker, dmpSettings);
            this.debugWindow = new DebugWindow(this, dmpSettings, timeSyncer, networkWorker, vesselWorker, dynamicTickWorker, warpWorker, vesselRecorder, posistionStatistics, optionsWindow, profiler);
            this.craftLibraryWorker = new CraftLibraryWorker(this, dmpSettings, networkWorker);
            this.asteroidWorker = new AsteroidWorker(this, lockSystem, networkWorker, vesselWorker);
            this.playerStatusWindow = new PlayerStatusWindow(this, dmpSettings, warpWorker, chatWorker, craftLibraryWorker, screenshotWorker, timeSyncer, playerStatusWorker, optionsWindow, playerColorWorker, groupsWindow, permissionsWindow);
            this.playerColorWorker.SetDependencies(playerStatusWindow);
            this.vesselWorker.SetDependencies(timeSyncer, warpWorker, asteroidWorker, chatWorker, playerStatusWorker);
            this.networkWorker.SetDependencies(timeSyncer, warpWorker, chatWorker, playerColorWorker, flagSyncer, partKiller, kerbalReassigner, asteroidWorker, vesselWorker, playerStatusWorker, scenarioWorker, dynamicTickWorker, craftLibraryWorker, screenshotWorker, toolbarSupport, adminSystem, lockSystem, dmpModInterface, universeSyncCache, vesselRecorder, groups, permissions, modpackWorker);
            //this.vesselPackedUpdater.SetVesselRecoder(this.vesselRecorder);
            optionsWindow.SetDependencies(this, networkWorker, playerColorWorker);
            groupsWindow.SetDependencies(playerStatusWorker);
            permissionsWindow.SetDependencies(playerStatusWorker);
            this.dmpModInterface.DMPRun(networkWorker);
            this.stopEvent.Add(this.chatWorker.Stop);
            this.stopEvent.Add(this.craftLibraryWorker.Stop);
            this.stopEvent.Add(this.debugWindow.Stop);
            this.stopEvent.Add(this.dynamicTickWorker.Stop);
            this.stopEvent.Add(this.flagSyncer.Stop);
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
            this.stopEvent.Add(this.groups.Stop);
            this.stopEvent.Add(this.permissions.Stop);
            this.stopEvent.Add(this.groupsWindow.Stop);
            this.stopEvent.Add(this.permissionsWindow.Stop);
            this.stopEvent.Add(this.modpackWorker.Stop);
            this.stopEvent.Add(this.vesselRangeBumper.Stop);
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
