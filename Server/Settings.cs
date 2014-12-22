using System;
using System.IO;
using System.Net;
using DarkMultiPlayerCommon;
using System.Reflection;
using System.Collections.Generic;

namespace DarkMultiPlayerServer
{
    using System.ComponentModel;
    using System.Linq;

    public class Settings
    {
        private const string SETTINGS_FILE_NAME = "DMPServerSettings.txt";
        private static string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SETTINGS_FILE_NAME);
        //Port
        private static SettingsStore _settingsStore;

        public static SettingsStore settingsStore
        {
            get
            {
                //Lazy loading
                if (_settingsStore == null)
                {
                    _settingsStore = new SettingsStore();
                    Load();
                }
                return _settingsStore;
            }
        }

        public static void Reset()
        {
            _settingsStore = null;
        }

        private static void Load()
        {
            DarkLog.Debug("Loading settings");

            var settingFields = typeof(SettingsStore).GetProperties();
            if (!File.Exists(settingsFile))
            {
                try
                {
                    if (System.Net.Sockets.Socket.OSSupportsIPv6)
                    {
                        // Default to listening on IPv4 and IPv6 if possible.
                        settingsStore.address = "::";
                    }
                }
                catch
                {
                    // May throw on Windows XP
                }

                Save();
            }

            using (var fs = new FileStream(settingsFile, FileMode.Open))
            {
                using (var sr = new StreamReader(fs))
                {
                    while (!sr.EndOfStream)
                    {
                        var currentLine = sr.ReadLine();
                        if (currentLine == null)
                        {
                            break;
                        }

                        var trimmedLine = currentLine.Trim();
                        if (string.IsNullOrEmpty(trimmedLine))
                        {
                            continue;
                        }

                        if (!trimmedLine.Contains(",") || trimmedLine.StartsWith("#"))
                        {
                            continue;
                        }

                        var currentKey = trimmedLine.Substring(0, trimmedLine.IndexOf(",", System.StringComparison.Ordinal));
                        var currentValue = trimmedLine.Substring(trimmedLine.IndexOf(",", System.StringComparison.Ordinal) + 1);

                        foreach (var settingField in settingFields)
                        {
                            if (settingField.Name.ToLower() != currentKey)
                            {
                                continue;
                            }

                            if (settingField.PropertyType == typeof(string))
                            {
                                settingField.SetValue(settingsStore, currentValue, null);
                            }

                            if (settingField.PropertyType == typeof(int))
                            {
                                settingField.SetValue(settingsStore, int.Parse(currentValue), null);
                            }

                            if (settingField.PropertyType == typeof(double))
                            {
                                var doubleValue = double.Parse(currentValue);
                                settingField.SetValue(settingsStore, doubleValue, null);
                            }

                            if (settingField.PropertyType == typeof(bool))
                            {
                                settingField.SetValue(settingsStore, currentValue == "1", null);
                            }

                            if (!settingField.PropertyType.IsEnum)
                            {
                                continue;
                            }

                            var intValue = int.Parse(currentValue);
                            var enumValues = settingField.PropertyType.GetEnumValues();
                            if (intValue <= enumValues.Length)
                            {
                                settingField.SetValue(settingsStore, enumValues.GetValue(intValue), null);
                            }

                            //DarkLog.Debug(settingField.Name + ": " + currentValue);
                        }
                    }
                }
            }

            Save();
        }

        private static void Save()
        {
            DarkLog.Debug("Saving settings");
            var settingFields = typeof(SettingsStore).GetProperties();
            if (File.Exists(settingsFile + ".tmp"))
            {
                File.Delete(settingsFile + ".tmp");
            }

            using (var fileStream = new FileStream(settingsFile + ".tmp", FileMode.CreateNew))
            {
                using (var streamWriter = new StreamWriter(fileStream))
                {
                    streamWriter.WriteLine("#Setting file format: (key),(value)");
                    streamWriter.WriteLine("#This file will be re-written every time the server is started. Only known keys will be saved.");
                    streamWriter.WriteLine(string.Empty);

                    foreach (var settingField in settingFields)
                    {
                        var descriptionAttribute = settingField.GetCustomAttributes(typeof(DescriptionAttribute), true).FirstOrDefault();
                        var description = descriptionAttribute != null ? ((DescriptionAttribute)descriptionAttribute).Description : null;

                        if (!string.IsNullOrEmpty(description))
                        {
                            streamWriter.WriteLine("#" + settingField.Name.ToLower() + " - " + description);
                        }

                        if (settingField.PropertyType == typeof(string) || settingField.PropertyType == typeof(int) || settingField.PropertyType == typeof(double))
                        {
                            streamWriter.WriteLine(settingField.Name.ToLower() + "," + settingField.GetValue(settingsStore, null));
                        }

                        if (settingField.PropertyType == typeof(bool))
                        {
                            if ((bool)settingField.GetValue(settingsStore, null))
                            {
                                streamWriter.WriteLine(settingField.Name.ToLower() + ",1");
                            }
                            else
                            {
                                streamWriter.WriteLine(settingField.Name.ToLower() + ",0");
                            }
                        }

                        if (settingField.PropertyType.IsEnum)
                        {
                            streamWriter.WriteLine("#Valid values are:");
                            foreach (int enumValue in settingField.PropertyType.GetEnumValues())
                            {
                                streamWriter.WriteLine("#" + enumValue + " - " + settingField.PropertyType.GetEnumValues().GetValue(enumValue).ToString());
                            }

                            streamWriter.WriteLine(settingField.Name.ToLower() + "," + (int)settingField.GetValue(settingsStore, null));
                        }

                        streamWriter.WriteLine(string.Empty);
                    }
                }
            }

            if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
            }

            File.Move(settingsFile + ".tmp", settingsFile);
        }
    }

    public class SettingsStore
    {
        public SettingsStore()
        {
            this.address = "0.0.0.0";
            this.port = 6702;
            this.warpMode = WarpMode.SUBSPACE;
            this.gameMode = GameMode.SANDBOX;
            this.whitelisted = false;
            this.modControl = ModControlMode.ENABLED_STOP_INVALID_PART_SYNC;
            this.keepTickingWhileOffline = true;
            this.sendPlayerToLatestSubspace = true;
            this.useUTCTimeInLog = false;
            this.logLevel = DarkLog.LogLevels.DEBUG;
            this.screenshotsPerPlayer = 20;
            this.screenshotHeight = 720;
            this.cheats = true;
            this.httpPort = 0;
            this.serverName = "DMP Server";
            this.maxPlayers = 20;
            this.screenshotDirectory = string.Empty;
            this.autoNuke = 0;
            this.autoDekessler = 30;
            this.numberOfAsteroids = 30;
            this.consoleIdentifier = "Server";
            this.serverMotd = "Welcome, %name%!";
            this.expireScreenshots = 0;
            this.compressionEnabled = true;
            this.expireLogs = 0;
        }

        [Description("The address the server listens on.\r\n#WARNING: You do not need to change this unless you are running 2 servers on the same port.\r\n#Changing this setting from 0.0.0.0 will only give you trouble if you aren't running multiple servers.\r\n#Change this setting to :: to listen on IPv4 and IPv6.")]
        public string address { get; set; }

        [Description("The port the server listens on.")]
        public int port { get; set; }

        [Description("Specify the warp type.")]
        public WarpMode warpMode { get; set; }

        [Description("Specify the game type.")]
        public GameMode gameMode { get; set; }

        [Description("Enable white-listing.")]
        public bool whitelisted { get; set; }

        [Description("Enable mod control.\r\n#WARNING: Only consider turning off mod control for private servers.\r\n#The game will constantly complain about missing parts if there are missing mods.")]
        public ModControlMode modControl { get; set; }

        [Description("Specify if the the server universe 'ticks' while nobody is connected or the server is shut down.")]
        public bool keepTickingWhileOffline { get; set; }

        [Description("If true, sends the player to the latest subspace upon connecting. If false, sends the player to the previous subspace they were in.\r\n#NOTE: This may cause time-paradoxes, and will not work across server restarts.")]
        public bool sendPlayerToLatestSubspace { get; set; }

        [Description("Use UTC instead of system time in the log.")]
        public bool useUTCTimeInLog { get; set; }

        [Description("Minimum log level.")]
        public DarkLog.LogLevels logLevel { get; set; }

        [Description("Specify maximum number of screenshots to save per player. -1 = None, 0 = Unlimited")]
        public int screenshotsPerPlayer { get; set; }

        [Description("Specify vertical resolution of screenshots.")]
        public int screenshotHeight { get; set; }

        [Description("Enable use of cheats in-game.")]
        public bool cheats { get; set; }

        [Description("HTTP port for server status. 0 = Disabled")]
        public int httpPort { get; set; }

        [Description("Name of the server.")]
        public string serverName { get; set; }

        [Description("Maximum amount of players that can join the server.")]
        public int maxPlayers { get; set; }

        [Description("Specify a custom screenshot directory.\r\n#This directory must exist in order to be used. Leave blank to store it in Universe.")]
        public string screenshotDirectory { get; set; }

        [Description("Specify in minutes how often /nukeksc automatically runs. 0 = Disabled")]
        public int autoNuke { get; set; }

        [Description("Specify in minutes how often /dekessler automatically runs. 0 = Disabled")]
        public int autoDekessler { get; set; }

        [Description("How many untracked asteroids to spawn into the universe. 0 = Disabled")]
        public int numberOfAsteroids { get; set; }

        [Description("Specify the name that will appear when you send a message using the server's console.")]
        public string consoleIdentifier { get; set; }

        [Description("Specify the server's MOTD (message of the day).")]
        public string serverMotd { get; set; }

        [Description("Specify the amount of days a screenshot should be considered as expired and deleted. 0 = Disabled")]
        public double expireScreenshots { get; set; }

        [Description("Specify whether to enable compression. Decreases bandwidth usage but increases CPU usage. 0 = Disabled")]
        public bool compressionEnabled { get; set; }

        [Description("Specify the amount of days a log file should be considered as expired and deleted. 0 = Disabled")]
        public double expireLogs { get; set; }
    }
}