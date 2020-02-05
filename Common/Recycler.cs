using System;
using System.Collections.Generic;
namespace DarkMultiPlayerCommon
{
    public class Recycler<T> where T : new()
    {
        private List<InUseContainer<T>> currentObjects = new List<InUseContainer<T>>();
        private Dictionary<object, InUseContainer<T>> reverseMapping = new Dictionary<object, InUseContainer<T>>();

        public T GetObject()
        {
            InUseContainer<T> freeObject = null;
            lock (this)
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

        public void ReleaseObject(T releaseObject)
        {
            lock (this)
            {
                if (reverseMapping.ContainsKey(releaseObject))
                {
                    InUseContainer<T> container = reverseMapping[releaseObject];
                    container.free = true;
                }
            }
        }

        private class InUseContainer<U>
        {
            public U obj;
            public bool free;
        }
    }
}
