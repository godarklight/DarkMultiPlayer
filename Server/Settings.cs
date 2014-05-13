using System;
using System.IO;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using System.Text.RegularExpressions;

namespace DarkMultiPlayerServer
{
    public class Settings
    {
        private const string SETTINGS_FILE_NAME = "DMPServerSettings.txt";
        public static string serverPath = AppDomain.CurrentDomain.BaseDirectory;
        private static string settingsFile = Path.Combine(serverPath, SETTINGS_FILE_NAME);

		private static Dictionary<string,ConfigEntry> config = new Dictionary<string,ConfigEntry>();

        public static void Load()
        {
            if (!File.Exists(Path.Combine(serverPath, SETTINGS_FILE_NAME)))
            {
                Save();
				return;
            }
			foreach (ConfigEntry entry in config.Values)
			{
				entry.cachedValue = null;
				entry.value = null;
			}
            using (FileStream fs = new FileStream(settingsFile, FileMode.Open))
            {
                using (StreamReader sr = new StreamReader(fs))
                {
                    string currentLine;
                    string trimmedLine;
                    string currentKey;
                    string currentValue;
					while ((currentLine = sr.ReadLine()) != null)
                    {
                        trimmedLine = currentLine.Trim();
						if (String.IsNullOrEmpty(trimmedLine))
							continue;

						if (!trimmedLine.Contains(",") || trimmedLine.StartsWith("#"))
							continue;

						Match match = Regex.Match(trimmedLine, @"^([^,# ]+),\s*([^,# ]+)\s*(#.*)?$");
						if (!match.Success)
							continue;

						currentKey = match.Groups[1].Value;
						currentValue = match.Groups[2].Value;
						if (!config.ContainsKey(currentKey))
						{
							DarkLog.Debug("Unregistered key: " + currentKey);
							ConfigEntry entry = new ConfigEntry();
							entry.key = currentKey;
							entry.value = currentValue;
							config.Add(currentKey,entry);
						}
						else
						{
							config[currentKey].value = currentValue;
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
					sw.WriteLine();
					foreach (ConfigEntry entry in config.Values)
					{
						entry.value = null;
						entry.normalize();
						foreach (string desc in entry.description)
						{
							sw.WriteLine("#" + desc);
						}
						if (entry.type == ConfigType.ENUM)
						{
							sw.WriteLine();
							sw.WriteLine("#Possible values are:");
							foreach (Enum enu in Enum.GetValues(entry.cachedValue.GetType())) //This is quite hacky - better solution without additional RAM usage?
							{
								sw.WriteLine("#" + enu.ToString());
							}
						}
						sw.WriteLine(entry.key + "," + entry.value);
						sw.WriteLine();
						sw.WriteLine();
					}
                }
            }
            if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
            }
            File.Move(Path.Combine(serverPath, SETTINGS_FILE_NAME + ".tmp"), Path.Combine(serverPath, SETTINGS_FILE_NAME));
        }

		public static int getInteger(string key)
		{
			return (int)Settings.getObject(key);
		}

		public static string getString(string key)
		{
			return (string)Settings.getObject(key);
		}

		public static bool getBoolean(string key)
		{
			return (bool)Settings.getObject(key);
		}

		public static Enum getEnum(string key)
		{
			return (Enum)Settings.getObject(key);
		}

		public static object getObject(string key)
		{
			ConfigEntry entry = config[key];

			if (entry.cachedValue == null)
			{
				if (entry.value == null)
				{
					//No value available, use default value
					//value stays null, since that is not available - might cause confusion, be careful
					entry.cachedValue = entry.defaultValue;
				}
				else
				{
					entry.cachedValue = InterpretValue(entry);
				}
			}
			return entry.cachedValue;
		}

		public static void RegisterConfigKey(string key, object defaultValue, ConfigType type, string[] description)
		{
			if (config.ContainsKey(key))
			{
				ConfigEntry entry = config[key];
				entry.defaultValue = defaultValue;
				entry.type = type;
				entry.description = description;
			}
			else
			{
				ConfigEntry entry = new ConfigEntry(key, defaultValue, type, description);
				config.Add(key, entry);
			}
		}

		public static void RegisterEnumConfigKey(string key, Enum defaultValue, Type enumType, string[] description)
		{
			if (config.ContainsKey(key))
			{
				RegisterConfigKey(key, defaultValue, ConfigType.ENUM, description);
				((EnumConfigEntry)config[key]).enumType = enumType;
			}
			else
			{
				EnumConfigEntry entry = new EnumConfigEntry(key, defaultValue, enumType, description);
				config.Add(key, entry);
			}
		}

		private static object InterpretValue(ConfigEntry entry)
		{
			try
			{
				string value = entry.value;
				switch (entry.type)
				{
					case ConfigType.INTEGER:
						return Int32.Parse(value);
					case ConfigType.STRING:
						return value;
					case ConfigType.BOOLEAN:
						return Boolean.Parse(value);
					case ConfigType.ENUM:
						EnumConfigEntry enumEntry = entry as EnumConfigEntry;
						return Enum.Parse(enumEntry.enumType, entry.value);
					default:
						DarkLog.Debug("Unknown ConfigType specified: " + entry.type.ToString());
						return null;
				}
			}
			catch (Exception)
			{
				//Use default value if something goes wrong
				return entry.defaultValue;
			}
		}

		public enum ConfigType
		{
			STRING,
			INTEGER,
			BOOLEAN,
			ENUM
		}

		private class ConfigEntry
		{
			public string key;
			public string value;
			public object cachedValue;
			public object defaultValue;
			public ConfigType type;
			public string[] description;

			public ConfigEntry()
			{
			}

			public ConfigEntry(string key, object defaultValue, ConfigType type, string[] description)
			{
				this.key = key;
				this.defaultValue = defaultValue;
				this.type = type;
				this.description = description;
			}

			/**
			 * If fields can be inferred by other fields, do it
			 */
			public void normalize()
			{
				if (String.IsNullOrEmpty(value) && cachedValue == null)
				{
					cachedValue = defaultValue;
				}
				if (String.IsNullOrEmpty(value) && cachedValue != null)
				{
					value = cachedValue.ToString();
				}
				if (!String.IsNullOrEmpty(value) && cachedValue == null)
				{
					cachedValue = InterpretValue(this);
				}
			}
		}

		private class EnumConfigEntry : ConfigEntry
		{
			public Type enumType;

			public EnumConfigEntry(string key, Enum defaultValue, Type enumType, string[] description) : base(key, defaultValue, ConfigType.ENUM, description)
			{
				this.enumType = enumType;
			}
		}
    }
}

