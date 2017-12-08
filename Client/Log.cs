using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class DarkLog
    {
        public static Queue<string> messageQueue = new Queue<string>();
        private static object externalLogLock = new object();
        private static int mainThreadID;

        public static void SetMainThread()
        {
            mainThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        public static void Debug(string message)
        {
            message = string.Format("[{0}] {1}", Client.realtimeSinceStartup, message);
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadID)
            {
                UnityEngine.Debug.Log("DarkMultiPlayer: " + message);
            }
            else
            {
                lock (messageQueue)
                {
                    messageQueue.Enqueue("DarkMultiPlayer: [THREAD] " + message);
                }
            }
        }

        public static void Update()
        {
            while (messageQueue.Count > 0)
            {
                lock (messageQueue)
                {
                    string message = messageQueue.Dequeue();
                    UnityEngine.Debug.Log(message);
                }
            }
        }

        public static void ExternalLog(string debugText)
        {
            lock (externalLogLock)
            {
                using (StreamWriter sw = new StreamWriter(Path.Combine(Client.dmpClient.kspRootPath, "DMP.log"), true))
                {
                    sw.WriteLine(debugText);
                }
            }
        }
    }
}
