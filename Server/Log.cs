using System;
using System.IO;
using System.Collections.Generic;

namespace DarkMultiPlayerServer
{
    public class DarkLog
    {
        private static string LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static string LogFilename = Path.Combine(LogFolder, "dmpserver " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log");
        private static object logLock = new object();
        private static Queue<string> logQueue = new Queue<string>();

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

                if (level == LogLevels.INFO)
                {
                    tag = " Server: ";
                }
                else
                {
                    tag = "[" + level.ToString() + "] Server: ";
                }
                
                if (Settings.settingsStore.useUTCTimeInLog)
                {
                    output = "[" + DateTime.UtcNow.ToString("HH:mm:ss") + "]" + tag + message;
                }
                else
                {
                    output = "[" + DateTime.Now.ToString("HH:mm:ss") + "]" + tag + message;
                }

                //Dirty evil flow control hack soup
                try
                {
                    ClientHandler.SendConsoleMessageToAdmins(output);
                    while (logQueue.Count > 0)
                    {
                        string queuedOutput = logQueue.Dequeue();
                        ClientHandler.SendConsoleMessageToAdmins(queuedOutput);
                    }
                }
                catch
                {
                    //If the ClientHandler is not ready yet, queue up the messages instead
                    logQueue.Enqueue(output);
                }

                try
                {
                    lock (logLock) {
                        File.AppendAllText(LogFilename, output + Environment.NewLine);
                    }
                }
                catch (Exception e)
                {
                    logQueue.Enqueue("Error writing to log file!, Exception: " + e);
                }
            }
        }

        public static void Debug(string message)
        {
            WriteLog(LogLevels.DEBUG, message);
        }

        public static void Normal(string message)
        {
            WriteLog(LogLevels.INFO, message);
        }

        public static void Error(string message)
        {
            WriteLog(LogLevels.ERROR, message);
        }

        public static void Fatal(string message)
        {
            WriteLog(LogLevels.FATAL, message);
        }

        public static void ChatMessage(string message)
        {
            WriteLog(LogLevels.CHAT, message);
        }
    }
}

