using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer
{
    /// <summary>
    /// DMP message callback.
    /// client - The client that has sent the message
    /// modData - The mod byte[] payload
    /// </summary>
    public delegate void DMPMessageCallback(ClientObject client, byte[] modData);
    public class DMPModInterface
    {
        private static Dictionary<string, DMPMessageCallback> registeredMods = new Dictionary<string, DMPMessageCallback>();
        private static object eventLock = new object();

        /// <summary>
        /// Registers a mod handler function that will be called as soon as the message is received.
        /// </summary>
        /// <param name="modName">Mod name.</param>
        /// <param name="handlerFunction">Handler function.</param>
        public static bool RegisterModHandler(string modName, DMPMessageCallback handlerFunction)
        {
            lock (eventLock)
            {
                if (registeredMods.ContainsKey(modName))
                {
                    DarkLog.Debug("Failed to register mod handler for " + modName + ", mod already registered");
                    return false;
                }
                DarkLog.Debug("Registered mod handler for " + modName);
                registeredMods.Add(modName, handlerFunction);
            }
            return true;
        }

        /// <summary>
        /// Unregisters a mod handler.
        /// </summary>
        /// <returns><c>true</c> if a mod handler was unregistered</returns>
        /// <param name="modName">Mod name.</param>
        public static bool UnregisterModHandler(string modName)
        {
            bool unregistered = false;
            lock (eventLock)
            {
                if (registeredMods.ContainsKey(modName))
                {
                    registeredMods.Remove(modName);
                    unregistered = true;
                }
            }
            return unregistered;
        }

        public static void SendDMPModMessageToClient(ClientObject client, string modName, byte[] messageData, bool highPriority)
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
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.MOD_DATA;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(modName);
                mw.Write<byte[]>(messageData);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, highPriority);
        }

        public static void SendDMPModMessageToAll(ClientObject excludeClient, string modName, byte[] messageData, bool highPriority)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.MOD_DATA;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string>(modName);
                mw.Write<byte[]>(messageData);
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToAll(excludeClient, newMessage, highPriority);
        }


        /// <summary>
        /// Internal use only - Called when a mod message is received from ClientHandler.
        /// </summary>
        public static void OnModMessageReceived(ClientObject client, string modName, byte[] modData)
        {
            if (registeredMods.ContainsKey(modName))
            {
                registeredMods[modName](client, modData);
            }
        }
    }
}
