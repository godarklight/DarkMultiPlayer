using System;
using System.Collections.Generic;
using UnityEngine;
using MessageStream2;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    //Damn you americans - You're making me spell 'colour' wrong!
    public class PlayerColorWorker
    {
        //As this worker is entirely event based, we need to register and unregister hooks in the workerEnabled accessor.
        private bool privateWorkerEnabled;
        //Services
        private Settings dmpSettings;
        private LockSystem lockSystem;
        private PlayerStatusWindow playerStatusWindow;
        private NetworkWorker networkWorker;

        public PlayerColorWorker(Settings dmpSettings, LockSystem lockSystem, NetworkWorker networkWorker)
        {
            this.dmpSettings = dmpSettings;
            this.lockSystem = lockSystem;
            this.networkWorker = networkWorker;
        }

        public void SetDependencies(PlayerStatusWindow playerStatusWindow)
        {
            this.playerStatusWindow = playerStatusWindow;
        }

        public bool workerEnabled
        {
            get
            {
                return privateWorkerEnabled;
            }
            set
            {
                if (!privateWorkerEnabled && value)
                {
                    GameEvents.onVesselCreate.Add(this.SetVesselColor);
                    lockSystem.RegisterAcquireHook(this.OnLockAcquire);
                    lockSystem.RegisterReleaseHook(this.OnLockRelease);
                }
                if (privateWorkerEnabled && !value)
                {
                    GameEvents.onVesselCreate.Remove(this.SetVesselColor);
                    lockSystem.UnregisterAcquireHook(this.OnLockAcquire);
                    lockSystem.UnregisterReleaseHook(this.OnLockRelease);
                }
                privateWorkerEnabled = value;
            }
        }

        private Dictionary<string, Color> playerColors = new Dictionary<string, Color>();
        private object playerColorLock = new object();
        //Can't declare const - But no touchy.
        public readonly Color DEFAULT_COLOR = Color.grey;

        private void SetVesselColor(Vessel colorVessel)
        {
            if (workerEnabled)
            {
                if (lockSystem.LockExists("control-" + colorVessel.id.ToString()) && !lockSystem.LockIsOurs("control-" + colorVessel.id.ToString()))
                {
                    string vesselOwner = lockSystem.LockOwner("control-" + colorVessel.id.ToString());
                    DarkLog.Debug("Vessel " + colorVessel.id.ToString() + " owner is " + vesselOwner);
                    colorVessel.orbitDriver.orbitColor = GetPlayerColor(vesselOwner);
                }
                else
                {
                    colorVessel.orbitDriver.orbitColor = DEFAULT_COLOR;
                }
            }
        }

        private void OnLockAcquire(string playerName, string lockName, bool result)
        {
            if (workerEnabled)
            {
                UpdateVesselColorsFromLockName(lockName);
            }
        }

        private void OnLockRelease(string playerName, string lockName)
        {
            if (workerEnabled)
            {
                UpdateVesselColorsFromLockName(lockName);
            }
        }

        private void UpdateVesselColorsFromLockName(string lockName)
        {
            if (lockName.StartsWith("control-"))
            {
                string vesselID = lockName.Substring(8);
                foreach (Vessel findVessel in FlightGlobals.fetch.vessels)
                {
                    if (findVessel.id.ToString() == vesselID)
                    {
                        SetVesselColor(findVessel);
                    }
                }
            }
        }

        private void UpdateAllVesselColors()
        {
            foreach (Vessel updateVessel in FlightGlobals.fetch.vessels)
            {
                SetVesselColor(updateVessel);
            }
        }

        public Color GetPlayerColor(string playerName)
        {
            lock (playerColorLock)
            {
                if (playerName == dmpSettings.playerName)
                {
                    return dmpSettings.playerColor;
                }
                if (playerColors.ContainsKey(playerName))
                {
                    return playerColors[playerName];
                }
                return DEFAULT_COLOR;
            }
        }

        public void HandlePlayerColorMessage(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                PlayerColorMessageType messageType = (PlayerColorMessageType)mr.Read<int>();
                switch (messageType)
                {
                    case PlayerColorMessageType.LIST:
                        {
                            int numOfEntries = mr.Read<int>();
                            lock (playerColorLock)
                            {
                                playerColors = new Dictionary<string, Color>();
                                for (int i = 0; i < numOfEntries; i++)
                                {

                                    string playerName = mr.Read<string>();
                                    Color playerColor = ConvertFloatArrayToColor(mr.Read<float[]>());
                                    playerColors.Add(playerName, playerColor);
                                    playerStatusWindow.colorEventHandled = false;
                                }
                            }
                        }
                        break;
                    case PlayerColorMessageType.SET:
                        {
                            lock (playerColorLock)
                            {
                                string playerName = mr.Read<string>();
                                Color playerColor = ConvertFloatArrayToColor(mr.Read<float[]>());
                                DarkLog.Debug("Color message, name: " + playerName + " , color: " + playerColor.ToString());
                                playerColors[playerName] = playerColor;
                                UpdateAllVesselColors();
                                playerStatusWindow.colorEventHandled = false;
                            }
                        }
                        break;
                }
            }
        }

        public void SendPlayerColorToServer()
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)PlayerColorMessageType.SET);
                mw.Write<string>(dmpSettings.playerName);
                mw.Write<float[]>(ConvertColorToFloatArray(dmpSettings.playerColor));
                networkWorker.SendPlayerColorMessage(mw.GetMessageBytes());
            }
        }
        //Helpers
        public static float[] ConvertColorToFloatArray(Color convertColour)
        {
            float[] returnArray = new float[3];
            returnArray[0] = convertColour.r;
            returnArray[1] = convertColour.g;
            returnArray[2] = convertColour.b;
            return returnArray;
        }

        public static Color ConvertFloatArrayToColor(float[] convertArray)
        {
            return new Color(convertArray[0], convertArray[1], convertArray[2]);
        }
        //Adapted from KMP
        public static Color GenerateRandomColor()
        {
            System.Random rand = new System.Random();
            int seed = rand.Next();
            Color returnColor = Color.white;
            switch (seed % 17)
            {
                case 0:
                    return Color.red;
                case 1:
                    return new Color(1, 0, 0.5f, 1); //Rosy pink
                case 2:
                    return new Color(0.6f, 0, 0.5f, 1); //OU Crimson
                case 3:
                    return new Color(1, 0.5f, 0, 1); //Orange
                case 4:
                    return Color.yellow;
                case 5:
                    return new Color(1, 0.84f, 0, 1); //Gold
                case 6:
                    return Color.green;
                case 7:
                    return new Color(0, 0.651f, 0.576f, 1); //Persian Green
                case 8:
                    return new Color(0, 0.651f, 0.576f, 1); //Persian Green
                case 9:
                    return new Color(0, 0.659f, 0.420f, 1); //Jade
                case 10:
                    return new Color(0.043f, 0.855f, 0.318f, 1); //Malachite
                case 11:
                    return Color.cyan;
                case 12:
                    return new Color(0.537f, 0.812f, 0.883f, 1); //Baby blue;
                case 13:
                    return new Color(0, 0.529f, 0.741f, 1); //NCS blue
                case 14:
                    return new Color(0.255f, 0.412f, 0.882f, 1); //Royal Blue
                case 15:
                    return new Color(0.5f, 0, 1, 1); //Violet
                default:
                    return Color.magenta;
            }
        }

        public void Stop()
        {
            workerEnabled = false;
        }
    }
}

