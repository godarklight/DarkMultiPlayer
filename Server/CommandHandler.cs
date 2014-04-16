using System;
using System.Threading;

namespace DarkMultiPlayerServer
{
    public class CommandHandler
    {
        public static void ThreadMain()
        {
            while (Server.serverRunning)
            {
                Thread.Sleep(10);
            }
        }
    }
}

