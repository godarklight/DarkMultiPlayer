using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DarkMultiPlayerServer
{
    class DMPShutdownCommand
    {
        public static void ShutDown(string commandArgs)
        {
            if (commandArgs != "")
            {
                DarkLog.Normal("Shutting down - " + commandArgs);
                ClientHandler.SendConnectionEndToAll("Server is shutting down - " + commandArgs);
            }
            else
            {
                DarkLog.Normal("Shutting down");
                ClientHandler.SendConnectionEndToAll("Server is shutting down");
            }
            Server.serverStarting = false;
            Server.serverRunning = false;
            Server.StopHTTPServer();
        }
    }
}
