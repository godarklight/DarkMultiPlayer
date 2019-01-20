using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    public class BackwardsCompatibility
    {
        public static void UpdateModcontrolPartList()
        {
            if (!File.Exists(Server.modFile))
            {
                return;
            }
            bool readingParts = false;
            string modcontrolVersion = "";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#MODCONTROLVERSION=" + Common.MODCONTROL_VERSION);
            List<string> stockParts = Common.GetStockParts();
            List<string> modParts = new List<string>();
            bool partsPrinted = false;
            using (StreamReader sr = new StreamReader(Server.modFile))
            {
                string currentLine = null;
                while ((currentLine = sr.ReadLine()) != null)
                {
                    string trimmedLine = currentLine.Trim();
                    if (!readingParts)
                    {
                        if (trimmedLine.StartsWith("#MODCONTROLVERSION="))
                        {
                            modcontrolVersion = trimmedLine.Substring(currentLine.IndexOf("=") + 1);
                            if (modcontrolVersion == Common.MODCONTROL_VERSION)
                            {
                                //Mod control file is up to date.
                                return;
                            }
                        }
                        else
                        {
                            sb.AppendLine(currentLine);
                        }
                        if (trimmedLine == "!partslist")
                        {
                            readingParts = true;
                        }
                    }
                    else
                    {
                        if (trimmedLine.StartsWith("#") || trimmedLine == string.Empty)
                        {
                            sb.AppendLine(currentLine);
                            continue;
                        }
                        //This is an edge case, but it's still possible if someone moves something manually.
                        if (trimmedLine.StartsWith("!"))
                        {
                            if (!partsPrinted)
                            {
                                partsPrinted = true;
                                foreach (string stockPart in stockParts)
                                {
                                    sb.AppendLine(stockPart);
                                }
                                foreach (string modPart in modParts)
                                {
                                    sb.AppendLine(modPart);
                                }
                            }
                            readingParts = false;
                            sb.AppendLine(currentLine);
                            continue;
                        }
                        if (!stockParts.Contains(currentLine))
                        {
                            modParts.Add(currentLine);
                        }
                    }
                }
            }
            if (!partsPrinted)
            {
                partsPrinted = true;
                foreach (string stockPart in stockParts)
                {
                    sb.AppendLine(stockPart);
                }
                foreach (string modPart in modParts)
                {
                    sb.AppendLine(modPart);
                }
            }
            File.WriteAllText(Server.modFile + ".new", sb.ToString());
            File.Copy(Server.modFile + ".new", Server.modFile, true);
            File.Delete(Server.modFile + ".new");
            DarkLog.Debug("Added " + Common.MODCONTROL_VERSION + " parts to modcontrol.txt");
        }

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

