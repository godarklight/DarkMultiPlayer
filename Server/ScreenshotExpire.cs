using System;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class ScreenshotExpire
    {
        private static string screenshotDirectory
        {
            get
            {
                if (Settings.settingsStore.screenshotDirectory != "")
                {
                    return Settings.settingsStore.screenshotDirectory;
                }
                return Path.Combine(Server.universeDirectory, "Screenshots");
            }
        }

        private static string[] GetCachedObjects()
        {
            string[] cacheFiles = Directory.GetFiles(screenshotDirectory);
            string[] cacheObjects = new string[cacheFiles.Length];
            for (int i = 0; i < cacheFiles.Length; i++)
            {
                cacheObjects[i] = Path.GetFileNameWithoutExtension(cacheFiles[i]);
            }
            return cacheObjects;
        }

        public static void ExpireCache()
        {
            string[] cacheObjects = GetCachedObjects();
            foreach (string cacheObject in cacheObjects)
            {
                string cacheFile = Path.Combine(screenshotDirectory, cacheObject + ".png");
                //Check if the expireScreenshots setting is enabled
                if (Settings.settingsStore.expireScreenshots > 0)
                {
                    //If the file is older than a day, delete it
                    if (File.GetCreationTime(cacheFile).AddDays(Settings.settingsStore.expireScreenshots) < DateTime.Now)
                    {
                        DarkLog.Debug("Deleting saved screenshot '" + cacheObject + "', reason: Expired!");
                        try
                        {
                            File.Delete(cacheFile);
                        }
                        catch (Exception e)
                        {
                            DarkLog.Error("Exception while trying to delete " + cacheFile + "!, Exception: " + e.Message);
                        }
                    }
                }
            }
        }
    }
}