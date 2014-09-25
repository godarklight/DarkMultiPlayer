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

        public static void Debug(string message)
        {
            //Use messageQueue if looking for messages that don't normally show up in the log.

            messageQueue.Enqueue("[" + UnityEngine.Time.realtimeSinceStartup + "] DarkMultiPlayer: " + message);
            //UnityEngine.Debug.Log("[" + UnityEngine.Time.realtimeSinceStartup + "] DarkMultiPlayer: " + message);
        }

        public static void Update()
        {
            while (messageQueue.Count > 0)
            {
                string message = messageQueue.Dequeue();
                ChatWorker.fetch.QueueSystemMessage(message);
                /*
                using (StreamWriter sw = new StreamWriter("DarkLog.txt", true, System.Text.Encoding.UTF8)) {
                    sw.WriteLine(message);
                }
                */
            }
        }

        public static void ExternalLog(string debugText)
        {
            lock (externalLogLock)
            {
                using (StreamWriter sw = new StreamWriter(Path.Combine(KSPUtil.ApplicationRootPath, "DMP.log"), true))
                {
                    sw.WriteLine(debugText);
                }
            }
        }
    }
}
