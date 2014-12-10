using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class VesselProto
    {
        public static void HandleVesselProto(ClientObject client, byte[] messageData)
        {
            //TODO: Relay the message as is so we can optimize it
            //Send vessel
            using (MessageReader mr = new MessageReader(messageData))
            {
                //Don't care about planet time
                double planetTime = mr.Read<double>();
                string vesselGuid = mr.Read<string>();
                bool isDockingUpdate = mr.Read<bool>();
                bool isFlyingUpdate = mr.Read<bool>();
                byte[] possibleCompressedBytes = mr.Read<byte[]>();
                byte[] vesselData = Compression.DecompressIfNeeded(possibleCompressedBytes);
                if (isFlyingUpdate)
                {
                    DarkLog.Debug("Relaying FLYING vessel " + vesselGuid + " from " + client.playerName);
                }
                else
                {
                    if (!isDockingUpdate)
                    {
                        DarkLog.Debug("Saving vessel " + vesselGuid + " from " + client.playerName);
                    }
                    else
                    {
                        DarkLog.Debug("Saving DOCKED vessel " + vesselGuid + " from " + client.playerName);
                    }
                    lock (Server.universeSizeLock)
                    {
                        File.WriteAllBytes(Path.Combine(Server.universeDirectory, "Vessels", vesselGuid + ".txt"), vesselData);
                    }
                }

                ServerMessage newCompressedMessage = null;
                ServerMessage newDecompressedMessage = null;
                if (Compression.BytesAreCompressed(possibleCompressedBytes))
                {
                    //Relay compressed message
                    newCompressedMessage = new ServerMessage();
                    newCompressedMessage.type = ServerMessageType.VESSEL_PROTO;
                    newCompressedMessage.data = messageData;
                    //Build decompressed message.
                    newDecompressedMessage = new ServerMessage();
                    newDecompressedMessage.type = ServerMessageType.VESSEL_PROTO;
                    using (MessageWriter mw = new MessageWriter())
                    {
                        mw.Write<double>(planetTime);
                        mw.Write<string>(vesselGuid);
                        mw.Write<bool>(isDockingUpdate);
                        mw.Write<bool>(isFlyingUpdate);
                        mw.Write<byte[]>(Compression.AddCompressionHeader(vesselData, false));
                        newDecompressedMessage.data = mw.GetMessageBytes();
                    }
                }
                else
                {
                    //Relay decompressed message
                    newDecompressedMessage = new ServerMessage();
                    newDecompressedMessage.type = ServerMessageType.VESSEL_PROTO;
                    newDecompressedMessage.data = messageData;
                    //Build compressed message if the message is over the threshold.
                    //This should only happen if the client has disabled compression.
                    if (vesselData.Length > Common.COMPRESSION_THRESHOLD)
                    {
                        newCompressedMessage = new ServerMessage();
                        newCompressedMessage.type = ServerMessageType.VESSEL_PROTO;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            mw.Write<double>(planetTime);
                            mw.Write<string>(vesselGuid);
                            mw.Write<bool>(isDockingUpdate);
                            mw.Write<bool>(isFlyingUpdate);
                            mw.Write<byte[]>(Compression.CompressIfNeeded(vesselData));
                            newCompressedMessage.data = mw.GetMessageBytes();
                        }
                    }
                }
                ClientHandler.SendToAllAutoCompressed(client, newCompressedMessage, newDecompressedMessage, false);
            }
        }

        public static void SendVessel(ClientObject client, string vesselGUID, byte[] vesselData)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.VESSEL_PROTO;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<double>(0);
                mw.Write<string>(vesselGUID);
                mw.Write<bool>(false);
                mw.Write<bool>(false);
                if (client.compressionEnabled)
                {
                    mw.Write<byte[]>(Compression.CompressIfNeeded(vesselData));
                }
                else
                {
                    mw.Write<byte[]>(Compression.AddCompressionHeader(vesselData, false));
                }
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, false);
        }
    }
}

