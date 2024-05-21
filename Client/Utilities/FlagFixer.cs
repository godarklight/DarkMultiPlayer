using System.IO;

namespace DarkMultiPlayer.Utilities
{
    static class FlagFixer
    {
        public static void Fix(string gameDataDir)
        {
            string[] flagFiles = Directory.GetFiles(Path.Combine(gameDataDir, "DarkMultiPlayer/Flags"));
            foreach (string file in flagFiles)
            {
                FileInfo fi = new FileInfo(file);
                if (fi.Length == 0)
                {
                    DarkLog.Debug($"Deleting broken flag {file}");
                    File.Delete(file);
                }
            }
        }
    }
}