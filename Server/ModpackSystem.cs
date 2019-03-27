using System;
using System.Collections.Generic;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;
namespace DarkMultiPlayerServer
{
    public class ModpackSystem
    {
        private static List<string> excludeList;
        private static List<string> containsExcludeList;
        private static string modpackPath;
        private static string modpackServerCacheObjects;
        private static string ckanPath;
        private static ModpackSystem instance;
        //From client for uploading, Path - SHA256Sum
        private Dictionary<string, string> clientData = new Dictionary<string, string>();
        private int clientReceived = 0;
        //Path - SHA256Sum
        private Dictionary<string, string> modpackData = new Dictionary<string, string>();
        private int hashCount = 0;
        private long nextHashTime = 0;
        //SHA256Sum - Path
        private Dictionary<string, string> objectData = new Dictionary<string, string>();
        //Send limited files at once, we can't queue gigabytes up on the network interface in one server frame.
        private Dictionary<ClientObject, SendFileData> filesToSend = new Dictionary<ClientObject, SendFileData>();

        public static ModpackSystem fetch
        {
            get
            {
                //Lazy loading
                if (instance == null)
                {
                    excludeList = Common.GetExclusionList();
                    containsExcludeList = Common.GetExclusionList();
                    if (Settings.settingsStore.modpackMode == DarkMultiPlayerCommon.ModpackMode.GAMEDATA)
                    {
                        modpackPath = Path.Combine(Server.configDirectory, "GameData");
                        modpackServerCacheObjects = Path.Combine(Server.configDirectory, "GameDataCache.txt");
                        Directory.CreateDirectory(modpackPath);
                    }
                    if (Settings.settingsStore.modpackMode == DarkMultiPlayerCommon.ModpackMode.CKAN)
                    {
                        ckanPath = Path.Combine(Server.configDirectory, "DarkMultiPlayer.ckan");
                    }
                    instance = new ModpackSystem();
                    instance.LoadAuto();
                }
                return instance;
            }
        }

        public void HandleSendList(ClientObject client, string[] sha256sums)
        {
            SendFileData sfd = new SendFileData(sha256sums);
            lock (filesToSend)
            {
                if (filesToSend.ContainsKey(client))
                {
                    filesToSend.Remove(client);
                }
                filesToSend.Add(client, sfd);
            }
        }

        public void SendFilesToClients()
        {
            lock (filesToSend)
            {
                ClientObject removeObject = null;
                foreach (KeyValuePair<ClientObject, SendFileData> kvp in filesToSend)
                {
                    bool sentData = false;
                    if (kvp.Key.connectionStatus == ConnectionStatus.DISCONNECTED)
                    {
                        removeObject = kvp.Key;
                        continue;
                    }
                    //Keep a megabyte queued while sending
                    while (kvp.Key.bytesQueuedOut < 1000000 && kvp.Value.position < kvp.Value.sha256sums.Length)
                    {
                        sentData = true;
                        string sha256sum = kvp.Value.sha256sums[kvp.Value.position];
                        kvp.Value.position++;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<int>((int)ModpackDataMessageType.RESPONSE_OBJECT);
                            mw.Write<string>(sha256sum);
                            byte[] fileData = GetModObject(sha256sum);
                            if (fileData != null)
                            {
                                mw.Write<bool>(true);
                                mw.Write<byte[]>(fileData);
                            }
                            else
                            {
                                mw.Write<bool>(false);
                            }
                            Messages.Modpack.SendModData(kvp.Key, mw.GetMessageBytes());
                        }
                    }
                    if (sentData)
                    {
                        DarkLog.Debug("Sending client: " + kvp.Value.position + "/" + kvp.Value.sha256sums.Length);
                    }
                    if (kvp.Value.position == kvp.Value.sha256sums.Length)
                    {
                        removeObject = kvp.Key;
                    }
                }
                if (removeObject != null)
                {
                    filesToSend.Remove(removeObject);
                }
            }
        }

        public void HandleReloadCommand(string commandText)
        {
            Load();
        }

        public void LoadAuto()
        {
            if (Settings.settingsStore.modpackMode == DarkMultiPlayerCommon.ModpackMode.GAMEDATA)
            {
                LoadCache();
                int realFiles = 0;
                string[] modFiles = Directory.GetFiles(modpackPath, "*", SearchOption.AllDirectories);
                foreach (string filePath in modFiles)
                {
                    if (!filePath.ToLower().StartsWith(modpackPath.ToLower(), StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string trimmedPath = filePath.Substring(modpackPath.Length + 1).Replace('\\', '/');
                    bool skipFile = false;
                    foreach (string excludePath in excludeList)
                    {
                        if (trimmedPath.ToLower().StartsWith(excludePath, StringComparison.Ordinal))
                        {
                            skipFile = true;
                        }
                    }
                    foreach (string excludePath in containsExcludeList)
                    {
                        if (trimmedPath.ToLower().Contains(excludePath))
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
                if (modpackData.Count != realFiles)
                {
                    DarkLog.Normal("GameData files length changed, automatically regenerating");
                    Load();
                }
            }
        }

        internal void HandleNewGameData(string[] files, string[] sha, ClientObject client)
        {
            if (files.Length != sha.Length)
            {
                DarkLog.Normal("Files and SHA list does not match, not using modpack");
                return;
            }
            clientReceived = 0;
            clientData.Clear();
            List<string> tempRequestObjects = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                clientData.Add(files[i], sha[i]);
                //Skip files we have
                if (modpackData.ContainsKey(files[i]) && modpackData[files[i]] == sha[i])
                {
                    continue;
                }
                if (!tempRequestObjects.Contains(sha[i]))
                {
                    tempRequestObjects.Add(sha[i]);
                }
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ModpackDataMessageType.REQUEST_OBJECT);
                mw.Write<string[]>(tempRequestObjects.ToArray());
                Messages.Modpack.SendModData(client, mw.GetMessageBytes());
            }
        }

        private void LoadCache()
        {
            modpackData.Clear();
            objectData.Clear();
            if (!File.Exists(modpackServerCacheObjects))
            {
                return;
            }
            using (StreamReader sr = new StreamReader(modpackServerCacheObjects))
            {
                string currentLine = null;
                while ((currentLine = sr.ReadLine()) != null)
                {
                    int splitPos = currentLine.LastIndexOf('=');
                    string path = currentLine.Substring(0, splitPos);
                    string sha256sum = currentLine.Substring(splitPos + 1);
                    if (!modpackData.ContainsKey(path))
                    {
                        modpackData.Add(path, sha256sum);
                    }
                    if (!objectData.ContainsKey(sha256sum))
                    {
                        objectData.Add(sha256sum, path);
                    }
                }
            }
        }

        public void Load()
        {
            if (Settings.settingsStore.modpackMode == DarkMultiPlayerCommon.ModpackMode.GAMEDATA)
            {
                DarkLog.Debug("Loading GameData mod list");
                if (File.Exists(modpackServerCacheObjects))
                {
                    File.Delete(modpackServerCacheObjects);
                }
                hashCount = 0;
                modpackData.Clear();
                objectData.Clear();
                string[] modFiles = Directory.GetFiles(modpackPath, "*", SearchOption.AllDirectories);
                foreach (string filePath in modFiles)
                {
                    hashCount++;
                    if (!filePath.ToLower().StartsWith(modpackPath.ToLower(), StringComparison.Ordinal))
                    {
                        DarkLog.Error("Not adding file that is in GameData, symlinks are not supported.");
                        DarkLog.Error("File was: " + filePath);
                        continue;
                    }
                    string trimmedPath = filePath.Substring(modpackPath.Length + 1).Replace('\\', '/');
                    bool skipFile = false;
                    foreach (string excludePath in excludeList)
                    {
                        if (trimmedPath.ToLower().StartsWith(excludePath, StringComparison.Ordinal))
                        {
                            skipFile = true;
                        }
                    }
                    foreach (string excludePath in containsExcludeList)
                    {
                        if (trimmedPath.ToLower().Contains(excludePath))
                        {
                            skipFile = true;
                        }
                    }
                    if (skipFile)
                    {
                        continue;
                    }
                    string sha256sum = Common.CalculateSHA256Hash(filePath);

                    if (!modpackData.ContainsKey(trimmedPath))
                    {
                        if (DateTime.UtcNow.Ticks > nextHashTime)
                        {
                            nextHashTime = DateTime.UtcNow.Ticks + TimeSpan.TicksPerSecond;
                            DarkLog.Debug("Hashing: " + hashCount + "/" + modFiles.Length);
                        }
                        modpackData.Add(trimmedPath, sha256sum);
                    }
                    //Need to check because we may have a duplicate file in GameData
                    if (!objectData.ContainsKey(sha256sum))
                    {
                        objectData.Add(sha256sum, trimmedPath);
                    }
                }
                DarkLog.Debug("Hashed " + modFiles.Length + " files");
                using (StreamWriter sw = new StreamWriter(modpackServerCacheObjects))
                {
                    foreach (KeyValuePair<string, string> kvp in modpackData)
                    {
                        sw.WriteLine("{0}={1}", kvp.Key, kvp.Value);
                    }
                }
            }
        }

        public void HandleModDone()
        {
            modpackData.Clear();
            objectData.Clear(); 
            if (File.Exists(modpackServerCacheObjects))
            {
                File.Delete(modpackServerCacheObjects);
            }
            if (Settings.settingsStore.modpackMode == ModpackMode.GAMEDATA)
            {
                string[] modFiles = Directory.GetFiles(modpackPath, "*", SearchOption.AllDirectories);
                foreach (string filePath in modFiles)
                {
                    if (!filePath.ToLower().StartsWith(modpackPath.ToLower(), StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string trimmedPath = filePath.Substring(modpackPath.Length + 1).Replace('\\', '/');
                    bool skipFile = false;
                    foreach (string excludePath in excludeList)
                    {
                        if (trimmedPath.ToLower().StartsWith(excludePath, StringComparison.Ordinal))
                        {
                            skipFile = true;
                        }
                    }
                    foreach (string excludePath in containsExcludeList)
                    {
                        if (trimmedPath.ToLower().Contains(excludePath))
                        {
                            skipFile = true;
                        }
                    }
                    if (skipFile)
                    {
                        continue;
                    }
                    if (!clientData.ContainsKey(trimmedPath))
                    {
                        DarkLog.Normal("Deleting old GameData file: " + trimmedPath);
                        File.Delete(filePath);
                    }
                }
            }
            Server.Restart("Updating mod pack");
            DarkLog.Normal("Finished receiving mods, synced " + modpackData.Count + "/" + clientData.Count + "!");
        }

        public Dictionary<string, string> GetModListData()
        {
            Dictionary<string, string> retVal = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> kvp in modpackData)
            {
                retVal.Add(kvp.Key, kvp.Value);
            }
            return retVal;
        }

        public byte[] GetModObject(string sha256sum)
        {
            if (!objectData.ContainsKey(sha256sum))
            {
                return null;
            }
            string filePath = Path.Combine(modpackPath, objectData[sha256sum]);
            if (File.Exists(filePath))
            {
                return File.ReadAllBytes(filePath);
            }
            return null;
        }

        public bool SaveModObject(byte[] data, string sha256sum)
        {
            if (data == null)
            {
                data = new byte[0];
            }
            if (Common.CalculateSHA256Hash(data) != sha256sum)
            {
                return false;
            }
            string tryWrite = null;
            try
            {
                foreach (KeyValuePair<string, string> kvp in clientData)
                {
                    tryWrite = Path.Combine(modpackPath, kvp.Key);
                    if (kvp.Value == sha256sum)
                    {
                        clientReceived++;
                        new FileInfo(tryWrite).Directory.Create();
                        if (File.Exists(tryWrite))
                        {
                            File.Delete(tryWrite);
                        }
                        File.WriteAllBytes(tryWrite, data);
                    }
                }

            }
            catch (Exception e)
            {
                DarkLog.Error("Cannot write file " + tryWrite + ", error: " + e);
                return false;
            }
            return true;
        }

        public byte[] GetCKANData()
        {
            if (File.Exists(ckanPath))
            {
                return File.ReadAllBytes(ckanPath);
            }
            return null;
        }

        public void SaveCKANData(byte[] ckanData)
        {
            //Automatically overwrites
            File.WriteAllBytes(ckanPath, ckanData);
        }

        private class SendFileData
        {
            public string[] sha256sums;
            public int position = 0;

            public SendFileData(string[] sha256sums)
            {
                this.sha256sums = sha256sums;
            }
        }
    }
}