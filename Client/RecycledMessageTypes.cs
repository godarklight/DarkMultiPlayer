using System;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class ClientMessage
    {
        public bool handled;
        public ClientMessageType type;
        public ByteArray data;
    }

    public class ServerMessage
    {
        public ServerMessageType type;
        public ByteArray data;
    }
}
