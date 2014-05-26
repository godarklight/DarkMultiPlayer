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

                //Main loop
                while (Server.serverRunning)
                {
                    string input = Console.ReadLine();
                    DarkLog.Normal("Command input: " + input);
                    if (input.StartsWith("/"))
                    {
                        string commandPart = input.Substring(1);
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
                                    DarkLog.Error("Error handling command " + commandPart + ", Exception " + e);
                                }
                            }
                            else
                            {
                                DarkLog.Normal("Unknown command: " + commandPart);
                            }
                        }
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
            commands.Sort();
            foreach (Command cmd in commands)
            {
                DarkLog.Normal(cmd.name.PadRight(longestName) + " - " + cmd.description);
            }
        }

        private static void Say(string sayText)
        {
            DarkLog.Normal("Broadcasting " + sayText);
            ClientHandler.SendChatMessageToAll(sayText);
        }

        private class Command : IComparable
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

            public int CompareTo(object obj)
            {
                var cmd = obj as Command;
                return this.name.CompareTo(cmd.name);
            }
        }
    }
}

