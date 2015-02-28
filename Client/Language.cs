using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Generic;

namespace DarkMultiPlayer
{
    public class Language
    {

        // General
        public static string cancelBtn = "Cancel";
        public static string connectBtn = "Connect";
        public static string disconnectBtn = "Disconnect";
        public static string randomBtn = "Random";
        public static string setBtn = "Set";
        public static string closeBtn = "Close";
        public static string removeBtn = "Remove";
        public static string uploadBtn = "Upload";
        public static string uploadBtnKey = "Upload ({0})";
        public static string unknown = "Unknown";
        public static string enable = "Enable";
        public static string disable = "Disable";

        // Main Menu
        public static string addServer = "Add";
        public static string editServer = "Edit";
        public static string serverNameLabel = "Name:";
        public static string serverAddressLabel = "Address:";
        public static string serverPortLabel = "Port:";
        public static string optionsBtn = "Options";
        public static string playerNameLabel = "Player name:";
        public static string serversLabel = "Servers:";
        public static string serverAddEdit = "server";
        public static string noServers = "(None - Add a server first)";

        // Options
        public static string options = "Options";
        public static string playerNameColor = "Player name color:";
        public static string cacheSizeLabel = "Cache size";
        public static string currentCacheSizeLabel = "Current size: {0}MB";
        public static string maxCacheSizeLabel = "Max size: {0]MB";
        public static string expireCacheButton = "Expire cache";
        public static string deleteCacheButton = "Delete cache";
        public static string setChatKeyBtn = "Set chat key ({0}: {1})";
        public static string currentKey = "current";
        public static string setScrnShotKeyBtn = "Set screenshot key ({0}: {1})";
        public static string generateModCntrlLabel = "Generate a server DMPModControl:";
        public static string generateModBlacklistBtn = "Generate blacklist DMPModControl.txt";
        public static string generateModWhitelistBtn = "Generate whitelist DMPModControl.txt";
        public static string generateUniverseSavedGameBtn = "Generate Universe from saved game";
        public static string resetDisclaimerBtn = "Reset disclaimer";
        public static string enableCompressionBtn = "{0} compression";
        public static string enableRevertBtn = "{0} revert";
        public static string toolbarModeLabel = "Toolbar:";
        public static string langLabel = "Language:";

        // Toolbar types
        public static string tbBlizzy = "Toolbar (mod)";
        public static string tbStock = "Stock";
        public static string tbDisabled = "Disabled";
        public static string tbBoth = "Both (if installed)";

        // In-Server Window
        public static string chatBtn = "Chat";
        public static string craftBtn = "Craft";
        public static string debugBtn = "Debug";
        public static string screenshotBtn = "Screenshot";
        public static string timeNow = "NOW";
        public static string inFuture = "{0} in the future";
        public static string inPast = "{0} in the past";
        public static string syncBtn = "Sync";
        public static string pilotLabel = "Pilot: {0}";

        // Craft Library
        public static string craftLibrary = "Craft Library";
        public static string plrCraftLibrary = "{0}'s Craft Library";
        public static string uploadedCraftMsg = "Craft uploaded!";
        public static string uploadingCraftMsg = "Uploading craft...";
        public static string savedCraftMsg = "Craft {0} saved!";

        // Screenshot Library
        public static string screenshots = "Screenshots";
        public static string sharedScreenshotMsg = "{0} shared a screenshot";
        public static string screenshotUploadedMsg = "Screenshot uploaded!";
        public static string uploadingScreenshotMsg = "Uploading screenshot...";
        public static string downloadingScreenshotMsg = "Downloading screenshot...";

        // Dates
        public static string singularYear = "year";
        public static string pluralYear = "years";
        public static string singularMonth = "month";
        public static string pluralMonth = "months";
        public static string singularWeek = "week";
        public static string pluralWeek = "weeks";
        public static string singularDay = "day";
        public static string pluralDay = "days";
        public static string singularHour = "hour";
        public static string pluralHour = "hours";
        public static string singularMinute = "minute";
        public static string pluralMinute = "minutes";
        public static string singularSecond = "second";
        public static string pluralSecond = "seconds";
        public static string shortYear = "y";
        public static string shortMonth = "m";
        public static string shortWeek = "w";
        public static string shortDay = "d";

    }

    public enum DMPLanguage
    {
        ENGLISH,
        SPANISH,
        BRAZILIAN,
        GERMAN
    }

    public class LanguageWorker
    {
        //LanguageWorker
        private static LanguageWorker singleton = new LanguageWorker();
        private string dataLocation;
        private string langFile;
        private string LANGUAGE_FILE = "english.txt";

        private bool loadedSettings = false;

        private static Dictionary<string, string> languageStrings;

        public static LanguageWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        public LanguageWorker()
        {
            dataLocation = Path.Combine(Directory.GetParent(System.Reflection.Assembly.GetExecutingAssembly().Location).Parent.ToString(), "Language");
            if (!Directory.Exists(dataLocation))
            {
                Directory.CreateDirectory(dataLocation);
            }
            langFile = Path.Combine(dataLocation, LANGUAGE_FILE);
            languageStrings = new Dictionary<string, string>();
        }

        public void LoadLanguage(DMPLanguage lang)
        {
            switch (lang)
            {
                case DMPLanguage.ENGLISH:
                    LANGUAGE_FILE = "english.txt";
                    break;
                case DMPLanguage.SPANISH:
                    LANGUAGE_FILE = "spanish.txt";
                    break;
                case DMPLanguage.BRAZILIAN:
                    LANGUAGE_FILE = "brazilian.txt";
                    break;
                case DMPLanguage.GERMAN:
                    LANGUAGE_FILE = "german.txt";
                    break;
            }

            langFile = Path.Combine(dataLocation, LANGUAGE_FILE);

            try
            {
                languageStrings.Clear();
            }
            catch (Exception e)
            {
                DarkLog.Debug("Error wiping loaded strings: " + e.ToString());
            }

            try
            {
                using (StreamReader sr = new StreamReader(langFile))
                {
                    string currentLine;

                    // darklight's key=value parser
                    while ((currentLine = sr.ReadLine()) != null)
                    {
                        if (currentLine.StartsWith("#"))
                        {
                            string loadedLanguage = currentLine.Substring(currentLine.IndexOf("=") + 1).Trim();
                        }

                        try
                        {
                            string key = currentLine.Substring(0, currentLine.IndexOf("=")).Trim();
                            string value = currentLine.Substring(currentLine.IndexOf("=") + 1).Trim();
                            languageStrings.Add(key, value);
                        }
                        catch (Exception e)
                        {
                            DarkLog.Debug("Error while reading language file: " + e);
                        }
                    }
                }
                DarkLog.Debug("Loaded " + languageStrings.Count + " language strings");
                loadedSettings = true;
            }
            catch (Exception e)
            {
                DarkLog.Debug("Language file not found!");
                if (!File.Exists(langFile))
                {
                    using (StreamWriter sw = new StreamWriter(langFile))
                    {
                        sw.WriteLine("# language=English");
                        foreach (FieldInfo fieldInfo in typeof(Language).GetFields())
                        {
                            sw.WriteLine(fieldInfo.Name + "=" + fieldInfo.GetValue(null));
                        }
                    }
                    LoadLanguage(Settings.fetch.useLanguage);
                }
            }
        }

        public string GetString(string key)
        {
            if (languageStrings.ContainsKey(key))
            {
                return languageStrings[key];
            }
            return key;
        }

        public string GetFormattedString(string toFormat, string[] args)
        {
            return String.Format(toFormat, args);
        }

        public string GetLanguageById(DMPLanguage lang)
        {
            switch (lang)
            {
                default:
                case DMPLanguage.ENGLISH:
                    return "English";
                case DMPLanguage.SPANISH:
                    return "Español";
                case DMPLanguage.BRAZILIAN:
                    return "Português (Brasil)";
                case DMPLanguage.GERMAN:
                    return "German";
            }
        }
    }
}
