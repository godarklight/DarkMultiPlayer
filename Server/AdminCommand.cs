using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer
{
    public class AdminCommand
    {
        public static void HandleCommand(string commandArgs)
        {
            string func = "";
            string playerName = "";

            func = commandArgs;
            if (commandArgs.Contains(" "))
            {
                func = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                if (commandArgs.Substring(func.Length).Contains(" "))
                {
                    playerName = commandArgs.Substring(func.Length + 1);
                }
            }

            switch (func)
            {
                default:
                    DarkLog.Normal("Undefined function. Usage: /admin [add|del] playername or /admin show");
                    break;
                case "add":
                    if (File.Exists(Path.Combine(Server.universeDirectory, "Players", playerName + ".txt")))
                    {
                        if (!AdminSystem.fetch.IsAdmin(playerName))
                        {
                            DarkLog.Debug("Added '" + playerName + "' to admin list.");
                            AdminSystem.fetch.AddAdmin(playerName);
                            //Notify all players an admin has been added
                            ServerMessage newMessage = new ServerMessage();
                            newMessage.type = ServerMessageType.ADMIN_SYSTEM;
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write<int>((int)AdminMessageType.ADD);
                                mw.Write<string>(playerName);
                                newMessage.data = mw.GetMessageBytes();
                            }
                            ClientHandler.SendToAll(null, newMessage, true);
                        }
                        else
                        {
                            DarkLog.Normal("'" + playerName + "' is already an admin.");
                        }

                    }
                    else
                    {
                        DarkLog.Normal("'" + playerName + "' does not exist.");
                    }
                    break;
                case "del":
                    if (AdminSystem.fetch.IsAdmin(playerName))
                    {
                        DarkLog.Normal("Removed '" + playerName + "' from the admin list.");
                        AdminSystem.fetch.RemoveAdmin(playerName);
                        //Notify all players an admin has been removed
                        ServerMessage newMessage = new ServerMessage();
                        newMessage.type = ServerMessageType.ADMIN_SYSTEM;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<int>((int)AdminMessageType.REMOVE);
                            mw.Write<string>(playerName);
                            newMessage.data = mw.GetMessageBytes();
                        }
                        ClientHandler.SendToAll(null, newMessage, true);

                    }
                    else
                    {
                        DarkLog.Normal("'" + playerName + "' is not an admin.");
                    }
                    break;
                case "show":
                    foreach (string player in AdminSystem.fetch.GetAdmins())
                    {
                        DarkLog.Normal(player);
                    }
                    break;
            }
        }
    }
}

