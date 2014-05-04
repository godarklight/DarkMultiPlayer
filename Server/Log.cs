using System;

namespace DarkMultiPlayerServer
{
    public class DarkLog
    {
        public static void Debug(string message)
        {
            float currentTime = Server.serverClock.ElapsedMilliseconds / 1000f;
            Console.WriteLine("[" + currentTime + "] Debug: " + message);
        }
        public static void Normal(string message)
        {
            float currentTime = Server.serverClock.ElapsedMilliseconds / 1000f;
            Console.WriteLine("[" + currentTime + "] Normal: " + message);
        }
    }
}

