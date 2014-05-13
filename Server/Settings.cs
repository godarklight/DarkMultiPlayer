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
        public static bool useUTCTimeInLog;
            useUTCTimeInLog = false;

        public static void Load()
        {
            FieldInfo[] settingFields = typeof(SettingsStore).GetFields();
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
            using (FileStream fs = new FileStream(settingsFile + ".tmp", FileMode.CreateNew))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.WriteLine("#Setting file format: (key),(value)");
                    sw.WriteLine("#This file will be re-written every time the server is started. Only known keys will be saved.");
                    foreach (FieldInfo settingField in settingFields)
                    {
                        if (settingDescriptions.ContainsKey(settingField.Name))
                        {
                            sw.WriteLine("#" + settingField.Name.ToLower() + " - " + settingDescriptions[settingField.Name]);
                        }
                        if (settingField.FieldType == typeof(string) || settingField.FieldType == typeof(int))
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
                    sw.WriteLine("#useUTC - displays the time in UTC/GMT format instead of displaying the elapsed milliseconds.");
                    sw.WriteLine("useUTC," + (useUTCTimeInLog ? "1" : "0"));
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
            Dictionary<string, string> descriptionList = new Dictionary<string, string>();
            descriptionList.Add("port", "The port the server listens on");
            descriptionList.Add("warpMode", "Specify the warp type");
            descriptionList.Add("gameMode", "Specify the game type");
            descriptionList.Add("modControl", "Enable mod control\n#WARNING: Only consider turning off mod control for private servers.\n#The game will constantly complain about missing parts if there are missing mods.");
            return descriptionList;
        }
    }

    public class SettingsStore
    {
        public int port = 6702;
        public WarpMode warpMode = WarpMode.SUBSPACE;
        public GameMode gameMode = GameMode.SANDBOX;
        public bool modControl = true;
    }
}