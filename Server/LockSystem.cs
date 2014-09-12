using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DarkMultiPlayerServer
{
    public class LockSystem
    {
        //Lock types
        //control-vessel-(vesselid) - Replaces the old "inUse" messages, the active pilot will have the control-vessel lock.
        //update-vessel-(vesselid) - Replaces the "only the closest player can update a vessel" code, Now you acquire locks to update crafts around you.
        //asteroid-spawn - Held by the player that can spawn asteroids into the game.
        private readonly ConcurrentDictionary<string, string> _playerLocks = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Create a function that will handle lock conflicts
        /// </summary>
        /// <param name="playerName">The name of the player who wants the lock</param>
        /// <param name="force">Whether or not to force the issue</param>
        /// <returns>A function which takes (lockName, CurrentLockHolderName) and returns NewLockHolderName</returns>
        private static Func<string, string, string> Exchange(string playerName, bool force)
        {
            return (lockName, currentLockHolder) =>
            {
                if (!force)
                {
                    //Force is false, so we return the name of whoever currently controls the lock
                    return currentLockHolder;
                }
                else
                {
                    //Force is true, so we return the name of the new lock holder
                    return playerName;
                }
            };
        }

        /// <summary>
        /// Attempt to acquire a lock
        /// </summary>
        /// <param name="lockName">The name of the lock</param>
        /// <param name="playerName">The name of the player acquiring the lock</param>
        /// <param name="force">If another player holds the lock and force is true, the lock will be forcefully taken from them</param>
        /// <returns>true, if the lock was acquired, otherwise false</returns>
        public bool AcquireLock(string lockName, string playerName, bool force)
        {
            return _playerLocks.AddOrUpdate(lockName, playerName, Exchange(playerName, force)) == playerName;
        }

        /// <summary>
        /// Attempt to release a lock
        /// </summary>
        /// <param name="lockName">The name of the lock</param>
        /// <param name="playerName">The name of the player you expect to be holding the lock</param>
        /// <returns>true if the given player held the lock, false if some other player held the lock</returns>
        public bool ReleaseLock(string lockName, string playerName)
        {
            return ((ICollection<KeyValuePair<string, string>>)_playerLocks).Remove(new KeyValuePair<string, string>(lockName, playerName));
        }

        /// <summary>
        /// Release all locks held by this player
        /// </summary>
        /// <param name="playerName"></param>
        public void ReleasePlayerLocks(string playerName)
        {
            foreach (var lck in _playerLocks.Where(l => l.Value == playerName))
                ReleaseLock(lck.Key, lck.Value);
        }

        /// <summary>
        /// Release a set of locks owned by the given player
        /// </summary>
        /// <param name="locks">The names of the locks to release if they are held by the named player</param>
        /// <param name="player">The name of the player who should hold these locks</param>
        public void ReleasePlayerLocks(IEnumerable<string> locks, string player)
        {
            foreach (var lck in locks)
                ReleaseLock(lck, player);
        }

        /// <summary>
        /// Get all current locks
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> Locks
        {
            get { return _playerLocks.ToArray(); }
        }
    }
}

