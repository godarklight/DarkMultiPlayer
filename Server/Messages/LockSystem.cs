using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class LockSystem
    {
        public static void SendAllLocks(ClientObject client)
        {
            ServerMessage newMessage = new ServerMessage();
            newMessage.type = ServerMessageType.LOCK_SYSTEM;
            //Send the dictionary as 2 string[]'s.
            Dictionary<string,string> lockList = DarkMultiPlayerServer.LockSystem.fetch.GetLockList();
            List<string> lockKeys = new List<string>(lockList.Keys);
            List<string> lockValues = new List<string>(lockList.Values);
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write((int)LockMessageType.LIST);
                mw.Write<string[]>(lockKeys.ToArray());
                mw.Write<string[]>(lockValues.ToArray());
                newMessage.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        public static void HandleLockSystemMessage(ClientObject client, byte[] messageData)
        {
            using (MessageReader mr = new MessageReader(messageData))
            {
                //All of the messages need replies, let's create a message for it.
                ServerMessage newMessage = new ServerMessage();
                newMessage.type = ServerMessageType.LOCK_SYSTEM;
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
                            using (MessageWriter mw = new MessageWriter())
                            {
                                mw.Write((int)LockMessageType.ACQUIRE);
                                mw.Write(playerName);
                                mw.Write(lockName);
                                mw.Write(lockResult);
                                newMessage.data = mw.GetMessageBytes();
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
                                using (MessageWriter mw = new MessageWriter())
                                {
                                    mw.Write((int)LockMessageType.RELEASE);
                                    mw.Write(playerName);
                                    mw.Write(lockName);
                                    mw.Write(lockResult);
                                    newMessage.data = mw.GetMessageBytes();
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

