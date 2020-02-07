using System;
using System.Collections.Generic;
using System.IO;

namespace DarkMultiPlayerCommon
{
    public static class ByteRecycler
    {
        private static Dictionary<int, List<InUseContainer>> currentObjects = new Dictionary<int, List<InUseContainer>>();
        private static Dictionary<object, InUseContainer> reverseMapping = new Dictionary<object, InUseContainer>();
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
                throw new InvalidOperationException("Pool size not big enough for current request");
            }
            InUseContainer freeObject = null;
            lock (lockObject)
            {
                List<InUseContainer> currentObjectsOfSize = currentObjects[pool_size];
                foreach (InUseContainer currentObject in currentObjectsOfSize)
                {
                    if (currentObject.free)
                    {
                        freeObject = currentObject;
                        break;
                    }
                }
                if (freeObject == null)
                {
                    freeObject = new InUseContainer();
                    freeObject.obj = new ByteArray(pool_size);
                    currentObjectsOfSize.Add(freeObject);
                    reverseMapping.Add(freeObject.obj, freeObject);
                }
                freeObject.obj.size = size;
                freeObject.free = false;
            }
            return freeObject.obj;
        }

        public static void ReleaseObject(ByteArray releaseObject)
        {
            lock (lockObject)
            {
                if (reverseMapping.ContainsKey(releaseObject))
                {
                    InUseContainer container = reverseMapping[releaseObject];
                    container.free = true;
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
                currentObjects.Add(pool_size, new List<InUseContainer>());
                poolSizes.Sort();
            }
        }

        public static int GetPoolCount(int size)
        {
            if (!currentObjects.ContainsKey(size))
            {
                return 0;
            }
            return currentObjects[size].Count;
        }

        public static int GetPoolFreeCount(int size)
        {
            if (!currentObjects.ContainsKey(size))
            {
                return 0;
            }
            int free = 0;
            foreach (InUseContainer iuc in currentObjects[size])
            {
                if (iuc.free)
                {
                    free++;
                }
            }
            return free;
        }

        public static void DumpNonFree(string path)
        {
            Directory.CreateDirectory(path);
            foreach (string file in Directory.GetFiles(path))
            {
                File.Delete(file);
            }
            int freeID = 0;
            foreach (KeyValuePair<int, List<InUseContainer>> poolObjects in currentObjects)
            {
                foreach (InUseContainer iuc in poolObjects.Value)
                {
                    if (!iuc.free)
                    {
                        string fileName = Path.Combine(path, freeID + ".txt");
                        using (FileStream fs = new FileStream(fileName, FileMode.OpenOrCreate))
                        {
                            fs.Write(iuc.obj.data, 0, iuc.obj.Length);;
                        }
                        freeID++;
                    }
                }
            }
        }

        private class InUseContainer
        {
            public ByteArray obj;
            public bool free;
        }
    }
}
