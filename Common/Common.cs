using System;

namespace DarkMultiPlayerCommon
{
    public class Common
    {
        public const long HEART_BEAT_INTERVAL = 5000;
        public const long CONNECTION_TIMEOUT = 30000;
        public const int PROTOCOL_VERSION = 1;
    }

    public enum ClientMessageType
    {
        HEARTBEAT,
        HANDSHAKE_REQUEST,
        SYNC_TIME_REQUEST,
        CHAT_MESSAGE,
        KERBALS_REQUEST,
        SEND_KERBAL_PROTO,
        VESSELS_REQUEST,
        SEND_VESSEL_PROTO,
        SEND_VESSEL_UPDATE,
        SEND_ACTIVE_VESSEL,
        TIME_LOCK_REQUEST,
        PING_REQUEST,
        SPLIT_MESSAGE,
        CONNECTION_END
    }

    public enum ServerMessageType
    {
        HEARTBEAT,
        HANDSHAKE_REPLY,
        CHAT_MESSAGE,
        KERBAL_REPLY,
        KERBAL_COMPLETE,
        VESSEL_REPLY,
        VESSEL_COMPLETE,
        TIME_LOCK_REPLY,
        PLAYER_STATUS,
        SYNC_TIME_REPLY,
        PING_REPLY,
        SPLIT_MESSAGE,
        CONNECTION_END
    }

    public enum ConnectionStatus
    {
        DISCONNECTED,
        CONNECTING,
        CONNECTED
    }

    public enum ClientState
    {
        DISCONNECTED,
        CONNECTING,
        CONNECTED,
        HANDSHAKING,
        AUTHENTICATED,
        TIME_SYNCING,
        TIME_SYNCED,
        SYNCING_KERBALS,
        KERBALS_SYNCED,
        SYNCING_VESSELS,
        VESSELS_SYNCED,
        TIME_LOCKING,
        TIME_LOCKED,
        STARTING,
        RUNNING,
        DISCONNECTING
    }

    public class ClientMessage
    {
        public ClientMessageType type;
        public byte[] data;
    }

    public class ServerMessage
    {
        public ServerMessageType type;
        public byte[] data;
    }
}