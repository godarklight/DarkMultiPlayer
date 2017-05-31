using System;
using System.IO;
using System.Net;
using DarkMultiPlayerCommon;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
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
        // General Options
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
        [Description("Re-Entry Heating")]
        public float reentryHeatScale = 1.0f;
        [Description("Resource Abundance")]
        public float resourceAbundance = 1.0f;
        [Description("Allow Quickloading and Reverting Flights\nNote that if set to true and warp mode isn't SUBSPACE, it will have no effect")]
        public bool canQuickLoad = true;
        [Description("Enable Comm Network")]
        public bool commNetwork = true;
        [Description("Crew Respawn Time")]
        public float respawnTime = 2f;
        // Career Options
        [Description("Funds Rewards")]
        public float fundsGainMultiplier = 1.0f;
        [Description("Funds Penalties")]
        public float fundsLossMultiplier = 1.0f;
        [Description("Reputation Rewards")]
        public float repGainMultiplier = 1.0f;
        [Description("Reputation Penalties")]
        public float repLossMultiplier = 1.0f;
        [Description("Decline Penalty")]
        public float repLossDeclined = 1.0f;
        [Description("Science Rewards")]
        public float scienceGainMultiplier = 1.0f;
        [Description("Starting Funds")]
        public float startingFunds = 25000.0f;
        [Description("Starting Reputation")]
        public float startingReputation = 0.0f;
        [Description("Starting Science")]
        public float startingScience = 0.0f;
        // Advanced Options
        [Description("Enable Kerbal Exp")]
        public bool kerbalExp = true;
        [Description("Kerbals Level Up Immediately")]
        public bool immediateLevelUp = false;
        [Description("Allow Negative Currency")]
        public bool allowNegativeCurrency = false;
        [Description("Obey Crossfeed Rules")]
        public bool obeyCrossfeedRules = false;
        [Description("Building Damage Multiplier")]
        public float buildingDamageMultiplier = 0.05f;
        [Description("Part Upgrades")]
        public bool partUpgrades = true;
        [Description("Should parts fail when they have exceeded their pressure/G-force limit, and kerbals go unconscious after sustaining too much G-force?")]
        public bool partPressureFail = true;
        [Description("Multiplier to how much G-force Kerbals tolerate before going unconscious")]
        public float kerbalGToleranceMultiplier = 1f;
        // CommNet Options
        [Description("Require Signal for Control")]
        public bool requireSignalForControl = false;
        [Description("Enable comm links weakening and going down when the link goes through atmospheric plasma.")]
        public bool plasmaBlackout = true;
        [Description("Range Modifier")]
        public float rangeModifier = 1.0f;
        [Description("DSN Modifier")]
        public float dsnModifier = 1.0f;
        [Description("Occlusion Modifier, Vac")]
        public float occlusionModifierVac = 0.9f;
        [Description("Occlusion Modifier, Atm")]
        public float occlusionModifierAtm = 0.75f;
        [Description("Enable Extra Groundstations")]
        public bool extraGroundstations = true;
    }
}
