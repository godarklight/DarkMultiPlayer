using System;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class DarkLog
    {
        private static string LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static string LogFilename = Path.Combine(LogFolder, "dmpserver " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log");
        private static object logLock = new object();

        public enum LogLevels
        {
            DEBUG,
            INFO,
            CHAT,
            ERROR,
            FATAL
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
                string tag;
                if (Settings.settingsStore.useUTCTimeInLog)
                {
                    tag = "[" + DateTime.UtcNow.ToString("HH:mm:ss") + "][" + level.ToString() + "] : ";
                }
                else
                {
                    tag = "[" + DateTime.Now.ToString("HH:mm:ss") + "][" + level.ToString() + "] : ";
                }
                output = tag + message;

                //Send messages to admin consoles or terminal console dependent upon the availablility of an admin.
                //This is ugly.
                try
                {
                    ClientHandler.SendConsoleMessageToAdmins(output);
                }
                catch
                {
                    Console.WriteLine(output);
                }


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

        public static void Fatal(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            WriteLog(LogLevels.FATAL, message);
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

