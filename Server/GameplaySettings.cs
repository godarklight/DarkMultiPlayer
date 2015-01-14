using System;
using System.IO;
using System.Net;
using DarkMultiPlayerCommon;
using System.Reflection;
using System.Collections.Generic;
using SettingsParser;

namespace DarkMultiPlayerServer
{
    public class GameplaySettings
    {
        private static ConfigParser<GameplaySettingsStore> gameplaySettings;
        public static GameplaySettingsStore settingsStore
        {
            get
            {
                if (gameplaySettings == null)
                {
                    return null;
                }
                return gameplaySettings.Settings;
            }
        }

        public static void Reset()
        {
            gameplaySettings = new ConfigParser<GameplaySettingsStore>(new GameplaySettingsStore(), Path.Combine(Server.configDirectory, "GameplaySettings.txt"));
        }

        public static void Load()
        {
            gameplaySettings.LoadSettings();
        }

        public static void Save()
        {
            gameplaySettings.SaveSettings();
        }
    }

    public class GameplaySettingsStore
    {
        [Description("Allow Stock Vessels")]
        public bool allowStockVessels = false;
        [Description("Auto-Hire Crewmemebers before Flight")]
        public bool autoHireCrews = true;
        [Description("No Entry Purchase Required on Research")]
        public bool bypassEntryPurchaseAfterResearch = true;
        [Description("Indestructible Facilities")]
        public bool indestructibleFacilities = false;
        [Description("Missing Crews Respawn")]
        public bool missingCrewsRespawn = true;
        // Career Settings
        [Description("Funds Rewards")]
        public float fundsGainMultiplier = 1.0f;
        [Description("Funds Penalties")]
        public float fundsLossMultiplier = 1.0f;
        [Description("Reputation Rewards")]
        public float repGainMultiplier = 1.0f;
        [Description("Reputation Penalties")]
        public float repLossMultiplier = 1.0f;
        [Description("Science Rewards")]
        public float scienceGainMultiplier = 1.0f;
        [Description("Starting Funds")]
        public float startingFunds = 25000.0f;
        [Description("Starting Reputation")]
        public float startingReputation = 0.0f;
        [Description("Starting Science")]
        public float startingScience = 0.0f;
    }
}
