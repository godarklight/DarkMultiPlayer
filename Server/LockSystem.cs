using System;
using System.Collections.Generic;

namespace DarkMultiPlayerServer
{
    public class LockSystem
    {
        private static LockSystem instance = new LockSystem();
        private Dictionary<string, string> playerLocks;
        //Lock types
        //control-vessel-(vesselid) - Replaces the old "inUse" messages, the active pilot will have the control-vessel lock.
        //update-vessel-(vesselid) - Replaces the "only the closest player can update a vessel" code, Now you acquire locks to update crafts around you.
        //asteroid-spawn - Held by the player that can spawn asteroids into the game.

        public LockSystem()
        {
            playerLocks = new Dictionary<string, string>();
        }

        public static LockSystem fetch
        {
            get
            {
                return instance;
            }
        }

        public bool AcquireLock(string lockName, string playerName, bool force)
        {
            lock (playerLocks)
            {
                if (force || !playerLocks.ContainsKey(lockName))
                {
                    playerLocks[lockName] = playerName;
                    return true;
                }
                return false;
            }
        }

        public bool ReleaseLock(string lockName, string playerName)
        {
            lock (playerLocks)
            {
                if (playerLocks.ContainsKey(lockName))
                {
                    if (playerLocks[lockName] == playerName)
                    {
                        playerLocks.Remove(lockName);
                        return true;
                    }
                }
                return false;
            }
        }

        public void ReleasePlayerLocks(string playerName)
        {
            lock (playerLocks)
            {
                List<string> removeList = new List<string>();
                foreach (KeyValuePair<string, string> kvp in playerLocks)
                {
                    if (kvp.Value == playerName)
                    {
                        removeList.Add(kvp.Key);
                    }
                }
                foreach (string removeValue in removeList)
                {
                    playerLocks.Remove(removeValue);
                }
            }
        }

        public Dictionary<string, string> GetLockList()
        {
            lock (playerLocks)
            {
                //Return a copy.
                return new Dictionary<string, string>(playerLocks);
            }
        }

        public static void Reset()
        {
            lock (fetch.playerLocks)
            {
                fetch.playerLocks.Clear();
            }
        }
    }
}

