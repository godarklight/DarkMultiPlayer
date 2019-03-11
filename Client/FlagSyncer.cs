using System;
using System.Collections.Generic;
using System.IO;
using MessageStream2;
using DarkMultiPlayerCommon;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class FlagSyncer
    {
        //Public
        public bool workerEnabled;
        public bool flagChangeEvent;
        public bool syncComplete;
        //Private
        private string flagPath;
        private Dictionary<string, FlagInfo> serverFlags = new Dictionary<string, FlagInfo>();
        private Queue<FlagRespondMessage> newFlags = new Queue<FlagRespondMessage>();
        //Services
        private DMPGame dmpGame;
        private Settings dmpSettings;
        private NetworkWorker networkWorker;

        public FlagSyncer(DMPGame dmpGame, Settings dmpSettings, NetworkWorker networkWorker)
        {
            this.dmpGame = dmpGame;
            this.dmpSettings = dmpSettings;
            this.networkWorker = networkWorker;
            flagPath = Path.Combine(Path.Combine(Client.dmpClient.gameDataDir, "DarkMultiPlayer"), "Flags");
            dmpGame.updateEvent.Add(Update);
        }

        public void SendFlagList()
        {
            string[] dmpFlags = Directory.GetFiles(flagPath);
            string[] dmpSha = new string[dmpFlags.Length];
            for (int i = 0; i < dmpFlags.Length; i++)
            {
                dmpSha[i] = Common.CalculateSHA256Hash(dmpFlags[i]);
                dmpFlags[i] = Path.GetFileName(dmpFlags[i]);
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)FlagMessageType.LIST);
                mw.Write<string>(dmpSettings.playerName);
                mw.Write<string[]>(dmpFlags);
                mw.Write<string[]>(dmpSha);
                networkWorker.SendFlagMessage(mw.GetMessageBytes());
            }
        }

        public void HandleMessage(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                FlagMessageType messageType = (FlagMessageType)mr.Read<int>();
                switch (messageType)
                {
                    case FlagMessageType.LIST:
                        {
                            //List code
                            string[] serverFlagFiles = mr.Read<string[]>();
                            string[] serverFlagOwners = mr.Read<string[]>();
                            string[] serverFlagShaSums = mr.Read<string[]>();
                            for (int i = 0; i < serverFlagFiles.Length; i++)
                            {
                                FlagInfo fi = new FlagInfo();
                                fi.owner = serverFlagOwners[i];
                                fi.shaSum = serverFlagShaSums[i];
                                serverFlags[Path.GetFileNameWithoutExtension(serverFlagFiles[i])] = fi;
                            }
                            syncComplete = true;
                            //Check if we need to upload the flag
                            flagChangeEvent = true;
                        }
                        break;
                    case FlagMessageType.FLAG_DATA:
                        {
                            FlagRespondMessage frm = new FlagRespondMessage();
                            frm.flagInfo.owner = mr.Read<string>();
                            frm.flagName = mr.Read<string>();
                            frm.flagData = mr.Read<byte[]>();
                            frm.flagInfo.shaSum = Common.CalculateSHA256Hash(frm.flagData);
                            newFlags.Enqueue(frm);
                        }
                        break;
                    case FlagMessageType.DELETE_FILE:
                        {
                            string flagName = mr.Read<string>();
                            string flagFile = Path.Combine(flagPath, flagName);
                            if (File.Exists(flagFile))
                            {
                                try
                                {

                                    if (File.Exists(flagFile))
                                    {
                                        DarkLog.Debug("Deleting flag " + flagFile);
                                        File.Delete(flagFile);
                                    }
                                }
                                catch (Exception e)
                                {
                                    DarkLog.Debug("Error deleting flag " + flagFile + ", exception: " + e);
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private void Update()
        {
            if (workerEnabled && syncComplete && (HighLogic.CurrentGame != null && HighLogic.CurrentGame.flagURL != null))
            {
                if (flagChangeEvent)
                {
                    flagChangeEvent = false;
                    HandleFlagChangeEvent();
                }
                while (newFlags.Count > 0)
                {
                    HandleFlagRespondMessage(newFlags.Dequeue());
                }
            }
        }

        private void HandleFlagChangeEvent()
        {
            string flagURL = HighLogic.CurrentGame.flagURL;
            if (!flagURL.ToLower().StartsWith("darkmultiplayer/flags/", StringComparison.Ordinal))
            {
                //If it's not a DMP flag don't sync it.
                return;
            }
            string flagName = flagURL.Substring("DarkMultiPlayer/Flags/".Length);
            if (serverFlags.ContainsKey(flagName) && serverFlags[flagName].owner != dmpSettings.playerName)
            {
                //If the flag is owned by someone else don't sync it
                return;
            }
            string flagFile = "";

            string[] flagFiles = Directory.GetFiles(flagPath, "*", SearchOption.TopDirectoryOnly);
            foreach (string possibleMatch in flagFiles)
            {
                if (flagName.ToLower() == Path.GetFileNameWithoutExtension(possibleMatch).ToLower())
                {
                    flagFile = possibleMatch;
                }
            }
            //Sanity check to make sure we found the file
            if (flagFile != "" && File.Exists(flagFile))
            {
                string shaSum = Common.CalculateSHA256Hash(flagFile);
                if (serverFlags.ContainsKey(flagName) && serverFlags[flagName].shaSum == shaSum)
                {
                    //Don't send the flag when the SHA sum already matches
                    return;
                }
                DarkLog.Debug("Uploading " + Path.GetFileName(flagFile));
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)FlagMessageType.UPLOAD_FILE);
                    mw.Write<string>(dmpSettings.playerName);
                    mw.Write<string>(Path.GetFileName(flagFile));
                    mw.Write<byte[]>(File.ReadAllBytes(flagFile));
                    networkWorker.SendFlagMessage(mw.GetMessageBytes());
                }
                FlagInfo fi = new FlagInfo();
                fi.owner = dmpSettings.playerName;
                fi.shaSum = Common.CalculateSHA256Hash(flagFile);
                serverFlags[flagName] = fi;
            }

        }

        private void HandleFlagRespondMessage(FlagRespondMessage flagRespondMessage)
        {
            serverFlags[flagRespondMessage.flagName] = flagRespondMessage.flagInfo;
            string flagFile = Path.Combine(flagPath, flagRespondMessage.flagName);
            Texture2D flagTexture = new Texture2D(4, 4);
            if (flagTexture.LoadImage(flagRespondMessage.flagData))
            {
                flagTexture.name = "DarkMultiPlayer/Flags/" + Path.GetFileNameWithoutExtension(flagRespondMessage.flagName);
                File.WriteAllBytes(flagFile, flagRespondMessage.flagData);
                GameDatabase.TextureInfo ti = new GameDatabase.TextureInfo(null, flagTexture, false, true, false);
                ti.name = flagTexture.name;
                bool containsTexture = false;
                foreach (GameDatabase.TextureInfo databaseTi in GameDatabase.Instance.databaseTexture)
                {
                    containsTexture |= databaseTi.name == ti.name;
                }
                if (!containsTexture)
                {
                    GameDatabase.Instance.databaseTexture.Add(ti);
                }
                else
                {
                    GameDatabase.Instance.ReplaceTexture(ti.name, ti);
                }
                DarkLog.Debug("Loaded " + flagTexture.name);
            }
            else
            {
                DarkLog.Debug("Failed to load flag " + flagRespondMessage.flagName);
            }
        }

        public void Stop()
        {
            workerEnabled = false;
            dmpGame.updateEvent.Remove(Update);
        }
    }

    public class FlagRespondMessage
    {
        public string flagName;
        public byte[] flagData;
        public FlagInfo flagInfo = new FlagInfo();
    }

    public class FlagInfo
    {
        public string shaSum;
        public string owner;
    }
}

