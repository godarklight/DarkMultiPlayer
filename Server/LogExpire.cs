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

        private static string[] GetCachedObjects()
        {
            string[] cacheFiles = Directory.GetFiles(logDirectory);
            string[] cacheObjects = new string[cacheFiles.Length];
            for (int i = 0; i < cacheFiles.Length; i++)
            {
                cacheObjects[i] = Path.GetFileNameWithoutExtension(cacheFiles[i]);
            }
            return cacheObjects;
        }

        public static void ExpireCache()
        {
            if (!Directory.Exists(logDirectory))
            {
                //Screenshot directory is missing so there will be no screenshots to delete.
                return;
            }
            string[] cacheObjects = GetCachedObjects();
            foreach (string cacheObject in cacheObjects)
            {
                string cacheFile = Path.Combine(logDirectory, cacheObject + ".log");
                //Check if the expireScreenshots setting is enabled
                if (Settings.settingsStore.expireScreenshots > 0)
                {
                    //If the file is older than a day, delete it
                    if (File.GetCreationTime(cacheFile).AddDays(Settings.settingsStore.expireLogs) < DateTime.Now)
                    {
                        DarkLog.Debug("Deleting saved log '" + cacheObject + "', reason: Expired!");
                        try
                        {
                            File.Delete(cacheFile);
                        }
                        catch (Exception e)
                        {
                            DarkLog.Error("Exception while trying to delete '" + cacheFile + "'!, Exception: " + e.Message);
                        }
                    }
                }
            }
        }
    }
}