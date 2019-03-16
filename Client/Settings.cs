using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class Settings
    {
        //Settings
        public string playerName;
        public string playerPublicKey;
        public string playerPrivateKey;
        public int cacheSize;
        public int disclaimerAccepted;
        public List<ServerEntry> servers = new List<ServerEntry>();
        public Color playerColor;
        public KeyCode screenshotKey;
        public KeyCode chatKey;
        public string selectedFlag;
        public bool compressionEnabled;
        public bool revertEnabled;
        public bool interframeEnabled;
        public DMPToolbarType toolbarType;
        public InterpolatorType interpolatorType;
        private const string DEFAULT_PLAYER_NAME = "Player";
        private const string OLD_SETTINGS_FILE = "servers.xml";
        private const string SETTINGS_FILE = "settings.cfg";
        private const string PUBLIC_KEY_FILE = "publickey.txt";
        private const string PRIVATE_KEY_FILE = "privatekey.txt";
        private const int DEFAULT_CACHE_SIZE = 100;
        private string dataLocation;
        private string oldSettingsFile;
        private string settingsFile;
        private string backupOldSettingsFile;
        private string backupSettingsFile;
        private string publicKeyFile;
        private string privateKeyFile;
        private string backupPublicKeyFile;
        private string backupPrivateKeyFile;

        public Settings()
        {
            string darkMultiPlayerDataDirectory = Client.dmpClient.dmpDataDir;
            if (!Directory.Exists(darkMultiPlayerDataDirectory))
            {
                Directory.CreateDirectory(darkMultiPlayerDataDirectory);
            }
            string darkMultiPlayerSavesDirectory = Path.Combine(Path.Combine(Client.dmpClient.kspRootPath, "saves"), "DarkMultiPlayer");
            if (!Directory.Exists(darkMultiPlayerSavesDirectory))
            {
                Directory.CreateDirectory(darkMultiPlayerSavesDirectory);
            }
            dataLocation = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Data");
            oldSettingsFile = Path.Combine(dataLocation, OLD_SETTINGS_FILE);
            settingsFile = Path.Combine(dataLocation, SETTINGS_FILE);
            backupOldSettingsFile = Path.Combine(darkMultiPlayerSavesDirectory, OLD_SETTINGS_FILE);
            backupSettingsFile = Path.Combine(darkMultiPlayerSavesDirectory, SETTINGS_FILE);
            publicKeyFile = Path.Combine(dataLocation, PUBLIC_KEY_FILE);
            backupPublicKeyFile = Path.Combine(darkMultiPlayerSavesDirectory, PUBLIC_KEY_FILE);
            privateKeyFile = Path.Combine(dataLocation, PRIVATE_KEY_FILE);
            backupPrivateKeyFile = Path.Combine(darkMultiPlayerSavesDirectory, PRIVATE_KEY_FILE);
            LoadSettings();
        }

        public void LoadOldSettings()
        {
            //Read XML settings
            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                if (File.Exists(backupOldSettingsFile) && !File.Exists(oldSettingsFile))
                {
                    DarkLog.Debug("[Settings]: Restoring old player settings file!");
                    File.Move(backupOldSettingsFile, oldSettingsFile);
                }
                xmlDoc.Load(oldSettingsFile);
                playerName = xmlDoc.SelectSingleNode("/settings/global/@username").Value;
                cacheSize = int.Parse(xmlDoc.SelectSingleNode("/settings/global/@cache-size").Value);
                disclaimerAccepted = Int32.Parse(xmlDoc.SelectSingleNode("/settings/global/@disclaimer").Value);

                string floatArrayString = xmlDoc.SelectSingleNode("/settings/global/@player-color").Value;
                string[] floatArrayStringSplit = floatArrayString.Split(',');
                float redColor = float.Parse(floatArrayStringSplit[0].Trim());
                float greenColor = float.Parse(floatArrayStringSplit[1].Trim());
                float blueColor = float.Parse(floatArrayStringSplit[2].Trim());
                //Bounds checking - Gotta check up on those players :)
                if (redColor < 0f)
                {
                    redColor = 0f;
                }
                if (redColor > 1f)
                {
                    redColor = 1f;
                }
                if (greenColor < 0f)
                {
                    greenColor = 0f;
                }
                if (greenColor > 1f)
                {
                    greenColor = 1f;
                }
                if (blueColor < 0f)
                {
                    blueColor = 0f;
                }
                if (blueColor > 1f)
                {
                    blueColor = 1f;
                }
                playerColor = new Color(redColor, greenColor, blueColor, 1f);

                chatKey = (KeyCode)Int32.Parse(xmlDoc.SelectSingleNode("/settings/global/@chat-key").Value);
                screenshotKey = (KeyCode)Int32.Parse(xmlDoc.SelectSingleNode("/settings/global/@screenshot-key").Value);
                selectedFlag = xmlDoc.SelectSingleNode("/settings/global/@selected-flag").Value;
                compressionEnabled = Boolean.Parse(xmlDoc.SelectSingleNode("/settings/global/@compression").Value);
                revertEnabled = Boolean.Parse(xmlDoc.SelectSingleNode("/settings/global/@revert").Value);
                toolbarType = (DMPToolbarType)Int32.Parse(xmlDoc.SelectSingleNode("/settings/global/@toolbar").Value);
                XmlNodeList serverNodeList = xmlDoc.GetElementsByTagName("server");
                servers.Clear();
                foreach (XmlNode xmlNode in serverNodeList)
                {
                    ServerEntry newServer = new ServerEntry();
                    newServer.name = xmlNode.Attributes["name"].Value;
                    newServer.address = xmlNode.Attributes["address"].Value;
                    Int32.TryParse(xmlNode.Attributes["port"].Value, out newServer.port);
                    servers.Add(newServer);
                }

                SaveSettings();
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error loading old settings: " + e);
            }
        }

        private void GenerateNewKeypair()
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(1024))
            {
                try
                {
                    playerPublicKey = rsa.ToXmlString(false);
                    playerPrivateKey = rsa.ToXmlString(true);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error: " + e);
                }
                finally
                {
                    //Don't save the key in the machine store.
                    rsa.PersistKeyInCsp = false;
                }
            }
            File.WriteAllText(publicKeyFile, playerPublicKey);
            File.WriteAllText(privateKeyFile, playerPrivateKey);
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(backupOldSettingsFile) || File.Exists(oldSettingsFile))
                {
                    DarkLog.Debug("[Settings]: Loading old settings");
                    LoadOldSettings();
                    SaveSettings();
                    File.Delete(backupOldSettingsFile);
                    File.Delete(oldSettingsFile);
                }

                bool saveAfterLoad = false;
                ConfigNode mainNode = new ConfigNode();

                if (File.Exists(backupSettingsFile) && !File.Exists(settingsFile))
                {
                    DarkLog.Debug("[Settings]: Restoring backup file");
                    File.Copy(backupSettingsFile, settingsFile);
                }

                if (!File.Exists(settingsFile))
                {
                    mainNode = GetDefaultSettings();
                    playerName = DEFAULT_PLAYER_NAME;
                    mainNode.Save(settingsFile);
                }

                if (!File.Exists(backupSettingsFile))
                {
                    DarkLog.Debug("[Settings]: Backing up settings");
                    File.Copy(settingsFile, backupSettingsFile);
                }

                mainNode = ConfigNode.Load(settingsFile);

                ConfigNode settingsNode = mainNode.GetNode("SETTINGS");
                ConfigNode playerNode = settingsNode.GetNode("PLAYER");
                ConfigNode bindingsNode = settingsNode.GetNode("KEYBINDINGS");

                playerName = playerNode.GetValue("name");

                if (!int.TryParse(settingsNode.GetValue("cacheSize"), out cacheSize))
                {
                    DarkLog.Debug("[Settings]: Adding cache size to settings file");
                    cacheSize = DEFAULT_CACHE_SIZE;
                    saveAfterLoad = true;
                }

                if (!int.TryParse(settingsNode.GetValue("disclaimer"), out disclaimerAccepted))
                {
                    DarkLog.Debug("[Settings]: Adding disclaimer to settings file");
                    disclaimerAccepted = 0;
                    saveAfterLoad = true;
                }

                if (!playerNode.TryGetValue("color", ref playerColor))
                {
                    DarkLog.Debug("[Settings]: Adding color to settings file");
                    playerColor = PlayerColorWorker.GenerateRandomColor();
                    saveAfterLoad = true;
                }

                int chatKey = (int)KeyCode.BackQuote, screenshotKey = (int)KeyCode.F8;
                if (!int.TryParse(bindingsNode.GetValue("chat"), out chatKey))
                {
                    DarkLog.Debug("[Settings]: Adding chat key to settings file");
                    this.chatKey = KeyCode.BackQuote;
                    saveAfterLoad = true;
                }
                else
                {
                    this.chatKey = (KeyCode)chatKey;
                }

                if (!int.TryParse(bindingsNode.GetValue("screenshot"), out screenshotKey))
                {
                    DarkLog.Debug("[Settings]: Adding screenshot key to settings file");
                    this.screenshotKey = KeyCode.F8;
                    saveAfterLoad = true;
                }
                else
                {
                    this.screenshotKey = (KeyCode)screenshotKey;
                }

                if (!playerNode.TryGetValue("flag", ref selectedFlag))
                {
                    DarkLog.Debug("[Settings]: Adding selected flag to settings file");
                    selectedFlag = "Squad/Flags/default";
                    saveAfterLoad = true;
                }

                if (!settingsNode.TryGetValue("compression", ref compressionEnabled))
                {
                    DarkLog.Debug("[Settings]: Adding compression flag to settings file");
                    compressionEnabled = true;
                    saveAfterLoad = true;
                }

                if (!settingsNode.TryGetValue("revert", ref revertEnabled))
                {
                    DarkLog.Debug("[Settings]: Adding revert flag to settings file");
                    revertEnabled = true;
                    saveAfterLoad = true;
                }

                string interpolatorString = null;
                int interpolatorInt = 0;
                try
                {
                    //Make sure we haven't saved to the old int type
                    if (settingsNode.TryGetValue("interpolation", ref interpolatorString) && !int.TryParse(interpolatorString, out interpolatorInt))
                    {
                        interpolatorType = (InterpolatorType)Enum.Parse(typeof(InterpolatorType), interpolatorString);
                    }
                    else
                    {
                        DarkLog.Debug("[Settings]: Adding interpolation flag to settings file");
                        interpolatorType = InterpolatorType.INTERPOLATE1S;
                        saveAfterLoad = true;
                    }
                }
                catch
                {
                    interpolatorType = InterpolatorType.INTERPOLATE1S;
                    saveAfterLoad = true;
                }

                if (!settingsNode.TryGetValue("posLoadedVessels", ref interframeEnabled))
                {
                    DarkLog.Debug("[Settings]: Adding interframe flag to settings file");
                    interframeEnabled = true;
                    saveAfterLoad = true;
                }

                int toolbarType;
                if (!int.TryParse(settingsNode.GetValue("toolbar"), out toolbarType))
                {
                    DarkLog.Debug("[Settings]: Adding toolbar flag to settings file");
                    this.toolbarType = DMPToolbarType.BLIZZY_IF_INSTALLED;
                    saveAfterLoad = true;
                }
                else
                {
                    this.toolbarType = (DMPToolbarType)toolbarType;
                }

                ConfigNode serversNode = settingsNode.GetNode("SERVERS");
                servers.Clear();
                if (serversNode.HasNode("SERVER"))
                {
                    foreach (ConfigNode serverNode in serversNode.GetNodes("SERVER"))
                    {
                        ServerEntry newServer = new ServerEntry();
                        newServer.name = serverNode.GetValue("name");
                        newServer.address = serverNode.GetValue("address");
                        serverNode.TryGetValue("port", ref newServer.port);
                        servers.Add(newServer);
                    }
                }

                if (saveAfterLoad) SaveSettings();
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error while loading settings:");
                DarkLog.Debug(e.ToString());
            }

            //Read player token
            try
            {
                //Restore backup if needed
                if (File.Exists(backupPublicKeyFile) && File.Exists(backupPrivateKeyFile) && (!File.Exists(publicKeyFile) || !File.Exists(privateKeyFile)))
                {
                    DarkLog.Debug("[Settings]: Restoring backed up keypair!");
                    File.Copy(backupPublicKeyFile, publicKeyFile, true);
                    File.Copy(backupPrivateKeyFile, privateKeyFile, true);
                }
                //Load or create token file
                if (File.Exists(privateKeyFile) && File.Exists(publicKeyFile))
                {
                    playerPublicKey = File.ReadAllText(publicKeyFile);
                    playerPrivateKey = File.ReadAllText(privateKeyFile);
                }
                else
                {
                    DarkLog.Debug("[Settings]: Creating new keypair!");
                    GenerateNewKeypair();
                }
                //Save backup token file if needed
                if (!File.Exists(backupPublicKeyFile) || !File.Exists(backupPrivateKeyFile))
                {
                    DarkLog.Debug("[Settings]: Backing up keypair");
                    File.Copy(publicKeyFile, backupPublicKeyFile, true);
                    File.Copy(privateKeyFile, backupPrivateKeyFile, true);
                }
            }
            catch
            {
                DarkLog.Debug("Error processing keypair, creating new keypair");
                GenerateNewKeypair();
                DarkLog.Debug("[Settings]: Backing up keypair");
                File.Copy(publicKeyFile, backupPublicKeyFile, true);
                File.Copy(privateKeyFile, backupPrivateKeyFile, true);
            }
        }

        public void SaveSettings()
        {
            ConfigNode mainNode = new ConfigNode();
            ConfigNode settingsNode = mainNode.AddNode("SETTINGS");
            ConfigNode playerNode = settingsNode.AddNode("PLAYER");

            playerNode.SetValue("name", playerName, true);
            playerNode.SetValue("color", playerColor, true);
            playerNode.SetValue("flag", selectedFlag, true);

            ConfigNode bindingsNode = settingsNode.AddNode("KEYBINDINGS");
            bindingsNode.SetValue("chat", (int)chatKey, true);
            bindingsNode.SetValue("screenshot", (int)screenshotKey, true);

            settingsNode.SetValue("cacheSize", cacheSize, true);
            settingsNode.SetValue("disclaimer", disclaimerAccepted, true);
            settingsNode.SetValue("compression", compressionEnabled, true);
            settingsNode.SetValue("revert", revertEnabled, true);
            settingsNode.SetValue("interpolation", interpolatorType.ToString(), true);
            settingsNode.SetValue("posLoadedVessels", interframeEnabled, true);
            settingsNode.SetValue("toolbar", (int)toolbarType, true);

            ConfigNode serversNode = settingsNode.AddNode("SERVERS");
            serversNode.ClearNodes();
            foreach (ServerEntry server in servers)
            {
                ConfigNode serverNode = serversNode.AddNode("SERVER");
                serverNode.AddValue("name", server.name);
                serverNode.AddValue("address", server.address);
                serverNode.AddValue("port", server.port);
            }
            mainNode.Save(settingsFile);

            File.Copy(settingsFile, backupSettingsFile, true);
        }

        public ConfigNode GetDefaultSettings()
        {
            ConfigNode mainNode = new ConfigNode();
            ConfigNode settingsNode = new ConfigNode("SETTINGS");
            settingsNode.AddValue("cacheSize", DEFAULT_CACHE_SIZE);

            ConfigNode playerNode = new ConfigNode("PLAYER");
            playerNode.AddValue("name", DEFAULT_PLAYER_NAME);

            ConfigNode bindingsNode = new ConfigNode("KEYBINDINGS");

            ConfigNode serversNode = new ConfigNode("SERVERS");

            settingsNode.AddNode(playerNode);
            settingsNode.AddNode(bindingsNode);
            settingsNode.AddNode(serversNode);

            mainNode.AddNode(settingsNode);
            return mainNode;
        }
    }

    public class ServerEntry
    {
        public string name;
        public string address;
        public int port;
    }

    public enum InterpolatorType
    {
        EXTRAPOLATE_FULL,
        EXTRAPOLATE_NO_ROT,
        INTERPOLATE1S,
        INTERPOLATE3S
    }
}

