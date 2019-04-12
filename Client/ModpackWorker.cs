using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayer
{
    public class ModpackWorker
    {
        public static bool secondModSync = false;
        public bool missingWarnFile = false;
        public bool synced = false;
        public string syncString = "Syncing files";
        private int filesDownloaded = 0;
        private ModpackMode modpackMode = ModpackMode.NONE;
        private Queue<byte[]> messageQueue = new Queue<byte[]>();
        private DMPGame dmpGame;
        private Settings dmpSettings;
        private ModWorker modWorker;
        private NetworkWorker networkWorker;
        private ChatWorker chatWorker;
        private AdminSystem adminSystem;
        /// <summary>
        /// KSP's gamedata path
        /// </summary>
        private readonly string gameDataPath;
        /// <summary>
        /// DarkMultiPlayers object store path
        /// </summary>
        private readonly string cacheDataPath;
        /// <summary>
        /// The servers CKAN index for DMPModpackUpdater
        /// </summary>
        private readonly string ckanDataPath;
        /// <summary>
        /// The servers GameData index for DMPModpackUpdater
        /// </summary>
        private readonly string gameDataServerCachePath;
        /// <summary>
        /// Our GameData cache, so we don't have to hash every single object every boot when connecting GAMEDATA mode servers.
        /// </summary>
        private readonly string gameDataClientCachePath;
        /// <summary>
        /// The servers GameData index
        /// </summary>
        private Dictionary<string, string> serverPathCache = new Dictionary<string, string>();
        /// <summary>
        /// The clients GameData index
        /// </summary>
        private Dictionary<string, string> clientPathCache = new Dictionary<string, string>();
        /// <summary>
        /// The clients files to hash
        /// </summary>
        private string[] modFilesToHash;
        private int modFilesToHashPos;
        private HashSet<string> noWarnSha = new HashSet<string>();
        private List<string> ignoreList = Common.GetExclusionList();
        private List<string> containsIgnoreList = Common.GetContainsExclusionList();
        private List<string> noWarnList = Common.GetContainsNoWarningList();
        private List<string> requestList = new List<string>();
        private bool uploadAfterHashing = false;
        private string[] modFilesToUpload;
        private int modFilesToUploadPos;
        private bool registeredChatCommand = false;
        private ScreenMessage screenMessage;
        private long nextScreenMessageUpdate;
        private int numHashingThreads = 2;
        private Thread[] hashingThreads;

        public ModpackWorker(DMPGame dmpGame, Settings dmpSettings, ModWorker modWorker, NetworkWorker networkWorker, ChatWorker chatWorker, AdminSystem adminSystem)
        {
            gameDataPath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData");
            cacheDataPath = Path.Combine(KSPUtil.ApplicationRootPath, "DarkMultiPlayer-ModCache");
            Directory.CreateDirectory(cacheDataPath);
            ckanDataPath = Path.Combine(KSPUtil.ApplicationRootPath, "DarkMultiPlayer.ckan");
            gameDataServerCachePath = Path.Combine(KSPUtil.ApplicationRootPath, "DarkMultiPlayer-Server-GameData.txt");
            gameDataClientCachePath = Path.Combine(KSPUtil.ApplicationRootPath, "DarkMultiPlayer-Client-GameData.txt");
            this.dmpGame = dmpGame;
            this.dmpSettings = dmpSettings;
            this.modWorker = modWorker;
            this.networkWorker = networkWorker;
            this.chatWorker = chatWorker;
            this.adminSystem = adminSystem;
            dmpGame.updateEvent.Add(Update);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
            try
            {
                numHashingThreads = Environment.ProcessorCount;
            }
            catch
            {
                Console.WriteLine("Environment.ProcessorCount does not work");
            }
        }

        private void OnGameSceneLoadRequested(GameScenes gameScene)
        {
            if (screenMessage != null)
            {
                screenMessage.duration = 0f;
                screenMessage = null;
            }
        }

        public void Stop(GameScenes gameScene)
        {
            dmpGame.updateEvent.Remove(Update);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
        }

        private void Update()
        {
            //Don't process incoming files or MOD_COMPLETE if we are hashing our gamedata folder
            if (hashingThreads == null)
            {
                lock (messageQueue)
                {
                    while (messageQueue.Count > 0)
                    {
                        RealHandleMessage(messageQueue.Dequeue());
                        //Don't process incoming files or MOD_COMPLETE if we are hashing our gamedata folder
                        if (hashingThreads != null)
                        {
                            syncString = "Hashing 0/" + modFilesToHash.Length + " files";
                            break;
                        }
                    }
                    if (networkWorker.state == ClientState.RUNNING)
                    {
                        while (modFilesToUpload != null && networkWorker.GetStatistics("QueuedOutBytes") < 1000000)
                        {
                            RealSendToServer();
                            if (modFilesToUpload != null)
                            {
                                syncString = "Uploading " + modFilesToUploadPos + "/" + modFilesToUpload.Length + " files";
                                DarkLog.Debug(syncString);
                                if (screenMessage != null && DateTime.UtcNow.Ticks > nextScreenMessageUpdate)
                                {
                                    nextScreenMessageUpdate = DateTime.UtcNow.Ticks + (TimeSpan.TicksPerMillisecond * 100);
                                    screenMessage.duration = 0f;
                                    screenMessage = ScreenMessages.PostScreenMessage(syncString);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                long stopTime = DateTime.UtcNow.Ticks + (TimeSpan.TicksPerMillisecond * 100);
                CheckHashingThreads();
                if (hashingThreads == null)
                {
                    using (StreamWriter sw = new StreamWriter(gameDataClientCachePath))
                    {
                        foreach (KeyValuePair<string, string> kvp in clientPathCache)
                        {
                            sw.WriteLine("{0}={1}", kvp.Key, kvp.Value);
                        }
                    }
                    syncString = "Hashed " + clientPathCache.Count + " files";
                    DarkLog.Debug("Hashed " + clientPathCache.Count + " files");
                    if (screenMessage != null && DateTime.UtcNow.Ticks > nextScreenMessageUpdate)
                    {
                        nextScreenMessageUpdate = DateTime.UtcNow.Ticks + (TimeSpan.TicksPerMillisecond * 100);
                        screenMessage.duration = 0f;
                        screenMessage = ScreenMessages.PostScreenMessage(syncString);
                    }
                    if (uploadAfterHashing)
                    {
                        uploadAfterHashing = false;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            List<string> uploadfiles = new List<string>(clientPathCache.Keys);
                            List<string> uploadsha = new List<string>(clientPathCache.Values);
                            mw.Write<int>((int)ModpackDataMessageType.MOD_LIST);
                            mw.Write<string[]>(uploadfiles.ToArray());
                            mw.Write<string[]>(uploadsha.ToArray());
                            networkWorker.SendModpackMessage(mw.GetMessageBytes());
                        }
                    }
                }
                else
                {
                    syncString = "Hashing " + modFilesToHashPos + "/" + modFilesToHash.Length + " files";
                    if (screenMessage != null && DateTime.UtcNow.Ticks > nextScreenMessageUpdate)
                    {
                        nextScreenMessageUpdate = DateTime.UtcNow.Ticks + (TimeSpan.TicksPerMillisecond * 100);
                        screenMessage.duration = 0f;
                        screenMessage = ScreenMessages.PostScreenMessage(syncString);
                    }
                    DarkLog.Debug("Hashing " + modFilesToHashPos + "/" + modFilesToHash.Length + " files");
                }
            }
        }

        private void CheckHashingThreads()
        {
            if (hashingThreads == null)
            {
                return;
            }
            bool isHashing = false;
            foreach (Thread t in hashingThreads)
            {
                if (t.IsAlive)
                {
                    isHashing = true;
                }
            }
            if (!isHashing)
            {
                modFilesToHash = null;
                modFilesToHashPos = 0;
                hashingThreads = null;
                if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                {
                    CompareGameDatas();
                }
            }
        }

        private void RealSendToServer()
        {
            if (modFilesToUpload == null)
            {
                DarkLog.Debug("Calling RealSendToServer while null?");
                return;
            }
            if (modFilesToUpload != null && modFilesToUploadPos >= modFilesToUpload.Length)
            {
                modFilesToUpload = null;
                modFilesToUploadPos = 0;
                if (screenMessage != null)
                {
                    screenMessage.duration = 0f;
                    screenMessage = null;
                    ScreenMessages.PostScreenMessage("Upload done!", 5f, ScreenMessageStyle.UPPER_CENTER);
                }
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)ModpackDataMessageType.MOD_DONE);
                    mw.Write<bool>(true);
                    modWorker.GenerateModControlFile(false, false);
                    byte[] tempModControl = File.ReadAllBytes(Path.Combine(KSPUtil.ApplicationRootPath, "mod-control.txt"));
                    mw.Write<byte[]>(tempModControl);
                    networkWorker.SendModpackMessage(mw.GetMessageBytes());
                }
                return;
            }
            string shaToUpload = modFilesToUpload[modFilesToUploadPos];
            DarkLog.Debug("Uploading object: " + shaToUpload);
            modFilesToUploadPos++;
            using (MessageWriter mw = new MessageWriter())
            {
                string fileToUploadPath = Path.Combine(cacheDataPath, shaToUpload + ".bin");
                mw.Write<int>((int)ModpackDataMessageType.RESPONSE_OBJECT);
                mw.Write<string>(shaToUpload);
                if (File.Exists(fileToUploadPath))
                {
                    byte[] fileBytes = File.ReadAllBytes(fileToUploadPath);
                    mw.Write<bool>(true);
                    mw.Write<byte[]>(fileBytes);
                }
                else
                {
                    mw.Write<bool>(false);
                }
                networkWorker.SendModpackMessage(mw.GetMessageBytes());
            }
        }

        private void UploadToServer(string chatCommand)
        {
            if (adminSystem.IsAdmin(dmpSettings.playerName) && !uploadAfterHashing)
            {
                uploadAfterHashing = true;
                screenMessage = ScreenMessages.PostScreenMessage("Uploading GameData", float.MaxValue, ScreenMessageStyle.UPPER_CENTER);
                UpdateCache();
            }
            else
            {
                screenMessage = ScreenMessages.PostScreenMessage("You are not an admin, unable to upload", float.MaxValue, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void UploadCKANToServer(string chatCommand)
        {
            if (adminSystem.IsAdmin(dmpSettings.playerName))
            {
                Console.WriteLine();
                string tempCkanPath = Path.Combine(KSPUtil.ApplicationRootPath, "DarkMultiPlayer-new.ckan");
                if (File.Exists(tempCkanPath))
                {
                    ScreenMessages.PostScreenMessage("Uploaded KSP/DarkMultiPlayer-new.ckan", 5f, ScreenMessageStyle.UPPER_CENTER);
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)ModpackDataMessageType.CKAN);
                        byte[] tempCkanBytes = File.ReadAllBytes(tempCkanPath);
                        mw.Write<byte[]>(tempCkanBytes);
                        networkWorker.SendModpackMessage(mw.GetMessageBytes());
                    }
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<int>((int)ModpackDataMessageType.MOD_DONE);
                        mw.Write<bool>(true);
                        modWorker.GenerateModControlFile(false, false);
                        byte[] tempModControl = File.ReadAllBytes(Path.Combine(KSPUtil.ApplicationRootPath, "mod-control.txt"));
                        mw.Write<byte[]>(tempModControl);
                        networkWorker.SendModpackMessage(mw.GetMessageBytes());
                    }
                }
                else
                {
                    ScreenMessages.PostScreenMessage("KSP/DarkMultiPlayer-new.ckan does not exist", 5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            else
            {
                screenMessage = ScreenMessages.PostScreenMessage("You are not an admin, unable to upload", float.MaxValue, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void LoadAuto()
        {
            clientPathCache.Clear();
            if (File.Exists(gameDataClientCachePath))
            {
                using (StreamReader sr = new StreamReader(gameDataClientCachePath))
                {
                    string currentLine = null;
                    while ((currentLine = sr.ReadLine()) != null)
                    {
                        int splitPos = currentLine.LastIndexOf('=');
                        string path = currentLine.Substring(0, splitPos);
                        string sha256sum = currentLine.Substring(splitPos + 1);
                        clientPathCache.Add(path, sha256sum);
                    }
                }
            }
            int realFiles = 0;
            string[] modFiles = Directory.GetFiles(gameDataPath, "*", SearchOption.AllDirectories);
            foreach (string filePath in modFiles)
            {
                if (!filePath.ToLower().StartsWith(gameDataPath.ToLower(), StringComparison.Ordinal))
                {
                    continue;
                }
                string trimmedPath = filePath.Substring(gameDataPath.Length + 1).Replace('\\', '/');
                bool skipFile = false;
                foreach (string ignoreString in ignoreList)
                {
                    if (trimmedPath.ToLower().StartsWith(ignoreString, StringComparison.Ordinal))
                    {
                        skipFile = true;
                    }
                }
                foreach (string ignoreString in containsIgnoreList)
                {
                    if (trimmedPath.ToLower().Contains(ignoreString))
                    {
                        skipFile = true;
                    }
                }
                if (skipFile)
                {
                    continue;
                }
                realFiles++;
            }
            if (realFiles != clientPathCache.Count)
            {
                UpdateCache();
            }
            else
            {
                CompareGameDatas();
            }
        }

        private void UpdateCache()
        {
            if (File.Exists(gameDataClientCachePath))
            {
                File.Delete(gameDataClientCachePath);
            }
            clientPathCache.Clear();
            List<string> filesToHashTemp = new List<string>();
            string[] modFilesToHashTemp = Directory.GetFiles(gameDataPath, "*", SearchOption.AllDirectories);
            foreach (string filePath in modFilesToHashTemp)
            {
                if (!filePath.ToLower().StartsWith(gameDataPath.ToLower(), StringComparison.Ordinal))
                {
                    continue;
                }
                string trimmedPath = filePath.Substring(gameDataPath.Length + 1).Replace('\\', '/');
                bool skipFile = false;
                foreach (string ignoreString in ignoreList)
                {
                    if (trimmedPath.ToLower().StartsWith(ignoreString, StringComparison.Ordinal))
                    {
                        skipFile = true;
                    }
                }
                foreach (string ignoreString in containsIgnoreList)
                {
                    if (trimmedPath.ToLower().Contains(ignoreString))
                    {
                        skipFile = true;
                    }
                }
                if (skipFile)
                {
                    continue;
                }
                filesToHashTemp.Add(filePath);
            }
            modFilesToHash = filesToHashTemp.ToArray();
            modFilesToHashPos = 0;
            if (modFilesToHash.Length == 0)
            {
                DarkLog.Debug("Not starting hashing thread, nothing to hash.");
                modFilesToHash = null;
                if (HighLogic.LoadedScene == GameScenes.MAINMENU)
                {
                    CompareGameDatas();
                }
            }
            else
            {
                //Don't multithread if we only have a few files
                if (modFilesToHash.Length < 50)
                {
                    hashingThreads = new Thread[1];
                    int[] threadStartObject = new int[] { 0, modFilesToHash.Length - 1 };
                    hashingThreads[0] = new Thread(new ParameterizedThreadStart(HashingThreadMain));
                    hashingThreads[0].Start(threadStartObject);
                    DarkLog.Debug("Started hashing thread from " + threadStartObject[0] + " to " + threadStartObject[1]);
                }
                else
                {
                    int lastEnd = 0;
                    int numSplit = modFilesToHash.Length / numHashingThreads;
                    hashingThreads = new Thread[numHashingThreads];
                    for (int i = 0; i < (numHashingThreads - 1); i++)
                    {

                        //First threads
                        int[] threadStartObject = new int[2];
                        threadStartObject[0] = lastEnd;
                        lastEnd = lastEnd + numSplit;
                        threadStartObject[1] = lastEnd;
                        lastEnd++;
                        hashingThreads[i] = new Thread(new ParameterizedThreadStart(HashingThreadMain));
                        hashingThreads[i].Start(threadStartObject);
                        DarkLog.Debug("Started hashing thread from " + threadStartObject[0] + " to " + threadStartObject[1]);


                    }
                    int[] threadStartObjectLast = new int[] { lastEnd, modFilesToHash.Length - 1 };
                    hashingThreads[numHashingThreads - 1] = new Thread(new ParameterizedThreadStart(HashingThreadMain));
                    hashingThreads[numHashingThreads - 1].Start(threadStartObjectLast);
                    DarkLog.Debug("Started hashing thread from " + threadStartObjectLast[0] + " to " + threadStartObjectLast[1]);
                }
            }
        }

        private void HashingThreadMain(object startEndArray)
        {
            int[] startEnd = (int[])startEndArray;
            Dictionary<string, string> threadHashing = new Dictionary<string, string>();
            for (int i = startEnd[0]; i <= startEnd[1]; i++)
            {
                string filePath = modFilesToHash[i];
                string trimmedPath = filePath.Substring(gameDataPath.Length + 1).Replace('\\', '/');
                Interlocked.Increment(ref modFilesToHashPos);
                try
                {
                    byte[] fileBytes = File.ReadAllBytes(filePath);
                    string sha256sum = Common.CalculateSHA256Hash(fileBytes);
                    string thisCachePath = Path.Combine(cacheDataPath, sha256sum + ".bin");
                    if (!File.Exists(thisCachePath))
                    {
                        File.Copy(filePath, thisCachePath);
                    }
                    threadHashing.Add(trimmedPath, sha256sum);
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Error reading: " + trimmedPath + ", Exception: " + e);
                }
            }
            lock (clientPathCache)
            {
                foreach (KeyValuePair<string, string> kvp in threadHashing)
                {
                    clientPathCache.Add(kvp.Key, kvp.Value);
                }
            }
        }

        public void HandleModpackMessage(byte[] messageData)
        {
            lock (messageQueue)
            {
                messageQueue.Enqueue(messageData);
            }
        }

        private void RealHandleMessage(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                ModpackDataMessageType type = (ModpackDataMessageType)mr.Read<int>();
                switch (type)
                {
                    case ModpackDataMessageType.CKAN:
                        {
                            modpackMode = ModpackMode.CKAN;
                            byte[] receiveData = mr.Read<byte[]>();
                            byte[] oldData = null;
                            if (File.Exists(ckanDataPath))
                            {
                                oldData = File.ReadAllBytes(ckanDataPath);
                            }
                            if (!BytesMatch(oldData, receiveData))
                            {
                                missingWarnFile = true;
                                DarkLog.Debug("Ckan file changed");
                                File.Delete(ckanDataPath);
                                File.WriteAllBytes(ckanDataPath, receiveData);
                            }
                            if (!registeredChatCommand)
                            {
                                registeredChatCommand = true;
                                chatWorker.RegisterChatCommand("upload", UploadCKANToServer, "Upload DarkMultiPlayer.ckan to the server");
                            }
                        }
                        break;
                    case ModpackDataMessageType.MOD_LIST:
                        {
                            modFilesToHash = null;
                            modFilesToHashPos = 0;
                            serverPathCache.Clear();
                            noWarnSha.Clear();
                            modpackMode = ModpackMode.GAMEDATA;
                            string[] files = mr.Read<string[]>();
                            string[] sha = mr.Read<string[]>();
                            if (File.Exists(gameDataServerCachePath))
                            {
                                File.Delete(gameDataServerCachePath);
                            }
                            using (StreamWriter sw = new StreamWriter(gameDataServerCachePath))
                            {
                                for (int i = 0; i < files.Length; i++)
                                {
                                    bool skipFile = false;
                                    foreach (string ignoreString in ignoreList)
                                    {
                                        if (files[i].ToLower().StartsWith(ignoreString, StringComparison.Ordinal))
                                        {
                                            skipFile = true;
                                        }
                                    }
                                    foreach (string ignoreString in containsIgnoreList)
                                    {
                                        if (files[i].ToLower().Contains(ignoreString))
                                        {
                                            skipFile = true;
                                        }
                                    }
                                    if (skipFile)
                                    {
                                        continue;
                                    }
                                    sw.WriteLine("{0}={1}", files[i], sha[i]);
                                    serverPathCache.Add(files[i], sha[i]);
                                }
                            }
                            LoadAuto();
                            if (!registeredChatCommand)
                            {
                                registeredChatCommand = true;
                                chatWorker.RegisterChatCommand("upload", UploadToServer, "Upload GameData to the server");
                            }
                        }
                        break;
                    case ModpackDataMessageType.REQUEST_OBJECT:
                        {
                            modFilesToUpload = mr.Read<string[]>();
                            modFilesToUploadPos = 0;
                            DarkLog.Debug("Server requested " + modFilesToUpload.Length + " files");
                        }
                        break;
                    case ModpackDataMessageType.RESPONSE_OBJECT:
                        {
                            string sha256sum = mr.Read<string>();
                            filesDownloaded++;
                            if (mr.Read<bool>())
                            {
                                syncString = "Syncing files " + filesDownloaded + "/" + requestList.Count + " (" + (serverPathCache.Count - requestList.Count) + " cached)";
                                byte[] fileBytes = mr.Read<byte[]>();
                                string filePath = Path.Combine(cacheDataPath, sha256sum + ".bin");
                                if (!File.Exists(filePath))
                                {
                                    File.WriteAllBytes(filePath, fileBytes);
                                }
                            }
                            else
                            {
                                ScreenMessages.PostScreenMessage("DMP Server has an out of date hash list. Tell the admin to run /reloadmods", float.PositiveInfinity, ScreenMessageStyle.UPPER_CENTER);
                                networkWorker.Disconnect("Syncing files error");
                            }
                            if (filesDownloaded == requestList.Count)
                            {
                                if (missingWarnFile)
                                {
                                    networkWorker.Disconnect("Syncing files " + filesDownloaded + "/" + requestList.Count + " (" + (serverPathCache.Count - requestList.Count) + " cached)");
                                    ScreenMessages.PostScreenMessage("Please run DMPModpackUpdater or reconnect to ignore", float.PositiveInfinity, ScreenMessageStyle.UPPER_CENTER);
                                }
                                else
                                {
                                    synced = true;
                                }
                            }
                        }
                        break;
                    case ModpackDataMessageType.MOD_DONE:
                        {
                            if ((!missingWarnFile && requestList.Count == 0) || secondModSync)
                            {
                                synced = true;
                            }
                            else
                            {
                                if (modpackMode == ModpackMode.CKAN)
                                {
                                    ScreenMessages.PostScreenMessage("Please install CKAN update at KSP/DarkMultiPlayer.ckan or reconnect to ignore", float.PositiveInfinity, ScreenMessageStyle.UPPER_CENTER);
                                    networkWorker.Disconnect("Synced DarkMultiPlayer.ckan");
                                }
                                if (modpackMode == ModpackMode.GAMEDATA && requestList.Count == 0)
                                {
                                    ScreenMessages.PostScreenMessage("Please run DMPModpackUpdater or reconnect to ignore", float.PositiveInfinity, ScreenMessageStyle.UPPER_CENTER);
                                    syncString = "Synced files (" + serverPathCache.Count + " cached)";
                                    networkWorker.Disconnect(syncString);
                                }
                            }
                            secondModSync = true;
                        }
                        break;
                }
            }
        }

        private bool BytesMatch(byte[] lhs, byte[] rhs)
        {
            if (lhs == null && rhs == null)
            {
                return true;
            }
            if (lhs == null || rhs == null)
            {
                return false;
            }
            if (lhs.Length != rhs.Length)
            {
                return false;
            }
            for (int i = 0; i < lhs.Length; i++)
            {
                if (lhs[i] != rhs[i])
                {
                    return false;
                }
            }
            return true;
        }

        private void CompareGameDatas()
        {
            foreach (KeyValuePair<string, string> kvp in serverPathCache)
            {
                string thisCachePath = Path.Combine(cacheDataPath, kvp.Value + ".bin");
                bool thisInNoWarn = false;
                foreach (string noWarnString in noWarnList)
                {
                    if (!noWarnSha.Contains(kvp.Value) && kvp.Key.ToLower().Contains(noWarnString))
                    {
                        thisInNoWarn = true;
                        noWarnSha.Add(kvp.Value);
                    }
                }
                if (!clientPathCache.ContainsKey(kvp.Key))
                {
                    if (!thisInNoWarn)
                    {
                        missingWarnFile = true;
                    }
                    if (!File.Exists(thisCachePath))
                    {
                        if (!requestList.Contains(kvp.Value))
                        {
                            DarkLog.Debug("Requesting: " + kvp.Key + ", sha: " + kvp.Value);
                            requestList.Add(kvp.Value);
                        }
                    }
                    else
                    {
                        DarkLog.Debug("Missing: " + kvp.Key + " (in cache)");
                    }
                }
                if (clientPathCache.ContainsKey(kvp.Key) && clientPathCache[kvp.Key] != kvp.Value)
                {
                    if (!thisInNoWarn)
                    {
                        missingWarnFile = true;
                    }
                    if (!File.Exists(thisCachePath))
                    {
                        if (!requestList.Contains(kvp.Value))
                        {
                            DarkLog.Debug("Requesting: " + kvp.Key + ", sha: " + kvp.Value);
                            requestList.Add(kvp.Value);
                        }
                    }
                    else
                    {
                        DarkLog.Debug("Missing: " + kvp.Key + " (in cache)");
                    }
                }
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ModpackDataMessageType.REQUEST_OBJECT);
                mw.Write<string[]>(requestList.ToArray());
                networkWorker.SendModpackMessage(mw.GetMessageBytes());
            }
        }

        public void Stop()
        {
            dmpGame.updateEvent.Remove(Update);
        }
    }
}
