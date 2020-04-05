using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class LockSystem
    {
        public static void SendAllLocks(ClientObject client)
        {
            //Send the dictionary as 2 string[]'s.
            Dictionary<string, string> lockList = DarkMultiPlayerServer.LockSystem.fetch.GetLockList();
            List<string> lockKeys = new List<string>(lockList.Keys);
            List<string> lockValues = new List<string>(lockList.Values);

            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.LOCK_SYSTEM, 512 * 1024, NetworkMessageType.ORDERED_RELIABLE);
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write((int)LockMessageType.LIST);
                mw.Write<string[]>(lockKeys.ToArray());
                mw.Write<string[]>(lockValues.ToArray());
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        public static void HandleLockSystemMessage(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                //Read the lock-system message type
                LockMessageType lockMessageType = (LockMessageType)mr.Read<int>();
                switch (lockMessageType)
                {
                    case LockMessageType.ACQUIRE:
                        {
                            string playerName = mr.Read<string>();
                            string lockName = mr.Read<string>();
                            bool force = mr.Read<bool>();
                            if (playerName != client.playerName)
                            {
                                Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for sending a lock message for another player");
                            }
                            bool lockResult = DarkMultiPlayerServer.LockSystem.fetch.AcquireLock(lockName, playerName, force);
                            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.LOCK_SYSTEM, 2048, NetworkMessageType.ORDERED_RELIABLE);
                            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
                            {
                                mw.Write((int)LockMessageType.ACQUIRE);
                                mw.Write(playerName);
                                mw.Write(lockName);
                                mw.Write(lockResult);
                                newMessage.data.size = (int)mw.GetMessageLength();
                            }
                            //Send to all clients
                            ClientHandler.SendToAll(null, newMessage, true);
                            if (lockResult)
                            {
                                DarkLog.Debug(playerName + " acquired lock " + lockName);
                            }
                            else
                            {
                                DarkLog.Debug(playerName + " failed to acquire lock " + lockName);
                            }
                        }
                        break;
                    case LockMessageType.RELEASE:
                        {
                            string playerName = mr.Read<string>();
                            string lockName = mr.Read<string>();
                            if (playerName != client.playerName)
                            {
                                Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for sending a lock message for another player");
                            }
                            bool lockResult = DarkMultiPlayerServer.LockSystem.fetch.ReleaseLock(lockName, playerName);
                            if (!lockResult)
                            {
                                Messages.ConnectionEnd.SendConnectionEnd(client, "Kicked for releasing a lock you do not own");
                            }
                            else
                            {
                                NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.LOCK_SYSTEM, 2048, NetworkMessageType.ORDERED_RELIABLE);
                                using (MessageWriter mw = new MessageWriter(newMessage.data.data))
                                {
                                    mw.Write((int)LockMessageType.RELEASE);
                                    mw.Write(playerName);
                                    mw.Write(lockName);
                                    mw.Write(lockResult);
                                    newMessage.data.size = (int)mw.GetMessageLength();
                                }
                                //Send to all clients
                                ClientHandler.SendToAll(null, newMessage, true);
                            }
                            if (lockResult)
                            {
                                DarkLog.Debug(playerName + " released lock " + lockName);
                            }
                            else
                            {
                                DarkLog.Debug(playerName + " failed to release lock " + lockName);
                            }
                        }
                        break;
                }
            }
        }
    }
}

