using System;
using System.Collections.Generic;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class WhitelistSystem
    {
        private static WhitelistSystem instance;
        private static string whitelistFile;
        private List<string> serverWhitelist;

        public WhitelistSystem()
        {
            LoadWhitelist();
        }

        public static WhitelistSystem fetch
        {
            get
            {
                //Lazy loading
                if (instance == null)
                {
                    whitelistFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DMPWhitelist.txt");
                    instance = new WhitelistSystem();
                }
                return instance;
            }
        }

        public void AddPlayer(string playerName)
        {
            lock (serverWhitelist)
            {
                if (!serverWhitelist.Contains(playerName))
                {
                    serverWhitelist.Add(playerName);
                    SaveWhitelist();
                }
            }
        }

        public void RemovePlayer(string playerName)
        {
            lock (serverWhitelist)
            {
                if (serverWhitelist.Contains(playerName))
                {
                    serverWhitelist.Remove(playerName);
                    SaveWhitelist();
                }
            }
        }

        private void LoadWhitelist()
        {
            DarkLog.Debug("Loading Whitelist");
            serverWhitelist = new List<string>();

            if (File.Exists(whitelistFile))
            {
                serverWhitelist.AddRange(File.ReadAllLines(whitelistFile));
            }
            else
            {
                SaveWhitelist();
            }
        }

        private void SaveWhitelist()
        {
            DarkLog.Debug("Saving Whitelist");
            try
            {
                if (File.Exists(whitelistFile))
                {
                    File.SetAttributes(whitelistFile, FileAttributes.Normal);
                }

                using (StreamWriter sw = new StreamWriter(whitelistFile))
                {
                    foreach (string user in serverWhitelist)
                    {
                        sw.WriteLine(user);
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Error saving whitelist!, Exception: " + e);
            }
        }

        public bool IsWhitelisted(string playerName)
        {
            return serverWhitelist.Contains(playerName);
        }

        public string[] GetWhiteList()
        {
            lock (serverWhitelist)
            {
                return serverWhitelist.ToArray();
            }
        }
    }

}

