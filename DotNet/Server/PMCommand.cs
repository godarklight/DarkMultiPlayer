using System;

namespace DarkMultiPlayerServer
{
    public class PMCommand
    {
        public static void HandleCommand(string commandArgs)
        {
            ClientObject pmPlayer = null;
            int matchedLength = 0;
            foreach (ClientObject testPlayer in ClientHandler.GetClients())
            {
                //Only search authenticated players
                if (testPlayer.authenticated)
                {
                    //Try to match the longest player name
                    if (commandArgs.StartsWith(testPlayer.playerName, StringComparison.Ordinal) && testPlayer.playerName.Length > matchedLength)
                    {
                        //Double check there is a space after the player name
                        if ((commandArgs.Length > (testPlayer.playerName.Length + 1)) ? commandArgs[testPlayer.playerName.Length] == ' ' : false)
                        {
                            pmPlayer = testPlayer;
                            matchedLength = testPlayer.playerName.Length;
                        }
                    }
                }
            }
            if (pmPlayer != null)
            {
                string messageText = commandArgs.Substring(pmPlayer.playerName.Length + 1);
                Messages.Chat.SendChatMessageToClient(pmPlayer, messageText);
            }
            else
            {
                DarkLog.Normal("Player not found!");
            }
        }
    }
}

