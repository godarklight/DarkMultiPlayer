using System;
using DarkMultiPlayerCommon;


namespace DarkMultiPlayerServer
{
    public interface IDMPPlugin
    {
        /// <summary>
        /// Fires every main thread tick (10ms).
        /// </summary>
        void OnUpdate();
        /// <summary>
        /// Fires just after the server is started or restarted.
        /// </summary>
        void OnServerStart();
        /// <summary>
        /// Fires just before the server stops or restarts.
        /// </summary>
        void OnServerStop();
        /// <summary>
        /// Fires when the client's connection is accepted.
        /// </summary>
        void OnClientConnect(ClientObject client);
        /// <summary>
        /// Fires just after the client has authenticated.
        /// </summary>
        void OnClientAuthenticated(ClientObject client);
        /// <summary>
        /// Fires when a client disconnects.
        /// </summary>
        void OnClientDisconnect(ClientObject client);
        /// <summary>
        /// Fires every time a message is received from a client.
        /// </summary>
        /// <param name="client">The client that has sent the message</param>
        /// <param name="messageData">The message payload (Null for certain types)</param>
        void OnMessageReceived(ClientObject client, ClientMessage messageData);
        /// <summary>
        /// Fires every time a message is sent to a client.
        /// </summary>
        /// <param name="client">The client that will receive the message</param>
        /// <param name="messageData">The message payload (Null for certain types)</param>
        void OnMessageSent(ClientObject client, ServerMessage messageData);
    }

    public abstract class DMPPlugin : IDMPPlugin
    {
        public virtual void OnUpdate() { }
        public virtual void OnServerStart() { }
        public virtual void OnServerStop() { }
        public virtual void OnClientConnect(ClientObject client) { }
        public virtual void OnClientAuthenticated(ClientObject client) { }
        public virtual void OnClientDisconnect(ClientObject client) { }
        public virtual void OnMessageReceived(ClientObject client, ClientMessage messageData) { }
        public virtual void OnMessageSent(ClientObject client, ServerMessage messageData) { }
    }
}
