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
                if (Settings.settingsStore.gameDifficulty == GameDifficulty.CUSTOM)
                {
                    mw.Write<bool>(GameplaySettings.settingsStore.allowStockVessels);
                    mw.Write<bool>(GameplaySettings.settingsStore.autoHireCrews);
                    mw.Write<bool>(GameplaySettings.settingsStore.bypassEntryPurchaseAfterResearch);
                    mw.Write<bool>(GameplaySettings.settingsStore.missingCrewsRespawn);
                    mw.Write<bool>(GameplaySettings.settingsStore.indestructibleFacilities);
                    mw.Write<float>(GameplaySettings.settingsStore.fundsGainMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.fundsLossMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.repGainMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.repLossMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.scienceGainMultiplier);
                    mw.Write<float>(GameplaySettings.settingsStore.startingFunds);
                    mw.Write<float>(GameplaySettings.settingsStore.startingReputation);
                    mw.Write<float>(GameplaySettings.settingsStore.startingScience);
                }
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }
    }
}

