using System;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class ModData
    {
        public static void HandleModDataMessage(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                string modName = mr.Read<string>();
                bool relay = mr.Read<bool>();
                bool highPriority = mr.Read<bool>();
                byte[] modData = mr.Read<byte[]>();
                if (relay)
                {
                    DMPModInterface.SendDMPModMessageToAll(client, modName, modData, highPriority);
                }
                DMPModInterface.OnModMessageReceived(client, modName, modData);
            }
        }
    }
}

