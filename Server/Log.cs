using System;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class DarkLog
    {
        private static string LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static string LogFilename = Path.Combine(LogFolder, "dmpserver " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log");
        private static object logLock = new object();

        public enum LogLevels : int
        {
            DEBUG = 0,
            INFO = 1,
            ERROR = 2,
            CHAT = 3,
        }

        private static void WriteLog(LogLevels level, string message)
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
                Console.WriteLine(output);
                try
                {
                    lock (logLock) {
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

        public static void Debug(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            WriteLog(LogLevels.DEBUG, message);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public static void Normal(string message)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            WriteLog(LogLevels.INFO, message);
        }

        public static void Error(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            WriteLog(LogLevels.ERROR, message);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public static void ChatMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            WriteLog(LogLevels.CHAT, message);
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }
}

