using System;

namespace DarkMultiPlayerServer
{
    public class KickCommand
    {
        public static void KickPlayer(string commandArgs)
        {
            string playerName = commandArgs;
            string reason = "";
            if (commandArgs.Contains(" "))
            {
                playerName = commandArgs.Substring(0, commandArgs.IndexOf(" ", StringComparison.Ordinal));
                reason = commandArgs.Substring(commandArgs.IndexOf(" ", StringComparison.Ordinal) + 1);
            }
            ClientObject player = null;

            if (playerName != "")
            {
                player = ClientHandler.GetClientByName(playerName);
                if (player != null)
                {
                    DarkLog.Normal("Kicking " + playerName + " from the server");
                    if (reason != "")
                    {
                        Messages.ConnectionEnd.SendConnectionEnd(player, "Kicked from the server, " + reason);
                    }
                    else
                    {
                        Messages.ConnectionEnd.SendConnectionEnd(player, "Kicked from the server");
                    }
                }
            }
            else
            {
                DarkLog.Error("Syntax error. Usage: /kick playername [reason]");
            }
        }
    }
}

