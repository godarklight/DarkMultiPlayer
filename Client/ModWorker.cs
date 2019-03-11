using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class ModWorker
    {
        public ModControlMode modControl = ModControlMode.ENABLED_STOP_INVALID_PART_SYNC;
        public bool dllListBuilt;
        //Dll files, built at startup
        private Dictionary<string, string> dllList;
        //Accessed from ModWindow
        private List<string> allowedParts;
        private string lastModFileData = "";
        //Services
        private ModWindow modWindow;

        public ModWorker(ModWindow modWindow)
        {
            this.modWindow = modWindow;
        }


        public string failText
        {
            private set;
            get;
        }

        private bool CheckFile(string relativeFileName, string referencefileHash)
        {
            string fullFileName = Path.Combine(Client.dmpClient.gameDataDir, relativeFileName);
            string fileHash = Common.CalculateSHA256Hash(fullFileName);
            if (fileHash != referencefileHash)
            {
                DarkLog.Debug(relativeFileName + " hash mismatch");
                return false;
            }
            return true;
        }

        public void BuildDllFileList()
        {
            dllList = new Dictionary<string, string>();
            string[] checkList = Directory.GetFiles(Client.dmpClient.gameDataDir, "*", SearchOption.AllDirectories);

            foreach (string checkFile in checkList)
            {
                //Only check DLL's
                if (checkFile.ToLower().EndsWith(".dll", StringComparison.CurrentCulture))
                {
                    //We want the relative path to check against, example: DarkMultiPlayer/Plugins/DarkMultiPlayer.dll
                    //Strip off everything from GameData
                    //Replace windows backslashes with mac/linux forward slashes.
                    //Make it lowercase so we don't worry about case sensitivity.
                    string relativeFilePath = checkFile.ToLowerInvariant().Substring(checkFile.ToLowerInvariant().IndexOf("gamedata", StringComparison.Ordinal) + 9).Replace('\\', '/');
                    string fileHash = Common.CalculateSHA256Hash(checkFile);
                    dllList.Add(relativeFilePath, fileHash);
                    DarkLog.Debug("Hashed file: " + relativeFilePath + ", hash: " + fileHash);
                }
            }
        }

        public bool ParseModFile(string modFileData)
        {
            if (modControl == ModControlMode.DISABLED)
            {
                return true;
            }
            bool modCheckOk = true;
            //Save mod file so we can recheck it.
            lastModFileData = modFileData;
            //Err...
            string tempModFilePath = Path.Combine(Client.dmpClient.dmpDataDir, "mod-control.txt");
            using (StreamWriter sw = new StreamWriter(tempModFilePath))
            {
                sw.WriteLine("#This file is downloaded from the server during connection. It is saved here for convenience.");
                sw.WriteLine(lastModFileData);
            }

            //Parse
            Dictionary<string, string> parseRequired = new Dictionary<string, string>();
            Dictionary<string, string> parseOptional = new Dictionary<string, string>();
            List<string> parseWhiteBlackList = new List<string>();
            List<string> parsePartsList = new List<string>();
            bool isWhiteList = false;
            string readMode = "";
            using (StringReader sr = new StringReader(modFileData))
            {
                while (true)
                {
                    string currentLine = sr.ReadLine();
                    if (currentLine == null)
                    {
                        //Done reading
                        break;
                    }
                    //Remove tabs/spaces from the start & end.
                    string trimmedLine = currentLine.Trim();
                    if (trimmedLine.StartsWith("#", StringComparison.Ordinal) || String.IsNullOrEmpty(trimmedLine))
                    {
                        //Skip comments or empty lines.
                        continue;
                    }
                    if (trimmedLine.StartsWith("!", StringComparison.Ordinal))
                    {
                        //New section
                        switch (trimmedLine.Substring(1))
                        {
                            case "required-files":
                            case "optional-files":
                            case "partslist":
                                readMode = trimmedLine.Substring(1);
                                break;
                            case "resource-blacklist":
                                readMode = trimmedLine.Substring(1);
                                isWhiteList = false;
                                break;
                            case "resource-whitelist":
                                readMode = trimmedLine.Substring(1);
                                isWhiteList = true;
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        switch (readMode)
                        {
                            case "required-files":
                                {
                                    string lowerFixedLine = trimmedLine.ToLowerInvariant().Replace('\\', '/');
                                    if (lowerFixedLine.Contains("="))
                                    {
                                        string[] splitLine = lowerFixedLine.Split('=');
                                        if (splitLine.Length == 2)
                                        {
                                            if (!parseRequired.ContainsKey(splitLine[0]))
                                            {
                                                parseRequired.Add(splitLine[0], splitLine[1].ToLowerInvariant());
                                            }
                                        }
                                        else
                                        {
                                            if (splitLine.Length == 1)
                                            {
                                                if (!parseRequired.ContainsKey(splitLine[0]))
                                                {
                                                    parseRequired.Add(splitLine[0], "");
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (!parseRequired.ContainsKey(lowerFixedLine))
                                        {
                                            parseRequired.Add(lowerFixedLine, "");
                                        }
                                    }
                                }
                                break;
                            case "optional-files":
                                {
                                    string lowerFixedLine = trimmedLine.ToLowerInvariant().Replace('\\', '/');
                                    if (lowerFixedLine.Contains("="))
                                    {
                                        string[] splitLine = lowerFixedLine.Split('=');
                                        if (splitLine.Length == 2)
                                        {
                                            if (!parseOptional.ContainsKey(splitLine[0]))
                                            {
                                                parseOptional.Add(splitLine[0], splitLine[1]);
                                            }
                                        }
                                        else
                                        {
                                            if (splitLine.Length == 1)
                                            {
                                                if (!parseOptional.ContainsKey(splitLine[0]))
                                                {
                                                    parseOptional.Add(splitLine[0], "");
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (!parseOptional.ContainsKey(lowerFixedLine))
                                        {
                                            parseOptional.Add(lowerFixedLine, "");
                                        }
                                    }
                                }
                                break;
                            case "resource-whitelist":
                            case "resource-blacklist":
                                {
                                    string lowerFixedLine = trimmedLine.ToLowerInvariant().Replace('\\', '/');
                                    //Resource is dll's only.
                                    if (lowerFixedLine.ToLowerInvariant().EndsWith(".dll", StringComparison.Ordinal))
                                    {
                                        if (parseWhiteBlackList.Contains(lowerFixedLine))
                                        {
                                            parseWhiteBlackList.Add(lowerFixedLine);
                                        }
                                    }
                                }
                                break;
                            case "partslist":
                                if (!parsePartsList.Contains(trimmedLine))
                                {
                                    parsePartsList.Add(trimmedLine);
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
            }

            string[] currentGameDataFiles = Directory.GetFiles(Client.dmpClient.gameDataDir, "*", SearchOption.AllDirectories);
            List<string> currentGameDataFilesNormal = new List<string>();
            List<string> currentGameDataFilesLower = new List<string>();
            foreach (string currentFile in currentGameDataFiles)
            {
                string relativeFilePath = currentFile.Substring(currentFile.ToLowerInvariant().IndexOf("gamedata", StringComparison.Ordinal) + 9).Replace('\\', '/');
                currentGameDataFilesNormal.Add(relativeFilePath);
                currentGameDataFilesLower.Add(relativeFilePath.ToLowerInvariant());
            }
            //Check
            StringBuilder sb = new StringBuilder();
            //Check Required
            foreach (KeyValuePair<string, string> requiredEntry in parseRequired)
            {
                if (!requiredEntry.Key.EndsWith("dll", StringComparison.Ordinal))
                {
                    //Protect against windows-style entries in mod-control.txt. Also use case insensitive matching.
                    if (!currentGameDataFilesLower.Contains(requiredEntry.Key))
                    {
                        modCheckOk = false;
                        DarkLog.Debug("Required file " + requiredEntry.Key + " is missing!");
                        sb.AppendLine("Required file " + requiredEntry.Key + " is missing!");
                        continue;
                    }
                    //If the entry has a SHA sum, we need to check it.
                    if (requiredEntry.Value != "")
                    {

                        string normalCaseFileName = currentGameDataFilesNormal[currentGameDataFilesLower.IndexOf(requiredEntry.Key)];
                        string fullFileName = Path.Combine(Client.dmpClient.gameDataDir, normalCaseFileName);
                        if (!CheckFile(fullFileName, requiredEntry.Value))
                        {
                            modCheckOk = false;
                            DarkLog.Debug("Required file " + requiredEntry.Key + " does not match hash " + requiredEntry.Value + "!");
                            sb.AppendLine("Required file " + requiredEntry.Key + " does not match hash " + requiredEntry.Value + "!");
                            continue;
                        }
                    }
                }
                else
                {
                    //DLL entries are cached from startup.
                    if (!dllList.ContainsKey(requiredEntry.Key))
                    {
                        modCheckOk = false;
                        DarkLog.Debug("Required file " + requiredEntry.Key + " is missing!");
                        sb.AppendLine("Required file " + requiredEntry.Key + " is missing!");
                        continue;
                    }
                    if (requiredEntry.Value != "")
                    {
                        if (dllList[requiredEntry.Key] != requiredEntry.Value)
                        {
                            modCheckOk = false;
                            DarkLog.Debug("Required file " + requiredEntry.Key + " does not match hash " + requiredEntry.Value + "!");
                            sb.AppendLine("Required file " + requiredEntry.Key + " does not match hash " + requiredEntry.Value + "!");
                            continue;
                        }
                    }
                }
            }
            //Check Optional
            foreach (KeyValuePair<string, string> optionalEntry in parseOptional)
            {
                if (!optionalEntry.Key.EndsWith("dll", StringComparison.Ordinal))
                {
                    //Protect against windows-style entries in mod-control.txt. Also use case insensitive matching.
                    if (!currentGameDataFilesLower.Contains(optionalEntry.Key))
                    {
                        //File is optional, nothing to check if it doesn't exist.
                        continue;
                    }
                    //If the entry has a SHA sum, we need to check it.
                    if (optionalEntry.Value != "")
                    {

                        string normalCaseFileName = currentGameDataFilesNormal[currentGameDataFilesLower.IndexOf(optionalEntry.Key)];
                        string fullFileName = Path.Combine(Client.dmpClient.gameDataDir, normalCaseFileName);
                        if (!CheckFile(fullFileName, optionalEntry.Value))
                        {
                            modCheckOk = false;
                            DarkLog.Debug("Optional file " + optionalEntry.Key + " does not match hash " + optionalEntry.Value + "!");
                            sb.AppendLine("Optional file " + optionalEntry.Key + " does not match hash " + optionalEntry.Value + "!");
                            continue;
                        }
                    }
                }
                else
                {
                    //DLL entries are cached from startup.
                    if (!dllList.ContainsKey(optionalEntry.Key))
                    {
                        //File is optional, nothing to check if it doesn't exist.
                        continue;
                    }
                    if (optionalEntry.Value != "")
                    {
                        if (dllList[optionalEntry.Key] != optionalEntry.Value)
                        {
                            modCheckOk = false;
                            DarkLog.Debug("Optional file " + optionalEntry.Key + " does not match hash " + optionalEntry.Value + "!");
                            sb.AppendLine("Optional file " + optionalEntry.Key + " does not match hash " + optionalEntry.Value + "!");
                            continue;
                        }
                    }
                }
            }
            if (isWhiteList)
            {
                //Check Resource whitelist
                List<string> autoAllowed = new List<string>();
                autoAllowed.Add("darkmultiplayer/plugins/darkmultiplayer.dll");
                autoAllowed.Add("darkmultiplayer/plugins/darkmultiplayer-common.dll");
                //Leave the old one there if the user forgets to delete it.
                autoAllowed.Add("darkmultiplayer/plugins/messagewriter.dll");
                autoAllowed.Add("darkmultiplayer/plugins/messagewriter2.dll");
                //Compression
                autoAllowed.Add("darkmultiplayer/plugins/icsharpcode.sharpziplib.dll");
                foreach (KeyValuePair<string, string> dllResource in dllList)
                {
                    //Allow DMP files
                    if (autoAllowed.Contains(dllResource.Key))
                    {
                        continue;
                    }
                    //Ignore squad plugins
                    if (dllResource.Key.StartsWith("squad/plugins", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    //Check required (Required implies whitelist)
                    if (parseRequired.ContainsKey(dllResource.Key))
                    {
                        continue;
                    }
                    //Check optional (Optional implies whitelist)
                    if (parseOptional.ContainsKey(dllResource.Key))
                    {
                        continue;
                    }
                    //Check whitelist
                    if (parseWhiteBlackList.Contains(dllResource.Key))
                    {
                        continue;
                    }
                    modCheckOk = false;
                    DarkLog.Debug("Non-whitelisted resource " + dllResource.Key + " exists on client!");
                    sb.AppendLine("Non-whitelisted resource " + dllResource.Key + " exists on client!");
                }
            }
            else
            {
                //Check Resource blacklist
                foreach (string blacklistEntry in parseWhiteBlackList)
                {
                    if (dllList.ContainsKey(blacklistEntry.ToLowerInvariant()))
                    {
                        modCheckOk = false;
                        DarkLog.Debug("Banned resource " + blacklistEntry + " exists on client!");
                        sb.AppendLine("Banned resource " + blacklistEntry + " exists on client!");
                    }
                }
            }
            if (!modCheckOk)
            {
                failText = sb.ToString();
                modWindow.display = true;
                return false;
            }
            allowedParts = parsePartsList;
            DarkLog.Debug("Mod check passed!");
            return true;
        }

        public List<string> GetAllowedPartsList()
        {
            //Return a copy
            if (modControl == ModControlMode.DISABLED)
            {
                return null;
            }
            return new List<string>(allowedParts);
        }

        public void GenerateModControlFile(bool whitelistMode)
        {
            string gameDataDir = Client.dmpClient.gameDataDir;
            string[] topLevelFiles = Directory.GetFiles(gameDataDir);
            string[] modDirectories = Directory.GetDirectories(gameDataDir);

            List<string> requiredFiles = new List<string>();
            List<string> optionalFiles = new List<string>();
            List<string> partsList = Common.GetStockParts();
            //If whitelisting, add top level dll's to required (It's usually things like modulemanager)
            foreach (string dllFile in topLevelFiles)
            {
                if (Path.GetExtension(dllFile).ToLower() == ".dll")
                {
                    requiredFiles.Add(Path.GetFileName(dllFile));
                }
            }

            foreach (string modDirectory in modDirectories)
            {
                string lowerDirectoryName = modDirectory.Substring(modDirectory.ToLower().IndexOf("gamedata", StringComparison.Ordinal) + 9).ToLower();
                if (lowerDirectoryName.StartsWith("squad", StringComparison.Ordinal))
                {
                    continue;
                }
                if (lowerDirectoryName.StartsWith("nasamission", StringComparison.Ordinal))
                {
                    continue;
                }
                if (lowerDirectoryName.StartsWith("darkmultiplayer", StringComparison.Ordinal))
                {
                    continue;
                }
                bool modIsRequired = false;
                string[] partFiles = Directory.GetFiles(Path.Combine(gameDataDir, modDirectory), "*", SearchOption.AllDirectories);
                List<string> modDllFiles = new List<string>();
                List<string> modPartCfgFiles = new List<string>();
                foreach (string partFile in partFiles)
                {
                    bool fileIsPartFile = false;
                    string relativeFileName = partFile.Substring(partFile.ToLower().IndexOf("gamedata", StringComparison.Ordinal) + 9).Replace(@"\", "/");
                    if (Path.GetExtension(partFile).ToLower() == ".cfg")
                    {
                        ConfigNode cn = ConfigNode.Load(partFile);
                        if (cn == null)
                        {
                            continue;
                        }
                        foreach (ConfigNode partNode in cn.GetNodes("PART"))
                        {
                            string partName = partNode.GetValue("name");
                            if (partName != null)
                            {
                                DarkLog.Debug("Part detected in " + relativeFileName + " , name: " + partName);
                                partName = partName.Replace('_', '.');
                                modIsRequired = true;
                                fileIsPartFile = true;
                                partsList.Add(partName);
                            }
                        }

                    }
                    if (fileIsPartFile)
                    {
                        modPartCfgFiles.Add(relativeFileName);
                    }
                    if (Path.GetExtension(partFile).ToLower() == ".dll")
                    {
                        modDllFiles.Add(relativeFileName);
                    }
                }

                if (modIsRequired)
                {
                    if (modDllFiles.Count > 0)
                    {
                        //If the mod as a plugin, just require that. It's clear enough.
                        requiredFiles.AddRange(modDllFiles);
                    }
                    else
                    {
                        //If the mod does *not* have a plugin (Scoop-o-matic is an example), add the part files to required instead.
                        requiredFiles.AddRange(modPartCfgFiles);
                    }
                }
                else
                {
                    if (whitelistMode)
                    {
                        optionalFiles.AddRange(modDllFiles);
                    }
                }
            }
            string modFileData = Common.GenerateModFileStringData(requiredFiles.ToArray(), optionalFiles.ToArray(), whitelistMode, new string[0], partsList.ToArray());
            string saveModFile = Path.Combine(Client.dmpClient.kspRootPath, "mod-control.txt");
            using (StreamWriter sw = new StreamWriter(saveModFile, false))
            {
                sw.Write(modFileData);
            }
            ScreenMessages.PostScreenMessage("mod-control.txt file generated in your KSP folder\nMove it to DMPServer/Config/", 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void CheckCommonStockParts()
        {
            int totalParts = 0;
            int missingParts = 0;
            List<string> stockParts = Common.GetStockParts();
            DarkLog.Debug("Missing parts start");
            foreach (AvailablePart part in PartLoader.LoadedPartsList)
            {
                totalParts++;
                if (!stockParts.Contains(part.name))
                {
                    missingParts++;
                    DarkLog.Debug("Missing '" + part.name + "'");
                }
            }
            DarkLog.Debug("Missing parts end");
            if (missingParts != 0)
            {
                ScreenMessages.PostScreenMessage(missingParts + " missing part(s) from Common.dll printed to debug log (" + totalParts + " total)", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
            else
            {
                ScreenMessages.PostScreenMessage("No missing parts out of from Common.dll (" + totalParts + " total)", 5f, ScreenMessageStyle.UPPER_CENTER);
            }
        }
    }
}

