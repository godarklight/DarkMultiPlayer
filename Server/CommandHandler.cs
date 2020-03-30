using System;
using System.Threading;
using System.Collections.Generic;

namespace DarkMultiPlayerServer
{
    public class CommandHandler
    {
        private static Dictionary<string, Command> commands = new Dictionary<string, Command>();

        public static void ThreadMain()
        {
            try
            {
                //Register commands
                CommandHandler.RegisterCommand("help", CommandHandler.DisplayHelp, "Displays this help");
                CommandHandler.RegisterCommand("say", CommandHandler.Say, "Broadcasts a message to clients");
                CommandHandler.RegisterCommand("dekessler", Dekessler.RunDekessler, "Clears out debris from the server");
                CommandHandler.RegisterCommand("nukeksc", NukeKSC.RunNukeKSC, "Clears ALL vessels from KSC and the Runway");
                CommandHandler.RegisterCommand("listclients", ListClients, "Lists connected clients");
                CommandHandler.RegisterCommand("countclients", CountClients, "Counts connected clients");
                CommandHandler.RegisterCommand("editgroup", Groups.fetch.EditGroupCommand, "Sets the group to edit");
                CommandHandler.RegisterCommand("addplayer", Groups.fetch.AddPlayerToGroupCommand, "Adds player to group, first player becomes admin");
                CommandHandler.RegisterCommand("removeplayer", Groups.fetch.RemovePlayerFromGroupCommand, "Removes player from group");
                CommandHandler.RegisterCommand("addadmin", Groups.fetch.AddPlayerAdminCommand, "Adds admin to group");
                CommandHandler.RegisterCommand("removeadmin", Groups.fetch.RemovePlayerAdminCommand, "Removes admin from group");
                CommandHandler.RegisterCommand("showgroups", Groups.fetch.ShowGroupsCommand, "Shows group database");
                CommandHandler.RegisterCommand("editvessel", Permissions.fetch.EditVesselCommand, "Edits vessel permissions, takes same ID from Universe/Vessels/");
                CommandHandler.RegisterCommand("vesselowner", Permissions.fetch.SetVesselOwnerCommand, "Sets a vessel owner");
                CommandHandler.RegisterCommand("vesselgroup", Permissions.fetch.SetVesselGroupCommand, "Sets a vessels group");
                CommandHandler.RegisterCommand("vesselprotection", Permissions.fetch.SetVesselProtectionCommand, "Sets vessel protection level. Valid levels: public, group, private.");
                CommandHandler.RegisterCommand("showvessels", Permissions.fetch.ShowVesselsCommand, "Shows vessel protection database");

                //Main loop
                while (Server.serverRunning)
                {
                    string input = "";
                    try
                    {
                        input = Console.ReadLine();
                        if (input == null)
                        {
                            DarkLog.Debug("Terminal may be not attached or broken, Exiting out of command handler");
                            return;
                        }
                    }
                    catch
                    {
                        if (Server.serverRunning)
                        {
                            DarkLog.Debug("Ignored mono Console.ReadLine() bug");
                        }
                        Thread.Sleep(500);
                    }
                    DarkLog.Normal("Command input: " + input);
                    if (input.StartsWith("/", StringComparison.Ordinal))
                    {
                        HandleServerInput(input.Substring(1));
                    }
                    else
                    {
                        if (input != "")
                        {
                            commands["say"].func(input);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (Server.serverRunning)
                {
                    DarkLog.Fatal("Error in command handler thread, Exception: " + e);
                    throw;
                }
            }
        }

        public static void HandleServerInput(string input)
        {
            string commandPart = input;
            string argumentPart = "";
            if (commandPart.Contains(" "))
            {
                if (commandPart.Length > commandPart.IndexOf(' ') + 1)
                {
                    argumentPart = commandPart.Substring(commandPart.IndexOf(' ') + 1);
                }
                commandPart = commandPart.Substring(0, commandPart.IndexOf(' '));
            }
            if (commandPart.Length > 0)
            {
                if (commands.ContainsKey(commandPart))
                {
                    try
                    {
                        commands[commandPart].func(argumentPart);
                    }
                    catch (Exception e)
                    {
                        DarkLog.Error("Error handling server command " + commandPart + ", Exception " + e);
                    }
                }
                else
                {
                    DarkLog.Normal("Unknown server command: " + commandPart);
                }
            }
        }

        public static void RegisterCommand(string command, Action<string> func, string description)
        {
            Command cmd = new Command(command, func, description);
            if (!commands.ContainsKey(command))
            {
                commands.Add(command, cmd);
            }
        }

        private static void DisplayHelp(string commandArgs)
        {
            List<Command> commands = new List<Command>();
            int longestName = 0;
            foreach (Command cmd in CommandHandler.commands.Values)
            {
                commands.Add(cmd);
                if (cmd.name.Length > longestName)
                {
                    longestName = cmd.name.Length;
                }
            }
            foreach (Command cmd in commands)
            {
                DarkLog.Normal(cmd.name.PadRight(longestName) + " - " + cmd.description);
            }
        }

        private static void Say(string sayText)
        {
            DarkLog.Normal("Broadcasting " + sayText);
            Messages.Chat.SendChatMessageToAll(sayText);
        }

        private static void ListClients(string commandArgs)
        {
            if (Server.players != "")
            {
                DarkLog.Normal("Online players: " + Server.players);
            }
            else
            {
                DarkLog.Normal("No clients connected");
            }
        }

        private static void CountClients(string commandArgs)
        {
            DarkLog.Normal("Online players: " + Server.playerCount);
        }

        private class Command
        {
            public string name;
            public Action<string> func;
            public string description;

            public Command(string name, Action<string> func, string description)
            {
                this.name = name;
                this.func = func;
                this.description = description;
            }
        }
    }
}

