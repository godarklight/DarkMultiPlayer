using System;
using System.Collections.Generic;
namespace DarkMultiPlayerCommon
{
    public static class Recycler<T> where T : new()
    {
        private static List<InUseContainer<T>> currentObjects = new List<InUseContainer<T>>();
        private static Dictionary<object, InUseContainer<T>> reverseMapping = new Dictionary<object, InUseContainer<T>>();
        private static object lockObject = new object();

        public static T GetObject()
        {
            InUseContainer<T> freeObject = null;
            lock (lockObject)
            {
                foreach (InUseContainer<T> currentObject in currentObjects)
                {
                    if (currentObject.free)
                    {
                        freeObject = currentObject;
                        break;
                    }
                }
                if (freeObject == null)
                {
                    freeObject = new InUseContainer<T>();
                    freeObject.obj = new T();
                    currentObjects.Add(freeObject);
                    reverseMapping.Add(freeObject.obj, freeObject);
                }
                freeObject.free = false;
            }
            return freeObject.obj;
        }

        public static void ReleaseObject(T releaseObject)
        {
            lock (lockObject)
            {
                if (reverseMapping.ContainsKey(releaseObject))
                {
                    InUseContainer<T> container = reverseMapping[releaseObject];
                    container.free = true;
                }
            }
        }

        public static int GetPoolCount()
        {
            return currentObjects.Count;
        }

        public static int GetPoolFreeCount()
        {
            int free = 0;
            lock (lockObject)
            {
                foreach (InUseContainer<T> currentObject in currentObjects)
                {
                    if (currentObject.free)
                    {
                        free++;
                    }
                }
            }
            return free;
        }

        private class InUseContainer<U>
        {
            public U obj;
            public bool free;
        }
    }
}
