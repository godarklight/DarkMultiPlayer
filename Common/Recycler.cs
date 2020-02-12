using System;
using System.Collections.Generic;
namespace DarkMultiPlayerCommon
{
    public static class Recycler<T> where T : class, new()
    {
        private static HashSet<T> inUseObjects = new HashSet<T>();
        private static Stack<T> freeObjects = new Stack<T>();
        private static object lockObject = new object();

        public static T GetObject()
        {
            T freeObject = default(T);
            lock (lockObject)
            {
                if (freeObjects.Count > 0)
                {
                    freeObject = freeObjects.Pop();
                }
                if (freeObject == null)
                {
                    freeObject = new T();
                }
                inUseObjects.Add(freeObject);
            }
            return freeObject;
        }

        public static void ReleaseObject(T releaseObject)
        {
            lock (lockObject)
            {
                if (inUseObjects.Contains(releaseObject))
                {
                    inUseObjects.Remove(releaseObject);
                    freeObjects.Push(releaseObject);
                }
            }
        }

        public static int GetPoolCount()
        {
            return inUseObjects.Count + freeObjects.Count;
        }

        public static int GetPoolFreeCount()
        {
            return freeObjects.Count;
        }

        public static void GarbageCollect(int freeObjectsToLeave, int freeObjectsToTrigger)
        {
            lock (lockObject)
            {
                if (freeObjects.Count > freeObjectsToTrigger)
                {
                    while (freeObjects.Count > freeObjectsToLeave)
                    {
                        freeObjects.Pop();
                    }
                }
            }
        }
    }
}
