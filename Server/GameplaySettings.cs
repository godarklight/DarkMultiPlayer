using System;
using System.IO;
using System.Net;
using DarkMultiPlayerCommon;
using System.Reflection;
using System.Collections.Generic;

namespace DarkMultiPlayerServer
{
    public class GameplaySettings
    {
        private const string SETTINGS_FILE_NAME = "DMPGameplaySettings.txt";
        private static string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SETTINGS_FILE_NAME);

        private static GameplaySettingsStore _settingsStore;

        public static GameplaySettingsStore settingsStore
        {
            get
            {
                if (_settingsStore == null)
                {
                    _settingsStore = new GameplaySettingsStore();
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
            DarkLog.Debug("Loading gameplay settings");
            FieldInfo[] settingFields = typeof(GameplaySettingsStore).GetFields();
            if (!File.Exists(settingsFile))
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
                            if (trimmedLine.Contains("=") && !trimmedLine.StartsWith("#"))
                            {
                                currentKey = trimmedLine.Substring(0, trimmedLine.IndexOf("="));
                                currentValue = trimmedLine.Substring(trimmedLine.IndexOf("=") + 1);

                                foreach (FieldInfo settingField in settingFields)
                                {
                                    if (settingField.Name == currentKey)
                                    {
                                        if (settingField.FieldType == typeof(string))
                                        {
                                            settingField.SetValue(settingsStore, currentValue);
                                        }
                                        if (settingField.FieldType == typeof(float))
                                        {
                                            float floatValue = float.Parse(currentValue);
                                            settingField.SetValue(settingsStore, floatValue);
                                        }
                                        if (settingField.FieldType == typeof(bool))
                                        {
                                            if (currentValue == "true" || currentValue == "1")
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
                                        //DarkLog.Debug(settingField.Name + ": " + currentValue);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void Save()
        {
            DarkLog.Debug("Saving gameplay settings");
            FieldInfo[] settingFields = typeof(GameplaySettingsStore).GetFields();
            Dictionary<string, string> settingDescriptions = GetSettingsDescriptions();
            if (File.Exists(settingsFile + ".tmp"))
            {
                File.Delete(settingsFile + ".tmp");
            }
            using (FileStream fs = new FileStream(settingsFile + ".tmp", FileMode.CreateNew))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.WriteLine("# Setting file format: (key)=(value)");
                    sw.WriteLine("# This file will be re-written every time the server is started. Only known keys will be saved.");
                    sw.WriteLine("");
                    foreach (FieldInfo settingField in settingFields)
                    {
                        if (settingDescriptions.ContainsKey(settingField.Name))
                        {
                            sw.WriteLine("# " + settingField.Name + " - " + settingDescriptions[settingField.Name]);
                        }
                        if (settingField.FieldType == typeof(string) || settingField.FieldType == typeof(float))
                        {
                            sw.WriteLine(settingField.Name + "=" + settingField.GetValue(settingsStore));
                        }
                        if (settingField.FieldType == typeof(bool))
                        {
                            if ((bool)settingField.GetValue(settingsStore))
                            {
                                sw.WriteLine(settingField.Name + "=true");
                            }
                            else
                            {
                                sw.WriteLine(settingField.Name + "=false");
                            }
                        }
                        if (settingField.FieldType.IsEnum)
                        {
                            sw.WriteLine("# Valid values are:");
                            foreach (int enumValue in settingField.FieldType.GetEnumValues())
                            {
                                sw.WriteLine("# " + enumValue + " - " + settingField.FieldType.GetEnumValues().GetValue(enumValue).ToString());
                            }
                            sw.WriteLine(settingField.Name.ToLower() + "=" + (int)settingField.GetValue(settingsStore));
                        }
                        sw.WriteLine("");
                    }
                }
            }
            if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
            }
            File.Move(settingsFile + ".tmp", settingsFile);
        }

        private static Dictionary<string, string> GetSettingsDescriptions()
        {
            Dictionary<string, string> descriptionList = new Dictionary<string, string>();
            descriptionList.Add("allowStockVessels", "Allow Stock Vessels");
            descriptionList.Add("autoHireCrews", "Auto-Hire Crewmemebers before Flight");
            descriptionList.Add("bypassEntryPurchaseAfterResearch", "No Entry Purchase Required on Research");
            descriptionList.Add("indestructibleFacilities", "Indestructible Facilities");
            descriptionList.Add("missingCrewsRespawn", "Missing Crews Respawn");
            descriptionList.Add("fundsGainMultiplier", "Funds Rewards");
            descriptionList.Add("fundsLossMultiplier", "Funds Penalties");
            descriptionList.Add("repGainMultiplier", "Reputation Rewards");
            descriptionList.Add("repLossMultiplier", "Reputation Penalties");
            descriptionList.Add("startingFunds", "Starting Funds");
            descriptionList.Add("startingReputation", "Starting Reputation");
            descriptionList.Add("startingScience", "Starting Science");
            return descriptionList;
        }
    }

    public class GameplaySettingsStore
    {
        // Difficulty Settings
        public bool allowStockVessels = false;
        public bool autoHireCrews = true;
        public bool bypassEntryPurchaseAfterResearch = true;
        public bool indestructibleFacilities = false;
        public bool missingCrewsRespawn = true;
        // Career Settings
        public float fundsGainMultiplier = 1.0f;
        public float fundsLossMultiplier = 1.0f;
        public float repGainMultiplier = 1.0f;
        public float repLossMultiplier = 1.0f;
        public float scienceGainMultiplier = 1.0f;
        public float startingFunds = 25000.0f;
        public float startingReputation = 0.0f;
        public float startingScience = 0.0f;
    }
}
