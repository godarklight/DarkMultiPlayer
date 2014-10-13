using System;
using System.IO;
using System.Collections.Generic;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class UniverseSyncCache
    {
        private static UniverseSyncCache singleton = new UniverseSyncCache();

        public string cacheDirectory
        {
            get
            {
                return Path.Combine(Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "GameData"), "DarkMultiPlayer"), "Cache");
            }
        }
        private Dictionary<string, long> fileLengths = new Dictionary<string, long>();
        private Dictionary<string, DateTime> fileCreationTimes = new Dictionary<string, DateTime>();
        public long currentCacheSize
        {
            get;
            private set;
        }

        public UniverseSyncCache()
        {
            ExpireCache();
        }

        public static UniverseSyncCache fetch
        {
            get
            {
                return singleton;
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
            string[] cacheObjects = GetCachedObjects();
            currentCacheSize = 0;
            foreach (string cacheObject in cacheObjects)
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
					var fi = new FileInfo(cacheFile);
                    fileCreationTimes[cacheObject] = fi.CreationTime;
                    fileLengths[cacheObject] = fi.Length;
                    currentCacheSize += fi.Length;
                }
            }
            //While the directory is over (cacheSize) MB
            while (currentCacheSize > (Settings.fetch.cacheSize * 1024 * 1024))
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

        public void SaveToCache(byte[] fileData)
        {
            string objectName = Common.CalculateSHA256Hash(fileData);
            string objectFile = Path.Combine(cacheDirectory, objectName + ".txt");
            if (!File.Exists(objectFile))
            {
                File.WriteAllBytes(objectFile, fileData);
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

