using System;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class Settings
    {
        private const string SETTINGS_FILE_NAME = "DarkServerSettings.txt";
        public static string serverPath = AppDomain.CurrentDomain.BaseDirectory;
        //Port
        public static int port;

        private static void UseDefaultSettings()
        {
            port = 6702;
        }

        public static void Load()
        {
            if (!File.Exists(Path.Combine(serverPath, SETTINGS_FILE_NAME)))
            {
                UseDefaultSettings();
                Save();
            }
            using (FileStream fs = new FileStream(Path.Combine(serverPath, SETTINGS_FILE_NAME), FileMode.Open))
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
        }

        public static void Save()
        {
            using (FileStream fs = new FileStream(Path.Combine(serverPath, SETTINGS_FILE_NAME + ".tmp"), FileMode.CreateNew))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.WriteLine("port," + port);
                }
            }
            File.Move(Path.Combine(serverPath, SETTINGS_FILE_NAME + ".tmp"), Path.Combine(serverPath, SETTINGS_FILE_NAME));
        }
    }
}

