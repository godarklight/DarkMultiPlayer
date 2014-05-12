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
            //Init
            CommandHandler.RegisterCommand("help", (x) => CommandHandler.DisplayHelp(), "Displays this help");

            //Main loop
            while (Server.serverRunning)
            {
                string input = Console.ReadLine();
                string[] split = input.Split(new char[]{' '});
                if (split.Length > 0)
                {
                    string command = split[0];
                    if (commands.ContainsKey(command))
                    {
                        commands[command].func(split);
                    }
                    else
                    {
                        Console.WriteLine("Unknown command: " + command);
                    }
                }
            }
        }

        public static void RegisterCommand(string command, Action<string[]> func, string description)
        {
            Command cmd = new Command(command, func, description);
            commands.Add(command, cmd);
        }

        public static void RegisterCommand(string command, Action<string[]> func)
        {
            RegisterCommand(command, func, "");
        }

        private static void DisplayHelp()
        {
            List<Command> commands = new List<Command>();
            int longestName = 0;
            foreach (Command cmd in CommandHandler.commands.Values)
            {
                commands.Add(cmd);
                if (cmd.name.Length > longestName)
                    longestName = cmd.name.Length;
            }
            commands.Sort();
            foreach (Command cmd in commands)
            {
                if (cmd.description != "")
                {
                    Console.WriteLine("{0,-" + (longestName) + "} - {1}", cmd.name, cmd.description);
                }
                else
                {
                    Console.WriteLine(cmd.name);
                }
            }
        }

        private class Command : IComparable
        {
            public string name;
            public Action<string[]> func;
            public string description;

            public Command(string name, Action<string[]> func, string description)
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

