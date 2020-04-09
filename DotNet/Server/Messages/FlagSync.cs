using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class FlagSync
    {
        public static void HandleFlagSync(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            string flagPath = Path.Combine(Server.universeDirectory, "Flags");
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                FlagMessageType messageType = (FlagMessageType)mr.Read<int>();
                string playerName = mr.Read<string>();
                if (playerName != client.playerName)
                {
                    Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for sending a flag for another player");
                    return;
                }
                switch (messageType)
                {
                    case FlagMessageType.LIST:
                        {
                            //Send the list back
                            List<string> serverFlagFileNames = new List<string>();
                            List<string> serverFlagOwners = new List<string>();
                            List<string> serverFlagShaSums = new List<string>();

                            string[] clientFlags = mr.Read<string[]>();
                            string[] clientFlagShas = mr.Read<string[]>();
                            string[] serverFlags = Directory.GetFiles(flagPath, "*", SearchOption.AllDirectories);
                            foreach (string serverFlag in serverFlags)
                            {
                                string trimmedName = Path.GetFileName(serverFlag);
                                string flagOwnerPath = Path.GetDirectoryName(serverFlag);
                                string flagOwner = flagOwnerPath.Substring(Path.GetDirectoryName(flagOwnerPath).Length + 1);
                                bool isMatched = false;
                                bool shaDifferent = false;
                                for (int i = 0; i < clientFlags.Length; i++)
                                {
                                    if (clientFlags[i].ToLower() == trimmedName.ToLower())
                                    {
                                        isMatched = true;
                                        shaDifferent = (Common.CalculateSHA256Hash(serverFlag) != clientFlagShas[i]);
                                    }
                                }
                                if (!isMatched || shaDifferent)
                                {
                                    if (flagOwner == client.playerName)
                                    {
                                        DarkLog.Debug("Deleting flag " + trimmedName);
                                        File.Delete(serverFlag);
                                        NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.FLAG_SYNC, 2048, NetworkMessageType.ORDERED_RELIABLE);
                                        using (MessageWriter mw = new MessageWriter(newMessage.data.data))
                                        {
                                            mw.Write<int>((int)FlagMessageType.DELETE_FILE);
                                            mw.Write<string>(trimmedName);
                                            newMessage.data.size = (int)mw.GetMessageLength();
                                            ClientHandler.SendToAll(client, newMessage, false);
                                        }
                                        if (Directory.GetFiles(flagOwnerPath).Length == 0)
                                        {
                                            Directory.Delete(flagOwnerPath);
                                        }
                                    }
                                    else
                                    {
                                        DarkLog.Debug("Sending flag " + serverFlag + " from " + flagOwner + " to " + client.playerName);
                                        NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.FLAG_SYNC, 5 * 1024 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                                        using (MessageWriter mw = new MessageWriter(newMessage.data.data))
                                        {
                                            mw.Write<int>((int)FlagMessageType.FLAG_DATA);
                                            mw.Write<string>(flagOwner);
                                            mw.Write<string>(trimmedName);
                                            mw.Write<byte[]>(File.ReadAllBytes(serverFlag));
                                            newMessage.data.size = (int)mw.GetMessageLength();
                                            ClientHandler.SendToClient(client, newMessage, false);
                                        }
                                    }
                                }
                                //Don't tell the client we have a different copy of the flag so it is reuploaded
                                if (File.Exists(serverFlag))
                                {
                                    serverFlagFileNames.Add(trimmedName);
                                    serverFlagOwners.Add(flagOwner);
                                    serverFlagShaSums.Add(Common.CalculateSHA256Hash(serverFlag));
                                }
                            }
                            NetworkMessage listMessage = NetworkMessage.Create((int)ServerMessageType.FLAG_SYNC, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                            using (MessageWriter mw2 = new MessageWriter(listMessage.data.data))
                            {
                                mw2.Write<int>((int)FlagMessageType.LIST);
                                mw2.Write<string[]>(serverFlagFileNames.ToArray());
                                mw2.Write<string[]>(serverFlagOwners.ToArray());
                                mw2.Write<string[]>(serverFlagShaSums.ToArray());
                                listMessage.data.size = (int)mw2.GetMessageLength();
                            }
                            ClientHandler.SendToClient(client, listMessage, false);
                        }
                        break;
                    case FlagMessageType.DELETE_FILE:
                        {
                            string flagName = mr.Read<string>();
                            string playerFlagPath = Path.Combine(flagPath, client.playerName);
                            if (Directory.Exists(playerFlagPath))
                            {
                                string flagFile = Path.Combine(playerFlagPath, flagName);
                                if (File.Exists(flagFile))
                                {
                                    File.Delete(flagFile);
                                }
                                if (Directory.GetFiles(playerFlagPath).Length == 0)
                                {
                                    Directory.Delete(playerFlagPath);
                                }
                            }
                            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.FLAG_SYNC, 2048, NetworkMessageType.ORDERED_RELIABLE);
                            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
                            {
                                mw.Write<int>((int)FlagMessageType.DELETE_FILE);
                                mw.Write<string>(flagName);
                                newMessage.data.size = (int)mw.GetMessageLength();
                            }
                            ClientHandler.SendToAll(client, newMessage, false);
                        }
                        break;
                    case FlagMessageType.UPLOAD_FILE:
                        {
                            string flagName = mr.Read<string>();
                            byte[] flagData = mr.Read<byte[]>();
                            // Do not save null files
                            if (flagData.Length > 0)
                            {
                                // Check if the specified file is a valid PNG file
                                byte[] pngSequence = { 137, 80, 78, 71, 13, 10, 26, 10 };
                                if (pngSequence.SequenceEqual(flagData.Take(pngSequence.Length)))
                                {
                                    string playerFlagPath = Path.Combine(flagPath, client.playerName);

                                    if (!Directory.Exists(playerFlagPath))
                                        Directory.CreateDirectory(playerFlagPath);

                                    DarkLog.Debug("Saving flag " + flagName + " from " + client.playerName);
                                    File.WriteAllBytes(Path.Combine(playerFlagPath, flagName), flagData);

                                    NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.FLAG_SYNC, 5 * 1024 * 1024, NetworkMessageType.ORDERED_RELIABLE);
                                    using (MessageWriter mw = new MessageWriter(newMessage.data.data))
                                    {
                                        mw.Write<int>((int)FlagMessageType.FLAG_DATA);
                                        mw.Write<string>(client.playerName);
                                        mw.Write<string>(flagName);
                                        mw.Write<byte[]>(flagData);
                                        newMessage.data.size = (int)mw.GetMessageLength();
                                    }

                                    ClientHandler.SendToAll(client, newMessage, false);
                                }
                            }
                        }
                        break;
                }
            }
        }
    }
}

