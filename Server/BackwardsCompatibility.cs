using System;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class BackwardsCompatibility
    {
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
    }
}

