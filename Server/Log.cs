using System;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class DarkLog
    {
        private static string LogFolder = "logs";
        private static string LogFilename = Path.Combine(LogFolder, "dmpserver " + DateTime.Now.ToString("dd-MM-yyyy HH-mm-ss") + ".dlog");

        public enum LogLevels : int
        {
            DEBUG = 0,
            INFO = 1,
            ERROR = 2,
        }

        public static LogLevels minLogLevel { get; set; }

        private static void WriteLog(LogLevels level, string message)
        {

            if (!Directory.Exists(LogFolder))
                Directory.CreateDirectory(LogFolder);

            string output;

            float currentTime = Server.serverClock.ElapsedMilliseconds / 1000f;

            if (level < minLogLevel) { return; }

            if (Settings.useUTCTimeInLog == true)
                output = String.Format("[{0}][{1}] : {2}", DateTime.Now.ToString("HH:mm:ss"), level.ToString(), message);
            else
                output = String.Format("[{0}][{1}] : {2}", currentTime, level.ToString(), message);
            

            Console.WriteLine(output);
            try
            {
                File.AppendAllText(LogFilename, output + Environment.NewLine);
            }
            catch
            { }

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
    }
}

