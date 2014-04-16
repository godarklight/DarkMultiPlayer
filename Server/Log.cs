using System;

namespace DarkMultiPlayerServer
{
    public class DarkLog
    {
        public static void Debug(string message)
        {
#if DEBUG
            float currentTime = Server.serverClock.ElapsedMilliseconds / 1000f;
            Console.WriteLine("[" + currentTime + "] DarkMultiPlayer Debug: " + message);
#endif
        }
        public static void Normal(string message)
        {
            float currentTime = Server.serverClock.ElapsedMilliseconds / 1000f;
            Console.WriteLine("[" + currentTime + "] " + message);
        }
    }
}

