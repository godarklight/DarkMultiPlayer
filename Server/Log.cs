using System;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class DarkLog
    {
        public static string LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        public static string LogFilename = Path.Combine(LogFolder, "dmpserver " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log");
        private static object logLock = new object();

        public enum LogLevels
        {
            DEBUG,
            INFO,
            CHAT,
            ERROR,
            FATAL
        }

        private static void WriteLog(LogLevels level, string message, bool sendToConsole)
        {
            if (!Directory.Exists(LogFolder))
            {
                Directory.CreateDirectory(LogFolder);
            }

            if (level >= Settings.settingsStore.logLevel)
            {
                string output;
                if (Settings.settingsStore.useUTCTimeInLog)
                {
                    output = "[" + DateTime.UtcNow.ToString("HH:mm:ss") + "][" + level.ToString() + "] : " + message;
                }
                else
                {
                    output = "[" + DateTime.Now.ToString("HH:mm:ss") + "][" + level.ToString() + "] : " + message;
                }
                if (sendToConsole)
                {
                    Console.WriteLine(output);
                    Messages.Chat.SendConsoleMessageToAdmins(output);
                }
                try
                {
                    lock (logLock)
                    {
                        File.AppendAllText(LogFilename, output + Environment.NewLine);
                    }
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine("Error writing to log file!, Exception: " + e);
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
            }
        }

        public static void WriteToLog(string message)
        {
            WriteLog(LogLevels.INFO, message, false);
        }

        public static void Debug(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            WriteLog(LogLevels.DEBUG, message, true);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public static void Normal(string message)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            WriteLog(LogLevels.INFO, message, true);
        }

        public static void Error(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            WriteLog(LogLevels.ERROR, message, true);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public static void Fatal(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            WriteLog(LogLevels.FATAL, message, true);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public static void ChatMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            WriteLog(LogLevels.CHAT, message, true);
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }
}

