using System;
using System.Collections.Generic;
using System.IO;

namespace DarkMultiPlayerCommon
{
    public static class ByteRecycler
    {
        private static Dictionary<int, HashSet<ByteArray>> inUseObjects = new Dictionary<int, HashSet<ByteArray>>();
        private static Dictionary<int, Stack<ByteArray>> freeObjects = new Dictionary<int, Stack<ByteArray>>();
        private static List<int> poolSizes = new List<int>();
        private static object lockObject = new object();

        public static ByteArray GetObject(int size)
        {
            if (size == 0)
            {
                throw new Exception("Cannot allocate 0 byte array");
            }
            int pool_size = 0;
            foreach (int poolSize in poolSizes)
            {
                if (poolSize > size)
                {
                    pool_size = poolSize;
                    break;
                }
            }
            if (pool_size == 0)
            {
                ByteArray temporary = new ByteArray(size);
                temporary.temporary = true;
                return temporary;
            }
            ByteArray freeObject = null;
            lock (lockObject)
            {
                Stack<ByteArray> currentObjectsOfSize = freeObjects[pool_size];
                if (currentObjectsOfSize.Count > 0)
                {
                    freeObject = currentObjectsOfSize.Pop();
                }
                if (freeObject == null)
                {
                    freeObject = new ByteArray(pool_size);
                }
                inUseObjects[pool_size].Add(freeObject);
                freeObject.size = size;
            }
            return freeObject;
        }

        public static void ReleaseObject(ByteArray releaseObject)
        {
            if (releaseObject.temporary)
            {
                //Temporary objects were never assigned to a pool and must be thrown away.
                return;
            }
            lock (lockObject)
            {
                if (inUseObjects.ContainsKey(releaseObject.data.Length))
                {
                    HashSet<ByteArray> currentObjectsOfSize = inUseObjects[releaseObject.data.Length];
                    currentObjectsOfSize.Remove(releaseObject);
                    freeObjects[releaseObject.data.Length].Push(releaseObject);
                }
                else
                {
                    throw new InvalidOperationException("Release object is not mapped.");
                }
            }
        }

        public static void AddPoolSize(int pool_size)
        {
            if (!poolSizes.Contains(pool_size))
            {
                poolSizes.Add(pool_size);
                freeObjects.Add(pool_size, new Stack<ByteArray>());
                inUseObjects.Add(pool_size, new HashSet<ByteArray>());
                poolSizes.Sort();
            }
        }

        public static int GetPoolCount(int size)
        {
            if (!poolSizes.Contains(size))
            {
                return 0;
            }
            return inUseObjects[size].Count + freeObjects[size].Count;
        }

        public static int GetPoolFreeCount(int size)
        {
            if (!poolSizes.Contains(size))
            {
                return 0;
            }
            return freeObjects[size].Count;
        }

        public static void GarbageCollect(int freeObjectsToLeave, int freeObjectsToTrigger)
        {
            lock (lockObject)
            {
                foreach (Stack<ByteArray> freeObjectStack in freeObjects.Values)
                {
                    if (freeObjectStack.Count > freeObjectsToTrigger)
                    {
                        while ( freeObjectStack.Count > freeObjectsToLeave)
                        {
                            freeObjectStack.Pop();
                        }
                    }
                }
            }
        }

        public static void DumpNonFree(string path)
        {
            Directory.CreateDirectory(path);
            foreach (string file in Directory.GetFiles(path))
            {
                File.Delete(file);
            }
            int freeID = 0;
            foreach (HashSet<ByteArray> currentObjectsOfSize in inUseObjects.Values)
            {
                foreach (ByteArray byteArray in currentObjectsOfSize)
                {
                    string fileName = Path.Combine(path, freeID + ".txt");
                    using (FileStream fs = new FileStream(fileName, FileMode.OpenOrCreate))
                    {
                        fs.Write(byteArray.data, 0, byteArray.Length); ;
                    }
                    freeID++;
                }
            }
        }
    }
}