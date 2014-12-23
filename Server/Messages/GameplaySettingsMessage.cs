using System;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class GameplaySettingsMessage
    {
        public static void HandleGameplaySettingsRequest(ClientObject client)
        {
            SendGameplaySettings(client);
        }

        public static void SendGameplaySettings(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.GAMEPLAY_SETTINGS_REPLY;
            using (MessageWriter mw = new MessageWriter())
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
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
            DarkLog.Debug("Sending " + client.playerName + " gameplay settings");
        }
    }
}
