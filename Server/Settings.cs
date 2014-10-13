using System;
using System.IO;
using DarkMultiPlayerCommon;
using System.Reflection;
using System.Collections.Generic;

namespace DarkMultiPlayerServer
{
    public class Settings
    {
        private const string SETTINGS_FILE_NAME = "DMPServerSettings.txt";
        public static string serverPath = AppDomain.CurrentDomain.BaseDirectory;
        private static string settingsFile = Path.Combine(serverPath, SETTINGS_FILE_NAME);
        //Port
        public static SettingsStore settingsStore = new SettingsStore();

        public static void Load()
        {
            FieldInfo[] settingFields = typeof(SettingsStore).GetFields();
            if (!File.Exists(Path.Combine(serverPath, SETTINGS_FILE_NAME)))
            {
                Save();
            }
			using (var fs = new FileStream(settingsFile, FileMode.Open))
            {
				using (var sr = new StreamReader(fs))
                {
                    string currentLine;
                    string trimmedLine;
                    string currentKey;
                    string currentValue;
                    while (true)
                    {
                        currentLine = sr.ReadLine();
                        if (currentLine == null)
                        {
                            break;
                        }
                        trimmedLine = currentLine.Trim();
                        if (!String.IsNullOrEmpty(trimmedLine))
                        {
                            if (trimmedLine.Contains(",") && !trimmedLine.StartsWith("#"))
                            {
                                currentKey = trimmedLine.Substring(0, trimmedLine.IndexOf(","));
                                currentValue = trimmedLine.Substring(trimmedLine.IndexOf(",") + 1);

                                foreach (FieldInfo settingField in settingFields)
                                {
                                    if (settingField.Name.ToLower() == currentKey)
                                    {
                                        if (settingField.FieldType == typeof(string))
                                        {
                                            settingField.SetValue(settingsStore, currentValue);
                                        }
                                        if (settingField.FieldType == typeof(int))
                                        {
                                            int intValue = Int32.Parse(currentValue);
                                            settingField.SetValue(settingsStore, (int)intValue);
                                        }
                                        if (settingField.FieldType == typeof(double))
                                        {
                                            double doubleValue = Double.Parse(currentValue);
                                            settingField.SetValue(settingsStore, (double)doubleValue);
                                        }
                                        if (settingField.FieldType == typeof(bool)) {
                                            if (currentValue == "1")
                                            {
                                                settingField.SetValue(settingsStore, true);
                                            }
                                            else
                                            {
                                                settingField.SetValue(settingsStore, false);
                                            }
                                        }
                                        if (settingField.FieldType.IsEnum)
                                        {
                                            int intValue = Int32.Parse(currentValue);
                                            Array enumValues = settingField.FieldType.GetEnumValues();
                                            if (intValue <= enumValues.Length)
                                            {
                                                settingField.SetValue(settingsStore, enumValues.GetValue(intValue));
                                            }
                                        }
                                        DarkLog.Debug(settingField.Name + ": " + currentValue);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Save();
        }

        public static void Save()
        {
            FieldInfo[] settingFields = typeof(SettingsStore).GetFields();
            Dictionary<string,string> settingDescriptions = GetSettingsDescriptions();
            if (File.Exists(settingsFile + ".tmp"))
            {
                File.Delete(settingsFile + ".tmp");
            }
			using (var fs = new FileStream(settingsFile + ".tmp", FileMode.CreateNew))
            {
				using (var sw = new StreamWriter(fs))
                {
                    sw.WriteLine("#Setting file format: (key),(value)");
                    sw.WriteLine("#This file will be re-written every time the server is started. Only known keys will be saved.");
                    sw.WriteLine("");
                    foreach (FieldInfo settingField in settingFields)
                    {
                        if (settingDescriptions.ContainsKey(settingField.Name))
                        {
                            sw.WriteLine("#" + settingField.Name.ToLower() + " - " + settingDescriptions[settingField.Name]);
                        }
                        if (settingField.FieldType == typeof(string) || settingField.FieldType == typeof(int) || settingField.FieldType == typeof(double))
                        {
                            sw.WriteLine(settingField.Name.ToLower() + "," + settingField.GetValue(settingsStore));
                        }
                        if (settingField.FieldType == typeof(bool)) {
                            if ((bool)settingField.GetValue(settingsStore))
                            {
                                sw.WriteLine(settingField.Name.ToLower() + ",1");
                            }
                            else
                            {
                                sw.WriteLine(settingField.Name.ToLower() + ",0");
                            }
                        }
                        if (settingField.FieldType.IsEnum)
                        {
                            sw.WriteLine("#Valid values are:");
                            foreach (int enumValue in settingField.FieldType.GetEnumValues())
                            {
                                sw.WriteLine("#" + enumValue + " - " + settingField.FieldType.GetEnumValues().GetValue(enumValue).ToString());
                            }
                            sw.WriteLine(settingField.Name.ToLower() + "," + (int)settingField.GetValue(settingsStore));
                        }
                        sw.WriteLine("");
                    }
                }
            }
            if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
            }
            File.Move(Path.Combine(serverPath, SETTINGS_FILE_NAME + ".tmp"), Path.Combine(serverPath, SETTINGS_FILE_NAME));
        }

        private static Dictionary<string,string> GetSettingsDescriptions()
		{
			return new Dictionary<string, string> () { 
				{"address", "The address the server listens on\n#WARNING: You do not need to change this unless you are running 2 servers on the same port.\n#Changing this setting from 0.0.0.0 will only give you trouble if you aren't running multiple servers."},
	            {"port", "The port the server listens on"},
	            {"warpMode", "Specify the warp type"},
	            {"gameMode", "Specify the game type"},
	            {"whitelisted", "Enable whitelisting"},
	            {"modControl", "Enable mod control\n#WARNING: Only consider turning off mod control for private servers.\n#The game will constantly complain about missing parts if there are missing mods."},
	            {"keepTickingWhileOffline", "Specify if the the server universe 'ticks' while nobody is connected or the server is shutdown"},
	            {"sendPlayerToLatestSubspace", "If true, sends the player to the latest subspace upon connecting. If false, sends the player to the previous subspace they were in.\n#NB: This may cause time-paradoxes, and will not work across server restarts"},
	            {"useUTCTimeInLog", "Use UTC instead of system time in the log."},
	            {"logLevel", "Minimum log level."},
	            {"screenshotsPerPlayer", "Specify maximum number of screenshots to save per player. -1 = None, 0 = Unlimited"},
	            {"screenshotHeight", "Specify vertical resolution of screenshots."},
	            {"cheats", "Enable use of cheats ingame."},
	            {"httpPort", "HTTP port for server status. 0 = Disabled"},
	            {"serverName", "Name of the server."},
	            {"maxPlayers", "Maximum amount of players that can join the server."},
	            {"screenshotDirectory", "Specify a custom screenshot directory.\n#This directory must exist in order to be used. Leave blank to store it in Universe."},
	            {"autoNuke", "Specify in minutes how often /nukeksc automatically runs. 0 = Disabled"},
	            {"autoDekessler", "Specify in minutes how often /dekessler automatically runs. 0 = Disabled"},
	            {"numberOfAsteroids", "How many untracked asteroids to spawn into the universe. 0 = Disabled"},
	            {"consoleIdentifier", "Specify the name that will appear when you send a message using the server's console."},
				{"serverMotd", "Specify the server's MOTD (message of the day)."},
				{"expireScreenshots", "Specify the amount of days a screenshot should be considered as expired and deleted. 0 = Disabled"}
			};
		}
    }

    public class SettingsStore
    {
        public string address = "0.0.0.0";
        public int port = 6702;
        public WarpMode warpMode = WarpMode.SUBSPACE;
        public GameMode gameMode = GameMode.SANDBOX;
        public bool whitelisted = false;
        public ModControlMode modControl = ModControlMode.ENABLED_STOP_INVALID_PART_SYNC;
        public bool keepTickingWhileOffline = true;
        public bool sendPlayerToLatestSubspace = true;
        public bool useUTCTimeInLog = false;
        public DarkLog.LogLevels logLevel = DarkLog.LogLevels.DEBUG;
        public int screenshotsPerPlayer = 20;
        public int screenshotHeight = 720;
        public bool cheats = true;
        public int httpPort = 0;
        public string serverName = "DMP Server";
        public int maxPlayers = 20;
        public string screenshotDirectory = "";
        public int autoNuke = 0;
        public int autoDekessler = 30;
        public int numberOfAsteroids = 30;
        public string consoleIdentifier = "Server";
        public string serverMotd = "Welcome, %name%!";
        public double expireScreenshots = 0;
    }
}