using System;
using System.IO;
using System.Reflection;

namespace DarkMultiPlayerServer
{
    public class BackwardsCompatibility
    {
        public static void RemoveOldPlayerTokens()
        {
            string playerDirectory = Path.Combine(Server.universeDirectory, "Players");
            if (!Directory.Exists(playerDirectory))
            {
                return;
            }
            string[] playerFiles = Directory.GetFiles(playerDirectory, "*", SearchOption.TopDirectoryOnly);
            Guid testGuid;
            foreach (string playerFile in playerFiles)
            {
                try
                {
                    string playerText = File.ReadAllLines(playerFile)[0];
                    if (Guid.TryParse(playerText, out testGuid))
                    {
                        //Player token detected, remove it
                        DarkLog.Debug("Removing old player token for " + Path.GetFileNameWithoutExtension(playerFile));
                        File.Delete(playerFile);
                    }
                }
                catch
                {
                    DarkLog.Debug("Removing damaged player token for " + Path.GetFileNameWithoutExtension(playerFile));
                    File.Delete(playerFile);
                }
            }
        }

        public static void FixKerbals()
        {
            string kerbalPath = Path.Combine(Server.universeDirectory, "Kerbals");
            int kerbalCount = 0;

            while (File.Exists(Path.Combine(kerbalPath, kerbalCount + ".txt")))
            {
                string oldKerbalFile = Path.Combine(kerbalPath, kerbalCount + ".txt");
                string kerbalName = null;

                using (StreamReader sr = new StreamReader(oldKerbalFile))
                {
                    string fileLine;
                    while ((fileLine = sr.ReadLine()) != null)
                    {
                        if (fileLine.StartsWith("name = "))
                        {
                            kerbalName = fileLine.Substring(fileLine.IndexOf("name = ") + 7);
                            break;
                        }
                    }
                }

                if (!String.IsNullOrEmpty(kerbalName))
                {
                    DarkLog.Debug("Renaming kerbal " + kerbalCount + " to " + kerbalName);
                    File.Move(oldKerbalFile, Path.Combine(kerbalPath, kerbalName + ".txt"));
                }
                kerbalCount++;
            }

            if (kerbalCount != 0)
            {
                DarkLog.Normal("Kerbal database upgraded to 0.24 format");
            }
        }

        public static void ConvertSettings(string oldSettings, string newSettings)
        {
            if (!File.Exists(oldSettings))
            {
                return;
            }

            using (StreamWriter sw = new StreamWriter(newSettings))
            {
                using (StreamReader sr = new StreamReader(oldSettings))
                {

                    string currentLine;
                    while ((currentLine = sr.ReadLine()) != null)
                    {
                        string trimmedLine = currentLine.Trim();
                        if (!trimmedLine.Contains(",") || trimmedLine.StartsWith("#") || trimmedLine == string.Empty)
                        {
                            continue;
                        }
                        int seperatorIndex = trimmedLine.IndexOf(",");
                        string keyPart = trimmedLine.Substring(0, seperatorIndex).Trim();
                        string valuePart = trimmedLine.Substring(seperatorIndex + 1).Trim();
                        string realKey = keyPart;
                        foreach (FieldInfo fieldInfo in typeof(SettingsStore).GetFields())
                        {
                            if (fieldInfo.Name.ToLower() == keyPart.ToLower())
                            {
                                realKey = fieldInfo.Name;
                                break;
                            }
                        }
                        sw.WriteLine(realKey + "=" + valuePart);
                    }
                }
            }
            File.Delete(oldSettings);
            DarkLog.Debug("Converted settings to DMP v0.2.1.0 format");
        }
    }
}

