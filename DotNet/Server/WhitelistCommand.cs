using System;

namespace DarkMultiPlayerServer
{
    public class WhitelistCommand
    {
        public static void HandleCommand(string commandArgs)
        {
            string func = "";
            string playerName = "";

            func = commandArgs;
            if (commandArgs.Contains(" "))
            {
                func = commandArgs.Substring(0, commandArgs.IndexOf(" ", StringComparison.Ordinal));
                if (commandArgs.Substring(func.Length).Contains(" "))
                {
                    playerName = commandArgs.Substring(func.Length + 1);
                }
            }

            switch (func)
            {
                default:
                    DarkLog.Debug("Undefined function. Usage: /whitelist [add|del] playername or /whitelist show");
                    break;
                case "add":
                    if (!WhitelistSystem.fetch.IsWhitelisted(playerName))
                    {
                        DarkLog.Normal("Added '" + playerName + "' to whitelist.");
                        WhitelistSystem.fetch.AddPlayer(playerName);
                    }
                    else
                    {
                        DarkLog.Normal("'" + playerName + "' is already on the whitelist.");
                    }
                    break;
                case "del":
                    if (WhitelistSystem.fetch.IsWhitelisted(playerName))
                    {
                        DarkLog.Normal("Removed '" + playerName + "' from the whitelist.");
                        WhitelistSystem.fetch.RemovePlayer(playerName);
                    }
                    else
                    {
                        DarkLog.Normal("'" + playerName + "' is not on the whitelist.");
                    }
                    break;
                case "show":
                    foreach (string player in WhitelistSystem.fetch.GetWhiteList())
                    {
                        DarkLog.Normal(player);
                    }
                    break;
            }
        }
    }
}

