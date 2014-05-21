using System;
using System.Text;
using System.Security.Cryptography;
using System.IO;

namespace DarkMultiPlayerCommon
{
    public class Common
    {
        public const long HEART_BEAT_INTERVAL = 5000;
        public const long CONNECTION_TIMEOUT = 30000;
        public const int MAX_MESSAGE_SIZE = 5242880;
        //5MB
        public const int SPLIT_MESSAGE_LENGTH = 8096;
        //8kb
        public const int PROTOCOL_VERSION = 8;

        public static string CalculateSHA256Hash(string fileName)
        {
            return CalculateSHA256Hash(File.ReadAllBytes(fileName));
        }

        public static string CalculateSHA256Hash(byte[] fileData)
        {
            StringBuilder sb = new StringBuilder();
            using (SHA256Managed sha = new SHA256Managed())
            {
                byte[] fileHashData = sha.ComputeHash(fileData);
                //Byte[] to string conversion adapted from MSDN...
                for (int i = 0; i < fileHashData.Length; i++)
                {
                    sb.Append(fileHashData[i].ToString("x2"));
                }
            }
            return sb.ToString();
        }
    }

    public enum CraftType
    {
        VAB,
        SPH,
        SUBASSEMBLY
    }

    public enum ClientMessageType
    {
        HEARTBEAT,
        HANDSHAKE_REQUEST,
        CHAT_MESSAGE,
        PLAYER_STATUS,
        SCENARIO_DATA,
        KERBALS_REQUEST,
        KERBAL_PROTO,
        VESSELS_REQUEST,
        VESSEL_PROTO,
        VESSEL_UPDATE,
        VESSEL_REMOVE,
        CRAFT_LIBRARY,
        SCREENSHOT_LIBRARY,
        SEND_ACTIVE_VESSEL,
        SYNC_TIME_REQUEST,
        PING_REQUEST,
        WARP_CONTROL,
        SPLIT_MESSAGE,
        CONNECTION_END
    }

    public enum ServerMessageType
    {
        HEARTBEAT,
        HANDSHAKE_REPLY,
        SERVER_SETTINGS,
        CHAT_MESSAGE,
        PLAYER_STATUS,
        PLAYER_DISCONNECT,
        SCENARIO_DATA,
        KERBAL_REPLY,
        KERBAL_COMPLETE,
        VESSEL_LIST,
        VESSEL_PROTO,
        VESSEL_UPDATE,
        VESSEL_COMPLETE,
        VESSEL_REMOVE,
        CRAFT_LIBRARY,
        SCREENSHOT_LIBRARY,
        SET_ACTIVE_VESSEL,
        SET_SUBSPACE,
        SYNC_TIME_REPLY,
        PING_REPLY,
        WARP_CONTROL,
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

    public enum WarpMode
    {
        MCW_FORCE,
        MCW_VOTE,
        MCW_LOWEST,
        SUBSPACE_SIMPLE,
        SUBSPACE,
        NONE
    }

    public enum GameMode
    {
        SANDBOX,
        CAREER
    }

    public enum WarpMessageType
    {
        REQUEST_VOTE,
        REPLY_VOTE,
        CHANGE_WARP,
        SET_CONTROLLER,
        NEW_SUBSPACE,
        CHANGE_SUBSPACE,
        RELOCK_SUBSPACE,
        REPORT_RATE
    }

    public enum CraftMessageType
    {
        LIST,
        REQUEST_FILE,
        RESPOND_FILE,
        UPLOAD_FILE,
        ADD_FILE,
        DELETE_FILE,
    }

    public enum ScreenshotMessageType
    {
        NOTIFY,
        WATCH,
        SCREENSHOT,
    }

    public enum ChatMessageType
    {
        LIST,
        JOIN,
        LEAVE,
        CHANNEL_MESSAGE,
        PRIVATE_MESSAGE,
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

    public class PlayerStatus
    {
        public string playerName;
        public string vesselText;
        public string statusText;
    }

    public class Subspace
    {
        public long serverClock;
        public double planetTime;
        public float subspaceSpeed;
    }
}