using System;
using System.Collections.Generic;
namespace DarkMultiPlayerCommon
{
    public static class ByteRecycler
    {
        private static Dictionary<int, List<InUseContainer<ByteArray>>> currentObjects = new Dictionary<int, List<InUseContainer<ByteArray>>>();
        private static Dictionary<object, InUseContainer<ByteArray>> reverseMapping = new Dictionary<object, InUseContainer<ByteArray>>();
        private static object lockObject = new object();

        public static ByteArray GetObject(int pool_size)
        {
            InUseContainer<ByteArray> freeObject = null;
            lock (lockObject)
            {
                if (!currentObjects.ContainsKey(pool_size))
                {
                    currentObjects.Add(pool_size, new List<InUseContainer<ByteArray>>());
                }
                List<InUseContainer<ByteArray>> currentObjectsOfSize = currentObjects[pool_size];
                foreach (InUseContainer<ByteArray> currentObject in currentObjectsOfSize)
                {
                    if (currentObject.free)
                    {
                        freeObject = currentObject;
                        break;
                    }
                }
                if (freeObject == null)
                {
                    freeObject = new InUseContainer<ByteArray>();
                    freeObject.obj = new ByteArray(pool_size);
                    currentObjectsOfSize.Add(freeObject);
                    reverseMapping.Add(freeObject.obj, freeObject);
                }
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
                    InUseContainer<ByteArray> container = reverseMapping[releaseObject];
                    container.free = true;
                }
            }
        }

        public class ByteArray
        {
            public int size;
            public byte[] data;

            public ByteArray(int size)
            {
                data = new byte[size];
                size = 0;
            }
        }

        private class InUseContainer<T>
        {
            public T obj;
            public bool free;
        }
    }
}
