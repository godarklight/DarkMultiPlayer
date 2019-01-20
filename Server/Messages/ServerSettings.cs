using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class ServerSettings
    {
        public static void SendServerSettings(ClientObject client)
        {
            int numberOfKerbals = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Kerbals")).Length;
            int numberOfVessels = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels")).Length;
            int numberOfScenarioModules = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", client.playerName)).Length;
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.SERVER_SETTINGS;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)Settings.settingsStore.warpMode);
                mw.Write<int>((int)Settings.settingsStore.gameMode);
                mw.Write<bool>(Settings.settingsStore.cheats);
                //Tack the amount of kerbals, vessels and scenario modules onto this message
                mw.Write<int>(numberOfKerbals);
                mw.Write<int>(numberOfVessels);
                //mw.Write<int>(numberOfScenarioModules);
                mw.Write<int>(Settings.settingsStore.screenshotHeight);
                mw.Write<int>(Settings.settingsStore.numberOfAsteroids);
                mw.Write<string>(Settings.settingsStore.consoleIdentifier);
                mw.Write<int>((int)Settings.settingsStore.gameDifficulty);
                mw.Write<float>(Settings.settingsStore.safetyBubbleDistance);

                if (Settings.settingsStore.gameDifficulty == GameDifficulty.CUSTOM)
                {
                    mw.Write<bool>(GameplaySettings.settingsStore.allowStockVessels);
                    mw.Write<bool>(GameplaySettings.settingsStore.autoHireCrews);
                    mw.Write<bool>(GameplaySettings.settingsStore.bypassEntryPurchaseAfterResearch);
                    mw.Write<bool>(GameplaySettings.settingsStore.indestructibleFacilities);
                    mw.Write<bool>(GameplaySettings.settingsStore.missingCrewsRespawn);
                    mw.Write<float>(GameplaySettings.settingsStore.reentryHeatScale);
                    mw.Write<float>(GameplaySettings.settingsStore.resourceAbundance);
                    mw.Write<bool>(GameplaySettings.settingsStore.canQuickLoad);
                    mw.Write<float>(GameplaySettings.settingsStore.fundsGainMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.fundsLossMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.repGainMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.repLossMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.repLossDeclined);
                    mw.Write<float>(GameplaySettings.settingsStore.scienceGainMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.startingFunds);
                    mw.Write<float>(GameplaySettings.settingsStore.startingReputation);
                    mw.Write<float>(GameplaySettings.settingsStore.startingScience);
                    //New KSP 1.2 Settings
                    mw.Write<float>(GameplaySettings.settingsStore.respawnTime);
                    mw.Write<bool>(GameplaySettings.settingsStore.commNetwork);
                    mw.Write<bool>(GameplaySettings.settingsStore.kerbalExp);
                    mw.Write<bool>(GameplaySettings.settingsStore.immediateLevelUp);
                    mw.Write<bool>(GameplaySettings.settingsStore.allowNegativeCurrency);
                    mw.Write<bool>(GameplaySettings.settingsStore.obeyCrossfeedRules);
                    mw.Write<float>(GameplaySettings.settingsStore.buildingDamageMultiplier);
                    mw.Write<bool>(GameplaySettings.settingsStore.partUpgrades);
                    mw.Write<bool>(GameplaySettings.settingsStore.partPressureFail);
                    mw.Write<float>(GameplaySettings.settingsStore.kerbalGToleranceMultiplier);
                    mw.Write<bool>(GameplaySettings.settingsStore.requireSignalForControl);
                    mw.Write<bool>(GameplaySettings.settingsStore.plasmaBlackout);
                    mw.Write<float>(GameplaySettings.settingsStore.rangeModifier);
                    mw.Write<float>(GameplaySettings.settingsStore.dsnModifier);
                    mw.Write<float>(GameplaySettings.settingsStore.occlusionModifierVac);
                    mw.Write<float>(GameplaySettings.settingsStore.occlusionModifierAtm);
                    mw.Write<bool>(GameplaySettings.settingsStore.extraGroundstations);
                }
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

