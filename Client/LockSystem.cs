using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayer
{
    public delegate void AcquireEvent(string playerName, string lockName, bool lockResult);
    public delegate void ReleaseEvent(string playerName, string lockName);
    public class LockSystem
    {
        private Dictionary<string, string> serverLocks = new Dictionary<string, string>();
        private List<AcquireEvent> lockAcquireEvents = new List<AcquireEvent>();
        private List<ReleaseEvent> lockReleaseEvents = new List<ReleaseEvent>();
        private Dictionary<string, double> lastAcquireTime = new Dictionary<string, double>();
        private object lockObject = new object();

        //Services
        private Settings dmpSettings;
        private NetworkWorker networkWorker;

        public LockSystem(Settings dmpSettings, NetworkWorker networkWorker)
        {
            this.dmpSettings = dmpSettings;
            this.networkWorker = networkWorker;
        }

        public void ThrottledAcquireLock(string lockname)
        {
            if (lastAcquireTime.ContainsKey(lockname) ? ((Client.realtimeSinceStartup - lastAcquireTime[lockname]) > 5f) : true)
            {
                lastAcquireTime[lockname] = Client.realtimeSinceStartup;
                AcquireLock(lockname, false);
            }
        }

        public void AcquireLock(string lockName, bool force)
        {
            lock (lockObject)
            {
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)LockMessageType.ACQUIRE);
                    mw.Write<string>(dmpSettings.playerName);
                    mw.Write<string>(lockName);
                    mw.Write<bool>(force);
                    networkWorker.SendLockSystemMessage(mw.GetMessageBytes());
                }
            }
        }

        public void ReleaseLock(string lockName)
        {
            lock (lockObject)
            {
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)LockMessageType.RELEASE);
                    mw.Write<string>(dmpSettings.playerName);
                    mw.Write<string>(lockName);
                    networkWorker.SendLockSystemMessage(mw.GetMessageBytes());
                }
                if (LockIsOurs(lockName))
                {
                    serverLocks.Remove(lockName);
                }
            }
        }

        public void ReleasePlayerLocks(string playerName)
        {
            lock (lockObject)
            {
                List<string> removeList = new List<string>();
                foreach (KeyValuePair<string, string> kvp in serverLocks)
                {
                    if (kvp.Value == playerName)
                    {
                        removeList.Add(kvp.Key);
                    }
                }
                foreach (string removeValue in removeList)
                {
                    serverLocks.Remove(removeValue);
                    FireReleaseEvent(playerName, removeValue);
                }
            }
        }

        public void ReleasePlayerLocksWithPrefix(string playerName, string prefix)
        {
            DarkLog.Debug("Releasing lock with prefix " + prefix + " for " + playerName);
            lock (lockObject)
            {
                List<string> removeList = new List<string>();
                foreach (KeyValuePair<string, string> kvp in serverLocks)
                {
                    if (kvp.Key.StartsWith(prefix) && kvp.Value == playerName)
                    {
                        removeList.Add(kvp.Key);
                    }
                }
                foreach (string removeValue in removeList)
                {
                    if (playerName == dmpSettings.playerName)
                    {
                        DarkLog.Debug("Releasing lock " + removeValue);
                        ReleaseLock(removeValue);
                    }
                    else
                    {
                        serverLocks.Remove(removeValue);
                        FireReleaseEvent(playerName, removeValue);
                    }
                }
            }
        }

        public void HandleLockMessage(byte[] messageData)
        {
            lock (lockObject)
            {
                using (MessageReader mr = new MessageReader(messageData))
                {
                    LockMessageType lockMessageType = (LockMessageType)mr.Read<int>();
                    switch (lockMessageType)
                    {
                        case LockMessageType.LIST:
                            {
                                //We shouldn't need to clear this as LIST is only sent once, but better safe than sorry.
                                serverLocks.Clear();
                                string[] lockKeys = mr.Read<string[]>();
                                string[] lockValues = mr.Read<string[]>();
                                for (int i = 0; i < lockKeys.Length; i++)
                                {
                                    serverLocks.Add(lockKeys[i], lockValues[i]);
                                }
                            }
                            break;
                        case LockMessageType.ACQUIRE:
                            {
                                string playerName = mr.Read<string>();
                                string lockName = mr.Read<string>();
                                bool lockResult = mr.Read<bool>();
                                if (lockResult)
                                {
                                    serverLocks[lockName] = playerName;
                                }
                                FireAcquireEvent(playerName, lockName, lockResult);
                            }
                            break;
                        case LockMessageType.RELEASE:
                            {
                                string playerName = mr.Read<string>();
                                string lockName = mr.Read<string>();
                                if (serverLocks.ContainsKey(lockName))
                                {
                                    serverLocks.Remove(lockName);
                                }
                                FireReleaseEvent(playerName, lockName);
                            }
                            break;
                    }
                }
            }
        }

        public void RegisterAcquireHook(AcquireEvent methodObject)
        {
            lockAcquireEvents.Add(methodObject);
        }

        public void UnregisterAcquireHook(AcquireEvent methodObject)
        {
            if (lockAcquireEvents.Contains(methodObject))
            {
                lockAcquireEvents.Remove(methodObject);
            }
        }

        public void RegisterReleaseHook(ReleaseEvent methodObject)
        {
            lockReleaseEvents.Add(methodObject);
        }

        public void UnregisterReleaseHook(ReleaseEvent methodObject)
        {
            if (lockReleaseEvents.Contains(methodObject))
            {
                lockReleaseEvents.Remove(methodObject);
            }
        }

        private void FireAcquireEvent(string playerName, string lockName, bool lockResult)
        {
            foreach (AcquireEvent methodObject in lockAcquireEvents)
            {
                try
                {
                    methodObject(playerName, lockName, lockResult);
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Error thrown in acquire lock event, exception " + e);
                }
            }
        }

        private void FireReleaseEvent(string playerName, string lockName)
        {
            foreach (ReleaseEvent methodObject in lockReleaseEvents)
            {
                try
                {
                    methodObject(playerName, lockName);
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Error thrown in release lock event, exception " + e);
                }
            }
        }

        public bool LockIsOurs(string lockName)
        {
            lock (lockObject)
            {
                if (serverLocks.ContainsKey(lockName))
                {
                    if (serverLocks[lockName] == dmpSettings.playerName)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public bool LockExists(string lockName)
        {
            lock (lockObject)
            {
                return serverLocks.ContainsKey(lockName);
            }
        }

        public string LockOwner(string lockName)
        {
            lock (lockObject)
            {
                if (serverLocks.ContainsKey(lockName))
                {
                    return serverLocks[lockName];
                }
                return "";
            }
        }
    }
}

