using System;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class Dekessler
    {
        public static void RunDekessler(string commandText)
        {
            string[] vesselList = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels"));
            int numberOfRemovals = 0;
            foreach (string vesselFile in vesselList)
            {
                string vesselID = Path.GetFileNameWithoutExtension(vesselFile);
                bool vesselIsDebris = false;
                using (StreamReader sr = new StreamReader(vesselFile))
                {
                    string currentLine = sr.ReadLine();
                    while (currentLine != null && !vesselIsDebris)
                    {
                        string trimmedLine = currentLine.Trim();
                        if (trimmedLine.StartsWith("type = "))
                        {
                            string vesselType = trimmedLine.Substring(trimmedLine.IndexOf("=") + 2);
                            if (vesselType == "Debris")
                            {
                                vesselIsDebris = true;
                            }
                        }
                        currentLine = sr.ReadLine();
                    }
                }
                if (vesselIsDebris)
                {
                    DarkLog.Normal("Removing vessel: " + vesselID);
                    File.Delete(vesselFile);
                    numberOfRemovals++;
                }
            }
            DarkLog.Normal("Removed " + numberOfRemovals + " debris");
        }
    }
}

