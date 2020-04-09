using System;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class LogExpire
    {
        private static string logDirectory
        {
            get
            {
                return Path.Combine(Server.universeDirectory, DarkLog.LogFolder);
            }
        }

        public static void ExpireLogs()
        {
            if (!Directory.Exists(logDirectory))
            {
                //Screenshot directory is missing so there will be no screenshots to delete.
                return;
            }
            string[] logFiles = Directory.GetFiles(logDirectory);
            foreach (string logFile in logFiles)
            {
                //Check if the expireScreenshots setting is enabled
                if (Settings.settingsStore.expireLogs > 0)
                {
                    //If the file is older than a day, delete it
                    if (File.GetCreationTime(logFile).AddDays(Settings.settingsStore.expireLogs) < DateTime.Now)
                    {
                        DarkLog.Debug("Deleting saved log '" + logFile + "', reason: Expired!");
                        try
                        {
                            File.Delete(logFile);
                        }
                        catch (Exception e)
                        {
                            DarkLog.Error("Exception while trying to delete '" + logFile + "'!, Exception: " + e.Message);
                        }
                    }
                }
            }
        }
    }
}