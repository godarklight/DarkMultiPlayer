using System;
using DarkMultiPlayerCommon;


namespace DarkMultiPlayerServer
{
    public interface IDMPPlugin
    {
        void OnUpdate();
        void OnServerStart();
        void OnServerStop();
        void OnClientConnect(ClientObject client);
        void OnClientAuthenticated(ClientObject client);
        void OnClientDisconnect(ClientObject client);
        void OnMessageReceived(ClientObject client, ClientMessage messageData);
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
    }
}
