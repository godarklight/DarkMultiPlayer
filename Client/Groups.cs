using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayer
{
    public class Groups
    {
        public bool synced;
        //Services
        private DMPGame dmpGame;
        NetworkWorker networkWorker;
        Settings dmpSettings;
        //Backing
        private Queue<ByteArray> messageQueue = new Queue<ByteArray>();
        internal Dictionary<string, List<string>> playerGroups = new Dictionary<string, List<string>>();
        internal Dictionary<string, List<string>> groupAdmins = new Dictionary<string, List<string>>();
        private NamedAction updateAction;

        public Groups(DMPGame dmpGame, NetworkWorker networkWorker, Settings dmpSettings)
        {
            this.dmpGame = dmpGame;
            this.networkWorker = networkWorker;
            this.dmpSettings = dmpSettings;
            updateAction = new NamedAction(ProcessMessages);
            dmpGame.updateEvent.Add(updateAction);
        }

        private void ProcessMessages()
        {
            lock (messageQueue)
            {
                while (messageQueue.Count > 0)
                {
                    ByteArray queueByteArray = messageQueue.Dequeue();
                    HandleMessage(queueByteArray.data);
                    ByteRecycler.ReleaseObject(queueByteArray);
                }
            }
        }

        public void Stop()
        {
            dmpGame.updateEvent.Remove(updateAction);
        }

        public void QueueMessage(byte[] data)
        {
            lock (messageQueue)
            {
                ByteArray queueByteArray = ByteRecycler.GetObject(data.Length);
                Array.Copy(data, 0, queueByteArray.data, 0, data.Length);
                messageQueue.Enqueue(queueByteArray);
            }
        }

        private void HandleMessage(byte[] data)
        {
            using (MessageReader mr = new MessageReader(data))
            {
                GroupMessageType type = (GroupMessageType)mr.Read<int>();
                lock (playerGroups)
                {
                    switch (type)
                    {
                        case GroupMessageType.GROUP_RESET:
                            playerGroups.Clear();
                            groupAdmins.Clear();
                            break;
                        case GroupMessageType.GROUP_INFO:
                            {
                                string playerName = mr.Read<string>();
                                string[] groups = mr.Read<string[]>();
                                playerGroups[playerName] = new List<string>(groups);
                            }
                            break;
                        case GroupMessageType.ADMIN_INFO:
                            {
                                string groupName = mr.Read<string>();
                                string[] players = mr.Read<string[]>();
                                groupAdmins[groupName] = new List<string>(players);
                            }
                            break;
                        case GroupMessageType.GROUPS_SYNCED:
                            synced = true;
                            break;
                    }
                }
            }
        }

        //First player added becomes admin
        public void AddPlayerToGroup(string playerName, string groupName)
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)GroupMessageType.ADD_PLAYER);
                mw.Write<string>(playerName);
                mw.Write<string>(groupName);
                networkWorker.SendGroupMessage(mw.GetMessageBytes());
            }
        }

        public void RemovePlayerFromGroup(string playerName, string groupName)
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)GroupMessageType.REMOVE_PLAYER);
                mw.Write<string>(playerName);
                mw.Write<string>(groupName);
                networkWorker.SendGroupMessage(mw.GetMessageBytes());
            }
        }

        public void AddPlayerAdmin(string playerName, string groupName)
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)GroupMessageType.ADD_ADMIN);
                mw.Write<string>(playerName);
                mw.Write<string>(groupName);
                networkWorker.SendGroupMessage(mw.GetMessageBytes());
            }
        }

        public void RemovePlayerAdmin(string playerName, string groupName)
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)GroupMessageType.REMOVE_ADMIN);
                mw.Write<string>(playerName);
                mw.Write<string>(groupName);
                networkWorker.SendGroupMessage(mw.GetMessageBytes());
            }
        }

        public void DeleteGroup(string groupName)
        {
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)GroupMessageType.REMOVE_GROUP);
                mw.Write<string>(groupName);
                networkWorker.SendGroupMessage(mw.GetMessageBytes());
            }
        }

        public bool PlayerInGroup(string playerName, string groupName)
        {
            lock (playerGroups)
            {
                return playerGroups.ContainsKey(playerName) && playerGroups[playerName].Contains(groupName);
            }
        }

        public bool PlayerIsAdmin(string playerName, string groupName)
        {
            lock (playerGroups)
            {
                return PlayerInGroup(playerName, groupName) && groupAdmins.ContainsKey(groupName) && groupAdmins[groupName].Contains(playerName);
            }
        }
    }
}

