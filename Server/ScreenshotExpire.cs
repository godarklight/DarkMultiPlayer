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

        public static void ExpireScreenshots()
        {
            if (!Directory.Exists(screenshotDirectory))
            {
                //Screenshot directory is missing so there will be no screenshots to delete.
                return;
            }
            string[] screenshotFiles = Directory.GetFiles(screenshotDirectory);
            foreach (string screenshotFile in screenshotFiles)
            {
                string cacheFile = Path.Combine(screenshotDirectory, screenshotFile + ".png");
                //Check if the expireScreenshots setting is enabled
                if (Settings.settingsStore.expireScreenshots > 0)
                {
                    //If the file is older than a day, delete it
                    if (File.GetCreationTime(cacheFile).AddDays(Settings.settingsStore.expireScreenshots) < DateTime.Now)
                    {
                        DarkLog.Debug("Deleting saved screenshot '" + screenshotFile + "', reason: Expired!");
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