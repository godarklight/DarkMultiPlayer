using System;
using System.Collections.Generic;
using System.IO;

namespace DarkMultiPlayerServer
{
    public class AdminSystem
    {
        private static AdminSystem instance = new AdminSystem();
        private List<string> serverAdmins;
        private string adminListFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "DMPAdmins.txt");

        public AdminSystem()
        {
            LoadAdmins();
        }

        public static AdminSystem fetch
        {
            get
            {
                return instance;
            }
        }

        private void LoadAdmins()
        {
            serverAdmins = new List<string>();

            if (File.Exists(adminListFile))
            {
                serverAdmins.AddRange(File.ReadAllLines(adminListFile));
            }
            else
            {
                SaveAdmins();
            }
        }

        private void SaveAdmins()
        {
            try
            {
                if (File.Exists(adminListFile))
                {
                    File.SetAttributes(adminListFile, FileAttributes.Normal);
                }

                using (StreamWriter sw = new StreamWriter(adminListFile))
                {
                    foreach (string user in serverAdmins)
                    {
                        sw.WriteLine(user);
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Error saving admin list!, Exception: " + e);
            }
        }

        public void AddAdmin(string playerName)
        {
            lock (serverAdmins)
            {
                if (!serverAdmins.Contains(playerName))
                {
                    serverAdmins.Add(playerName);
                }
            }
        }

        public void RemoveAdmin(string playerName)
        {
            lock (serverAdmins)
            {
                if (serverAdmins.Contains(playerName))
                {
                    serverAdmins.Remove(playerName);
                }
            }
        }

        public bool IsAdmin(string playerName)
        {
            lock (serverAdmins)
            {
                return serverAdmins.Contains(playerName);
            }
        }

        public string[] GetAdmins()
        {
            lock (serverAdmins)
            {
                return serverAdmins.ToArray();
            }
        }
    }
}

