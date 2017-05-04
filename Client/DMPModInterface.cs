using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayer
{
    public delegate void DMPMessageCallback(byte[] messageData);

    public class QueuedDMPMessage
    {
        public string modName;
        public byte[] messageData;
    }

    public class DMPModInterface
    {
        //TODO: Fix event firing
        //Registered methods
        private Dictionary<string, DMPMessageCallback> registeredRawMods = new Dictionary<string, DMPMessageCallback>();
        private Dictionary<string, DMPMessageCallback> registeredUpdateMods = new Dictionary<string, DMPMessageCallback>();
        private Dictionary<string, DMPMessageCallback> registeredFixedUpdateMods = new Dictionary<string, DMPMessageCallback>();
        //Delay queues - Apparently ConcurrentQueue isn't supported in .NET 3.5 :(
        private Dictionary<string, Queue<byte[]>> updateQueue = new Dictionary<string, Queue<byte[]>>();
        private Dictionary<string, Queue<byte[]>> fixedUpdateQueue = new Dictionary<string, Queue<byte[]>>();
        //Protect against threaded access
        private object eventLock = new object();
        //Services
        private NetworkWorker networkWorker;

        internal void DMPRun(NetworkWorker networkWorker)
        {
            this.networkWorker = networkWorker;
        }

        internal void DMPStop()
        {
            this.networkWorker = null;
        }

        /// <summary>
        /// Unregisters a mod handler.
        /// </summary>
        /// <returns><c>true</c>, if mod handler was unregistered, <c>false</c> otherwise.</returns>
        /// <param name="modName">Mod name.</param>
        public bool UnregisterModHandler(string modName)
        {
            bool unregistered = false;
            lock (eventLock)
            {
                if (registeredRawMods.ContainsKey(modName))
                {
                    registeredRawMods.Remove(modName);
                    unregistered = true;
                }
                if (registeredUpdateMods.ContainsKey(modName))
                {
                    registeredUpdateMods.Remove(modName);
                    updateQueue.Remove(modName);
                    unregistered = true;
                }
                if (registeredFixedUpdateMods.ContainsKey(modName))
                {
                    registeredFixedUpdateMods.Remove(modName);
                    fixedUpdateQueue.Remove(modName);
                    unregistered = true;
                }
            }
            return unregistered;
        }

        /// <summary>
        /// Registers a mod handler function that will be called as soon as the message is received.
        /// This is called from the networking thread, so you should avoid interacting with KSP directly here as Unity is not thread safe.
        /// </summary>
        /// <param name="modName">Mod name.</param>
        /// <param name="handlerFunction">Handler function.</param>
        public bool RegisterRawModHandler(string modName, DMPMessageCallback handlerFunction)
        {
            lock (eventLock)
            {
                if (registeredRawMods.ContainsKey(modName))
                {
                    DarkLog.Debug("Failed to register raw mod handler for " + modName + ", mod already registered");
                    return false;
                }
                DarkLog.Debug("Registered raw mod handler for " + modName);
                registeredRawMods.Add(modName, handlerFunction);
            }
            return true;
        }

        /// <summary>
        /// Registers a mod handler function that will be called on every Update.
        /// </summary>
        /// <param name="modName">Mod name.</param>
        /// <param name="handlerFunction">Handler function.</param>
        public bool RegisterUpdateModHandler(string modName, DMPMessageCallback handlerFunction)
        {
            lock (eventLock)
            {
                if (registeredUpdateMods.ContainsKey(modName))
                {
                    DarkLog.Debug("Failed to register Update mod handler for " + modName + ", mod already registered");
                    return false;
                }
                DarkLog.Debug("Registered Update mod handler for " + modName);
                registeredUpdateMods.Add(modName, handlerFunction);
                updateQueue.Add(modName, new Queue<byte[]>());
            }
            return true;
        }

        /// <summary>
        /// Registers a mod handler function that will be called on every FixedUpdate.
        /// </summary>
        /// <param name="modName">Mod name.</param>
        /// <param name="handlerFunction">Handler function.</param>
        public bool RegisterFixedUpdateModHandler(string modName, DMPMessageCallback handlerFunction)
        {
            lock (eventLock)
            {
                if (registeredFixedUpdateMods.ContainsKey(modName))
                {
                    DarkLog.Debug("Failed to register FixedUpdate mod handler for " + modName + ", mod already registered");
                    return false;
                }
                DarkLog.Debug("Registered FixedUpdate mod handler for " + modName);
                registeredFixedUpdateMods.Add(modName, handlerFunction);
                fixedUpdateQueue.Add(modName, new Queue<byte[]>());
            }
            return true;
        }

        /// <summary>
        /// Sends a DMP mod message.
        /// </summary>
        /// <param name="modName">Mod name</param>
        /// <param name="messageData">The message payload (MessageWriter can make this easier)</param>
        /// <param name="relay">If set to <c>true</c>, The server will relay the message to all other authenticated clients</param>
        /// <param name="highPriority">If set to <c>true</c>, DMP will send this in the high priority queue (Which will send before all vessel updates and screenshots)</param>
        public void SendDMPModMessage(string modName, byte[] messageData, bool relay, bool highPriority)
        {
            if (modName == null)
            {
                //Now that's just being silly :)
                return;
            }
            if (messageData == null)
            {
                DarkLog.Debug(modName + " attemped to send a null message");
                return;
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(modName);
                mw.Write<bool>(relay);
                mw.Write<bool>(highPriority);
                mw.Write<byte[]>(messageData);
                networkWorker.SendModMessage(mw.GetMessageBytes(), highPriority);
            }
        }

        /// <summary>
        /// Internal use only - Called when a mod message is received from NetworkWorker.
        /// </summary>
        public void HandleModData(byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                string modName = mr.Read<string>();
                byte[] modData = mr.Read<byte[]>();
                OnModMessageReceived(modName, modData);
            }
        }

        /// <summary>
        /// Internal use only
        /// </summary>
        private void OnModMessageReceived(string modName, byte[] modData)
        {
            lock (eventLock)
            {
                if (updateQueue.ContainsKey(modName))
                {
                    updateQueue[modName].Enqueue(modData);
                }

                if (fixedUpdateQueue.ContainsKey(modName))
                {
                    fixedUpdateQueue[modName].Enqueue(modData);
                }

                if (registeredRawMods.ContainsKey(modName))
                {
                    registeredRawMods[modName](modData);
                }
            }
        }

        /// <summary>
        /// Internal use only
        /// </summary>
        internal void Update()
        {
            lock (eventLock)
            {
                foreach (KeyValuePair<string, Queue<byte[]>> currentModQueue in updateQueue)
                {
                    while (currentModQueue.Value.Count > 0)
                    {
                        registeredUpdateMods[currentModQueue.Key](currentModQueue.Value.Dequeue());
                    }
                }
            }
        }

        /// <summary>
        /// Internal use only
        /// </summary>
        internal void FixedUpdate()
        {
            lock (eventLock)
            {
                foreach (KeyValuePair<string, Queue<byte[]>> currentModQueue in fixedUpdateQueue)
                {
                    while (currentModQueue.Value.Count > 0)
                    {
                        registeredFixedUpdateMods[currentModQueue.Key](currentModQueue.Value.Dequeue());
                    }
                }
            }
        }
    }
}

