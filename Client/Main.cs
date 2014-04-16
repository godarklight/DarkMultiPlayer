using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class Client : MonoBehaviour
    {
        //Global state vars
        public string playerName;
        public bool forceQuit;

        //Game running is directly set from networkWorker after a successful connection
        public bool gameRunning;

        //Singletons
        public TimeSyncer timeSyncer;
        public VesselWorker vesselWorker;
        public NetworkWorker networkWorker;
        public Settings settings;
        private ConnectionWindow connectionWindow;


        public void Awake()
        {
            GameObject.DontDestroyOnLoad(this);
            timeSyncer = new TimeSyncer(this);
            vesselWorker = new VesselWorker(this);
            networkWorker = new NetworkWorker(this);
            settings = new Settings();
            connectionWindow = new ConnectionWindow();
            DarkLog.Debug("DarkMultiPlayer Initialized!");

            //Temporary testing stuff
            settings.playerGuid = new Guid();
            System.Random r = new System.Random();
            double randomNumber = r.NextDouble();
            settings.playerName = "godarklight-" + randomNumber;
        }

        public void Start()
        {
        }

        public void Update()
        {
            DarkLog.Update();
            if (!connectionWindow.eventHandled)
            {
                connectionWindow.eventHandled = true;
                networkWorker.ConnectToServer("127.0.0.1", "6702");
            }
            networkWorker.Update();
            //Force quit
            if (forceQuit)
            {
                forceQuit = false;
                gameRunning = false;
                timeSyncer.Reset();
                vesselWorker.Reset();
                networkWorker.SendDisconnect("Force quit to main menu");
                StopGame();
            }
            //Normal quit
            if (gameRunning == true && HighLogic.LoadedScene == GameScenes.MAINMENU)
            {
                gameRunning = false;
                timeSyncer.Reset();
                vesselWorker.Reset();
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
        }

        public void OnDestroy()
        {
        }

        //WARNING: Called from NetworkWorker.
        public void StartGame()
        {
            HighLogic.CurrentGame = new Game();
            HighLogic.CurrentGame.flightState = new FlightState();
            HighLogic.CurrentGame.startScene = GameScenes.SPACECENTER;
            HighLogic.CurrentGame.Title = "DarkMultiPlayer";
            HighLogic.SaveFolder = "DarkMultiPlayer";
            DarkLog.Debug("Universe time is " + timeSyncer.GetCurrentTime());
            HighLogic.CurrentGame.flightState.universalTime = timeSyncer.GetCurrentTime();
            vesselWorker.LoadKerbalsIntoGame();
            HighLogic.CurrentGame.Start();
            vesselWorker.LoadVesselsIntoGame();
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
        }

        public void StopGame()
        {
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            if (HighLogic.LoadedScene != GameScenes.MAINMENU)
            {
                HighLogic.LoadScene(GameScenes.MAINMENU);
            }
        }
    }
}

