using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DarkMultiPlayerServer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Server.Test
{
    [TestClass]
    public class LockSystemTest
    {
        private readonly LockSystem _lockSystem = new LockSystem();

        [TestMethod]
        public void AcquiringFreeLockAcquiresLock()
        {
            //Acquire a lock
            Assert.IsTrue(_lockSystem.AcquireLock("lock", "player", false));
        }

        [TestMethod]
        public void AcquiringLockTwiceAcquiresLock()
        {
            //Acquire a lock
            Assert.IsTrue(_lockSystem.AcquireLock("lock", "player", false));

            //Acquire same lock again
            Assert.IsTrue(_lockSystem.AcquireLock("lock", "player", false));
        }

        [TestMethod]
        public void AcquiringNonFreeLockDoesNotAcquireLock()
        {
            //Acquire a lock
            Assert.IsTrue(_lockSystem.AcquireLock("lock", "player", false));

            //Lock cannot be acquired by someone else
            Assert.IsFalse(_lockSystem.AcquireLock("lock", "player 2", false));
        }

        [TestMethod]
        public void AcquiringNonFreeLockWithForceAcquiresLock()
        {
            //Acquire a lock
            Assert.IsTrue(_lockSystem.AcquireLock("lock", "player", false));

            //Forcefully take the lock
            Assert.IsTrue(_lockSystem.AcquireLock("lock", "player 2", true));
        }

        [TestMethod]
        public void ReleasingLockMakesLockFreeToBeAcquired()
        {
            //Acquire a lock
            Assert.IsTrue(_lockSystem.AcquireLock("lock", "player", false));

            //Lock cannot be acquired by someone else
            Assert.IsFalse(_lockSystem.AcquireLock("lock", "player 2", false));

            //Release the lock
            Assert.IsTrue(_lockSystem.ReleaseLock("lock", "player"));

            //Now the lock can be acquired by someone else
            Assert.IsTrue(_lockSystem.AcquireLock("lock", "player 2", false));
        }

        [TestMethod]
        public void ReleasingLockWithWrongPlayerNameFails()
        {
            //Acquire lock
            Assert.IsTrue(_lockSystem.AcquireLock("lock", "player", false));

            //Release lock using a different name
            Assert.IsFalse(_lockSystem.ReleaseLock("lock", "player 2"));
        }

        [TestMethod]
        public void ReleasingPlayerLocksReleasesAllPlayerLocksAndNotOtherPlayerLocks()
        {
            Assert.IsTrue(_lockSystem.AcquireLock("0", "player", false));
            Assert.IsTrue(_lockSystem.AcquireLock("1", "player", false));
            Assert.IsTrue(_lockSystem.AcquireLock("2", "player", false));
            Assert.IsTrue(_lockSystem.AcquireLock("3", "player", false));

            Assert.IsTrue(_lockSystem.AcquireLock("4", "player 2", false));
            Assert.IsTrue(_lockSystem.AcquireLock("5", "player 2", false));
            Assert.IsTrue(_lockSystem.AcquireLock("6", "player 2", false));
            Assert.IsTrue(_lockSystem.AcquireLock("7", "player 2", false));

            //Release all locks of player 2s
            _lockSystem.ReleasePlayerLocks("player 2");

            //Make sure that player still holds all his locks
            for (int i = 0; i < 4; i++)
                Assert.IsTrue(_lockSystem.Locks.Contains(new KeyValuePair<string, string>(i.ToString(CultureInfo.InvariantCulture), "player")));
        }

        [TestMethod]
        public void ReleasingPlayerLocksReleasesAllNamedLocksAndNotOtherLocks()
        {
            Assert.IsTrue(_lockSystem.AcquireLock("0", "player", false));
            Assert.IsTrue(_lockSystem.AcquireLock("1", "player", false));
            Assert.IsTrue(_lockSystem.AcquireLock("2", "player", false));
            Assert.IsTrue(_lockSystem.AcquireLock("3", "player", false));

            Assert.IsTrue(_lockSystem.AcquireLock("4", "player 2", false));
            Assert.IsTrue(_lockSystem.AcquireLock("5", "player 2", false));
            Assert.IsTrue(_lockSystem.AcquireLock("6", "player 2", false));
            Assert.IsTrue(_lockSystem.AcquireLock("7", "player 2", false));

            //Release all locks of player 2s
            _lockSystem.ReleasePlayerLocks(new[]
            {
                "0",    //Release lock 0
                "1",    //Release lock 1
                "4"     //Oops! "player" does not own lock "4", nothing should happen
            }, "player");

            //Make sure that player 2 still holds all his locks
            for (int i = 4; i < 8; i++)
                Assert.IsTrue(_lockSystem.Locks.Contains(new KeyValuePair<string, string>(i.ToString(CultureInfo.InvariantCulture), "player 2")));

            //Make sure that player 1 still holds the locks we didn't release
            for (int i = 2; i < 4; i++)
                Assert.IsTrue(_lockSystem.Locks.Contains(new KeyValuePair<string, string>(i.ToString(CultureInfo.InvariantCulture), "player")));
        }
    }
}
