using System;
using System.IO;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    public class Settings
    {
        private const string SETTINGS_FILE_NAME = "DMPServerSettings.txt";
        public static string serverPath = AppDomain.CurrentDomain.BaseDirectory;
        private static string settingsFile = Path.Combine(serverPath, SETTINGS_FILE_NAME);
        //Port
        public static int port;
        public static WarpMode warpMode;
        public static GameMode gameMode;
        public static bool modControl;

        private static void UseDefaultSettings()
        {
            port = 6702;
            warpMode = WarpMode.SUBSPACE;
            gameMode = GameMode.SANDBOX;
            modControl = true;
        }

        public static void Load()
        {
            UseDefaultSettings();
            if (!File.Exists(Path.Combine(serverPath, SETTINGS_FILE_NAME)))
            {
                Save();
            }
            using (FileStream fs = new FileStream(settingsFile, FileMode.Open))
            {
                using (StreamReader sr = new StreamReader(fs))
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
                                switch (currentKey)
                                {
                                    case "port":
                                        port = Int32.Parse(currentValue);
                                        DarkLog.Debug("Port: " + port);
                                        break;
                                    case "warpmode":
                                        warpMode = (WarpMode)(Int32.Parse(currentValue));
                                        DarkLog.Debug("Warp mode: " + warpMode);
                                        break;
                                    case "gamemode":
                                        gameMode = (GameMode)(Int32.Parse(currentValue));
                                        DarkLog.Debug("Game mode: " + gameMode);
                                        break;
                                    case "modcontrol":
                                        modControl = (currentValue == "1");
                                        DarkLog.Debug("Mod control: " + modControl);
                                        break;
                                    default:
                                        Console.WriteLine("Unknown key: " + currentKey);
                                        break;
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
            if (File.Exists(settingsFile + ".tmp"))
            {
                File.Delete(settingsFile + ".tmp");
            }
            using (FileStream fs = new FileStream(settingsFile + ".tmp", FileMode.CreateNew))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.WriteLine("#Setting file format: (key),(value)");
                    sw.WriteLine("#This file will be re-written every time the server is started. Only known keys will be saved.");
                    sw.WriteLine("#port - The port the server listens on");
                    sw.WriteLine("port," + port);
                    sw.WriteLine("");
                    sw.WriteLine("#warpmode - Specify the warp type");
                    foreach (int warpModeType in Enum.GetValues(typeof(WarpMode)))
                    {
                        sw.WriteLine("#Mode " + warpModeType + " - " + (WarpMode)warpModeType);
                    }
                    sw.WriteLine("warpmode," + (int)warpMode);
                    sw.WriteLine("");
                    sw.WriteLine("#gamemode - Specify the game type");
                    foreach (int gameModeType in Enum.GetValues(typeof(GameMode)))
                    {
                        sw.WriteLine("#Mode " + gameModeType + " - " + (GameMode)gameModeType);
                    }
                    sw.WriteLine("gamemode," + (int)gameMode);
                    sw.WriteLine("");
                    sw.WriteLine("#modcontrol - Enable mod control");
                    sw.WriteLine("#WARNING: Only consider turning off mod control for private servers.");
                    sw.WriteLine("#The game will constantly complain about missing parts if there are missing mods.");
                    sw.WriteLine("modcontrol," + (modControl ? "1" : "0"));
                    sw.WriteLine("");
                }
            }
            if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
            }
            File.Move(Path.Combine(serverPath, SETTINGS_FILE_NAME + ".tmp"), Path.Combine(serverPath, SETTINGS_FILE_NAME));
        }
    }
}

