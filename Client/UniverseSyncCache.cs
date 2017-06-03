using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class UniverseSyncCache
    {
        public string cacheDirectory
        {
            get
            {
                return Path.Combine(Path.Combine(Client.dmpClient.gameDataDir, "DarkMultiPlayer"), "Cache");
            }
        }

        private AutoResetEvent incomingEvent = new AutoResetEvent(false);
        private Queue<byte[]> incomingQueue = new Queue<byte[]>();
        private Dictionary<string, long> fileLengths = new Dictionary<string, long>();
        private Dictionary<string, DateTime> fileCreationTimes = new Dictionary<string, DateTime>();
        //Services
        private Settings dmpSettings;

        public long currentCacheSize
        {
            get;
            private set;
        }

        public UniverseSyncCache(Settings dmpSettings)
        {
            this.dmpSettings = dmpSettings;
            Thread processingThread = new Thread(new ThreadStart(ProcessingThreadMain));
            processingThread.IsBackground = true;
            processingThread.Start();
        }

        private void ProcessingThreadMain()
        {
            while (true)
            {
                if (incomingQueue.Count == 0)
                {
                    incomingEvent.WaitOne(500);
                }
                else
                {
                    byte[] incomingBytes;
                    lock (incomingQueue)
                    {
                        incomingBytes = incomingQueue.Dequeue();
                    }
                    SaveToCache(incomingBytes);
                }
            }
        }

        private string[] GetCachedFiles()
        {
            return Directory.GetFiles(cacheDirectory);
        }

        public string[] GetCachedObjects()
        {
            string[] cacheFiles = GetCachedFiles();
            string[] cacheObjects = new string[cacheFiles.Length];
            for (int i = 0; i < cacheFiles.Length; i++)
            {
                cacheObjects[i] = Path.GetFileNameWithoutExtension(cacheFiles[i]);
            }
            return cacheObjects;
        }

        public void ExpireCache()
        {
            DarkLog.Debug("Expiring cache!");
            //No folder, no delete.
            if (!Directory.Exists(Path.Combine(cacheDirectory, "Incoming")))
            {
                DarkLog.Debug("No sync cache folder, skipping expire.");
                return;
            }
            //Delete partial incoming files
            string[] incomingFiles = Directory.GetFiles(Path.Combine(cacheDirectory, "Incoming"));
            foreach (string incomingFile in incomingFiles)
            {
                DarkLog.Debug("Deleting partially cached object " + incomingFile);
                File.Delete(incomingFile);
            }
            //Delete old files
            string[] cacheObjects = GetCachedObjects();
            currentCacheSize = 0;
            foreach (string cacheObject in cacheObjects)
            {
                if (!string.IsNullOrEmpty(cacheObject))
                {
                    string cacheFile = Path.Combine(cacheDirectory, cacheObject + ".txt");
                    //If the file is older than a week, delete it.
                    if (File.GetCreationTime(cacheFile).AddDays(7d) < DateTime.Now)
                    {
                        DarkLog.Debug("Deleting cached object " + cacheObject + ", reason: Expired!");
                        File.Delete(cacheFile);
                    }
                    else
                    {
                        FileInfo fi = new FileInfo(cacheFile);
                        fileCreationTimes[cacheObject] = fi.CreationTime;
                        fileLengths[cacheObject] = fi.Length;
                        currentCacheSize += fi.Length;
                    }
                }
            }
            //While the directory is over (cacheSize) MB
            while (currentCacheSize > (dmpSettings.cacheSize * 1024 * 1024))
            {
                string deleteObject = null;
                //Find oldest file
                foreach (KeyValuePair<string, DateTime> testFile in fileCreationTimes)
                {
                    if (deleteObject == null)
                    {
                        deleteObject = testFile.Key;
                    }
                    if (testFile.Value < fileCreationTimes[deleteObject])
                    {
                        deleteObject = testFile.Key;
                    }
                }
                DarkLog.Debug("Deleting cached object " + deleteObject + ", reason: Cache full!");
                string deleteFile = Path.Combine(cacheDirectory, deleteObject + ".txt");
                File.Delete(deleteFile);
                currentCacheSize -= fileLengths[deleteObject];
                if (fileCreationTimes.ContainsKey(deleteObject))
                {
                    fileCreationTimes.Remove(deleteObject);
                }
                if (fileLengths.ContainsKey(deleteObject))
                {
                    fileLengths.Remove(deleteObject);
                }
            }
        }

        /// <summary>
        /// Queues to cache. This method is non-blocking, using SaveToCache for a blocking method.
        /// </summary>
        /// <param name="fileData">File data.</param>
        public void QueueToCache(byte[] fileData)
        {
            lock (incomingQueue)
            {
                incomingQueue.Enqueue(fileData);
            }
            incomingEvent.Set();
        }

        /// <summary>
        /// Saves to cache. This method is blocking, use QueueToCache for a non-blocking method.
        /// </summary>
        /// <param name="fileData">File data.</param>
        public void SaveToCache(byte[] fileData)
        {
            if (fileData == null || fileData.Length == 0)
            {
                //Don't save 0 byte data.
                return;
            }
            string objectName = Common.CalculateSHA256Hash(fileData);
            string objectFile = Path.Combine(cacheDirectory, objectName + ".txt");
            string incomingFile = Path.Combine(Path.Combine(cacheDirectory, "Incoming"), objectName + ".txt");
            if (!File.Exists(objectFile))
            {
                File.WriteAllBytes(incomingFile, fileData);
                File.Move(incomingFile, objectFile);
                currentCacheSize += fileData.Length;
                fileLengths[objectName] = fileData.Length;
                fileCreationTimes[objectName] = new FileInfo(objectFile).CreationTime;
            }
            else
            {
                File.SetCreationTime(objectFile, DateTime.Now);
                fileCreationTimes[objectName] = new FileInfo(objectFile).CreationTime;
            }
        }

        public byte[] GetFromCache(string objectName)
        {
            string objectFile = Path.Combine(cacheDirectory, objectName + ".txt");
            if (File.Exists(objectFile))
            {
                return File.ReadAllBytes(objectFile);
            }
            else
            {
                throw new IOException("Cached object " + objectName + " does not exist");
            }
        }

        public void DeleteCache()
        {
            DarkLog.Debug("Deleting cache!");
            foreach (string cacheFile in GetCachedFiles())
            {
                File.Delete(cacheFile);
            }
            fileLengths = new Dictionary<string, long>();
            fileCreationTimes = new Dictionary<string, DateTime>();
            currentCacheSize = 0;
        }
    }
}

