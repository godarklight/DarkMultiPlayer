using System;
using System.Collections.Generic;
using System.IO;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class UniverseConverter
    {
        private static string savesFolder = Path.Combine(Client.dmpClient.kspRootPath, "saves");
        //Services
        private Settings dmpSettings;

        public UniverseConverter(Settings dmpSettings)
        {
            this.dmpSettings = dmpSettings;
        }

        public void GenerateUniverse(string saveName)
        {
            string universeFolder = Path.Combine(Client.dmpClient.kspRootPath, "Universe");
            if (Directory.Exists(universeFolder))
            {
                Directory.Delete(universeFolder, true);
            }

            string saveFolder = Path.Combine(savesFolder, saveName);
            if (!Directory.Exists(saveFolder))
            {
                DarkLog.Debug("Failed to generate a DMP universe for '" + saveName + "', Save directory doesn't exist");
                ScreenMessages.PostScreenMessage("Failed to generate a DMP universe for '" + saveName + "', Save directory doesn't exist", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            string persistentFile = Path.Combine(saveFolder, "persistent.sfs");
            if (!File.Exists(persistentFile))
            {
                DarkLog.Debug("Failed to generate a DMP universe for '" + saveName + "', persistent.sfs doesn't exist");
                ScreenMessages.PostScreenMessage("Failed to generate a DMP universe for '" + saveName + "', persistent.sfs doesn't exist", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            Directory.CreateDirectory(universeFolder);
            string vesselFolder = Path.Combine(universeFolder, "Vessels");
            Directory.CreateDirectory(vesselFolder);
            string scenarioFolder = Path.Combine(universeFolder, "Scenarios");
            Directory.CreateDirectory(scenarioFolder);
            string playerScenarioFolder = Path.Combine(scenarioFolder, dmpSettings.playerName);
            Directory.CreateDirectory(playerScenarioFolder);
            string kerbalFolder = Path.Combine(universeFolder, "Kerbals");
            Directory.CreateDirectory(kerbalFolder);

            //Load game data
            ConfigNode persistentData = ConfigNode.Load(persistentFile);
            if (persistentData == null)
            {
                DarkLog.Debug("Failed to generate a DMP universe for '" + saveName + "', failed to load persistent data");
                ScreenMessages.PostScreenMessage("Failed to generate a DMP universe for '" + saveName + "', failed to load persistent data", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            ConfigNode gameData = persistentData.GetNode("GAME");
            if (gameData == null)
            {
                DarkLog.Debug("Failed to generate a DMP universe for '" + saveName + "', failed to load game data");
                ScreenMessages.PostScreenMessage("Failed to generate a DMP universe for '" + saveName + "', failed to load game data", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            //Save vessels
            ConfigNode flightState = gameData.GetNode("FLIGHTSTATE");
            if (flightState == null)
            {
                DarkLog.Debug("Failed to generate a DMP universe for '" + saveName + "', failed to load flight state data");
                ScreenMessages.PostScreenMessage("Failed to generate a DMP universe for '" + saveName + "', failed to load flight state data", 5f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            ConfigNode[] vesselNodes = flightState.GetNodes("VESSEL");
            if (vesselNodes != null)
            {
                foreach (ConfigNode cn in vesselNodes)
                {
                    string vesselID = Common.ConvertConfigStringToGUIDString(cn.GetValue("pid"));
                    DarkLog.Debug("Saving vessel " + vesselID + ", name: " + cn.GetValue("name"));
                    cn.Save(Path.Combine(vesselFolder, vesselID + ".txt"));
                }
            }
            //Save scenario data
            ConfigNode[] scenarioNodes = gameData.GetNodes("SCENARIO");
            if (scenarioNodes != null)
            {
                foreach (ConfigNode cn in scenarioNodes)
                {
                    string scenarioName = cn.GetValue("name");
                    DarkLog.Debug("Saving scenario: " + scenarioName);
                    cn.Save(Path.Combine(playerScenarioFolder, scenarioName + ".txt"));
                }
            }
            //Save kerbal data
            ConfigNode[] kerbalNodes = gameData.GetNode("ROSTER").GetNodes("CREW");
            if (kerbalNodes != null)
            {
                int kerbalIndex = 0;
                foreach (ConfigNode cn in kerbalNodes)
                {
                    DarkLog.Debug("Saving kerbal " + kerbalIndex + ", name: " + cn.GetValue("name"));
                    cn.Save(Path.Combine(kerbalFolder, kerbalIndex + ".txt"));
                    kerbalIndex++;
                }
            }
            DarkLog.Debug("Generated KSP_folder/Universe from " + saveName);
            ScreenMessages.PostScreenMessage("Generated KSP_folder/Universe from " + saveName, 5f, ScreenMessageStyle.UPPER_CENTER);
        }

        public static string[] GetSavedNames()
        {
            List<string> returnList = new List<string>();
            string[] possibleSaves = Directory.GetDirectories(savesFolder);
            foreach (string saveDirectory in possibleSaves)
            {
                string trimmedDirectory = saveDirectory;
                //Cut the trailing path character off if we need to
                if (saveDirectory[saveDirectory.Length - 1] == Path.DirectorySeparatorChar)
                {
                    trimmedDirectory = saveDirectory.Substring(0, saveDirectory.Length - 2);
                }
                string saveName = trimmedDirectory.Substring(trimmedDirectory.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                if (saveName.ToLower() != "training" && saveName.ToLower() != "scenarios")
                {
                    if (File.Exists(Path.Combine(saveDirectory, "persistent.sfs")))
                    {
                        returnList.Add(saveName);
                    }
                }
            }
            return returnList.ToArray();
        }
    }
}

