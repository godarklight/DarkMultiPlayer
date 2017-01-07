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

            messageQueue.Enqueue(string.Format("DarkMultiPlayer: {0}", message));
        }

        public static void Update()
        {
            while (messageQueue.Count > 0)
            {
                string message = messageQueue.Dequeue();
                UnityEngine.Debug.Log(string.Format("[{0}] {1}", Time.realtimeSinceStartup, message));
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
