using System;
using System.Collections.Generic;

namespace DarkMultiPlayerServer
{
    public class LockSystem
    {
        private object lockObject = new object();
        private Dictionary<string, string> playerLocks = new Dictionary<string, string>();
        //Lock types
        //control-vessel-(vesselid) - Replaces the old "inUse" messages, the active pilot will have the control-vessel lock.
        //update-vessel-(vesselid) - Replaces the "only the closest player can update a vessel" code, Now you acquire locks to update crafts around you.
        //asteroid-spawn - Held by the player that can spawn asteroids into the game.

        public bool AcquireLock(string lockName, string playerName, bool force)
        {
            lock (lockObject)
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
            lock (lockObject)
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
            lock (lockObject)
            {
				var removeList = new List<string>();
                foreach (KeyValuePair<string,string> kvp in playerLocks)
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

        public Dictionary<string,string> GetLockList()
        {
            lock (lockObject)
            {
                //Return a copy.
                return new Dictionary<string, string>(playerLocks);
            }
        }
    }
}

