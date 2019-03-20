using System;
using System.Collections.Generic;
using System.IO;
namespace DarkMultiPlayerServer
{
    public class Groups
    {
        private string editGroup = null;
        private Dictionary<string, List<string>> playerGroups = new Dictionary<string, List<string>>();
        private Dictionary<string, List<string>> groupAdmins = new Dictionary<string, List<string>>();
        private static Groups instance;
        private static string groupFolder;


        public static Groups fetch
        {
            get
            {
                //Lazy loading
                if (instance == null)
                {
                    groupFolder = Path.Combine(Server.universeDirectory, "Groups");
                    Directory.CreateDirectory(groupFolder);
                    instance = new Groups();
                    instance.Load();
                }
                return instance;
            }
        }

        public Dictionary<string, List<string>> GetGroupsCopy()
        {
            Dictionary<string, List<string>> retVal = new Dictionary<string, List<string>>();
            lock (playerGroups)
            {
                foreach (KeyValuePair<string, List<string>> kvp in playerGroups)
                {
                    List<string> newList = new List<string>(kvp.Value);
                    retVal.Add(kvp.Key, newList);
                }
            }
            return retVal;
        }

        public Dictionary<string, List<string>> GetAdminsCopy()
        {
            Dictionary<string, List<string>> retVal = new Dictionary<string, List<string>>();
            lock (playerGroups)
            {
                foreach (KeyValuePair<string, List<string>> kvp in groupAdmins)
                {
                    List<string> newList = new List<string>(kvp.Value);
                    retVal.Add(kvp.Key, newList);
                }
            }
            return retVal;
        }

        internal void EditGroupCommand(string commandText)
        {
            DarkLog.Normal("Now editing group: " + commandText);
            editGroup = commandText;
        }

        internal void AddPlayerToGroupCommand(string commandText)
        {
            if (editGroup == null)
            {
                DarkLog.Normal("Set /editgroup first");
                return;
            }
            AddPlayerToGroup(commandText, editGroup);
            DarkLog.Normal("Added player " + commandText + " to " + editGroup);
        }

        internal void RemovePlayerFromGroupCommand(string commandText)
        {
            if (editGroup == null)
            {
                DarkLog.Normal("Set /editgroup first");
                return;
            }
            RemovePlayerFromGroup(commandText, editGroup);
            DarkLog.Normal("Removed player " + commandText + " from " + editGroup);
        }

        internal void AddPlayerAdminCommand(string commandText)
        {
            if (editGroup == null)
            {
                DarkLog.Normal("Set /editgroup first");
                return;
            }
            AddPlayerAdmin(commandText, editGroup);
            DarkLog.Normal("Added admin " + commandText + " to " + editGroup);
        }

        internal void RemovePlayerAdminCommand(string commandText)
        {
            if (editGroup == null)
            {
                DarkLog.Normal("Set /editgroup first");
                return;
            }
            RemovePlayerAdmin(commandText, editGroup);
            DarkLog.Normal("Removed admin " + commandText + " from " + editGroup);
        }

        internal void ShowGroupsCommand(string commandText)
        {
            lock (playerGroups)
            {
                foreach (KeyValuePair<string, List<string>> kvp in playerGroups)
                {
                    DarkLog.Normal(kvp.Key + ":");
                    foreach (string group in kvp.Value)
                    {
                        if (PlayerIsAdmin(kvp.Key, group))
                        {
                            DarkLog.Normal(group + " (Admin)");
                        }
                        else
                        {
                            DarkLog.Normal(group);
                        }
                    }
                }
            }
        }

        private void Load()
        {
            playerGroups.Clear();
            groupAdmins.Clear();
            lock (playerGroups)
            {
                string[] files = Directory.GetFiles(groupFolder);
                foreach (string file in files)
                {
                    string playerName = Path.GetFileNameWithoutExtension(file);
                    List<string> groups = new List<string>();
                    using (StreamReader sr = new StreamReader(file))
                    {
                        string addGroup = null;
                        while ((addGroup = sr.ReadLine()) != null)
                        {
                            if (addGroup.StartsWith(".", StringComparison.Ordinal))
                            {
                                addGroup = addGroup.Substring(1);
                                if (!groupAdmins.ContainsKey(addGroup))
                                {
                                    groupAdmins.Add(addGroup, new List<string>());
                                }
                                groupAdmins[addGroup].Add(playerName);
                            }
                            groups.Add(addGroup);
                        }
                    }
                    playerGroups.Add(playerName, groups);
                }
            }
        }

        private void SavePlayer(string playerName)
        {
            lock (playerGroups)
            {
                string playerPath = Path.Combine(groupFolder, playerName + ".txt");
                if (File.Exists(playerPath))
                {
                    File.Delete(playerPath);
                }
                if (playerGroups.ContainsKey(playerName))
                {               
                    List<string> groups = playerGroups[playerName];
                    using (StreamWriter sw = new StreamWriter(playerPath))
                    {
                        foreach (string group in groups)
                        {
                            if (PlayerIsAdmin(playerName, group))
                            {
                                sw.WriteLine("." + group);
                            }
                            else
                            {
                                sw.WriteLine(group);
                            }
                        }
                    }
                }
                Messages.GroupMessage.SendGroupsToAll();
            }
        }

        //First player added becomes admin
        public void AddPlayerToGroup(string playerName, string groupName)
        {
            if (groupName == null || groupName == "")
            {
                DarkLog.Normal("Cannot create groups with empty name");
                return;
            }
            if (groupName.StartsWith(".", StringComparison.Ordinal))
            {
                DarkLog.Normal("Cannot create groups with a leading dot.");
                return;
            }
            lock (playerGroups)
            {
                if (playerGroups.ContainsKey(playerName) && playerGroups[playerName].Contains(groupName))
                {
                    return;
                }
                if (!playerGroups.ContainsKey(playerName))
                {
                    playerGroups.Add(playerName, new List<string>());
                }
                playerGroups[playerName].Add(groupName);
                if (!groupAdmins.ContainsKey(groupName))
                {
                    AddPlayerAdmin(playerName, groupName);
                }
            }
            SavePlayer(playerName);
        }

        public void RemovePlayerFromGroup(string playerName, string groupName)
        {
            RemovePlayerAdmin(playerName, groupName);
            lock (playerGroups)
            {
                if (playerGroups.ContainsKey(playerName))
                {
                    List<string> groups = playerGroups[playerName];
                    if (groups.Contains(groupName))
                    {
                        groups.Remove(groupName);
                    }
                    if (groups.Count == 0)
                    {
                        playerGroups.Remove(playerName);
                    }
                }
            }
            SavePlayer(playerName);
        }

        public void AddPlayerAdmin(string playerName, string groupName)
        {
            if (groupName == null || groupName == "")
            {
                DarkLog.Normal("Cannot create groups with empty name");
                return;
            }
            if (groupName.StartsWith(".", StringComparison.Ordinal))
            {
                DarkLog.Normal("Cannot create groups with a leading dot.");
                return;
            }
            AddPlayerToGroup(playerName, groupName);
            lock (playerGroups)
            {
                if (!groupAdmins.ContainsKey(groupName))
                {
                    groupAdmins.Add(groupName, new List<string>());
                }
                if (!groupAdmins[groupName].Contains(playerName))
                {
                    groupAdmins[groupName].Add(playerName);
                }
            }
            SavePlayer(playerName);
        }

        public void RemovePlayerAdmin(string playerName, string groupName)
        {
            lock (playerGroups)
            {
                if (groupAdmins.ContainsKey(groupName))
                {
                    if (groupAdmins[groupName].Contains(playerName))
                    {
                        groupAdmins[groupName].Remove(playerName);
                    }
                    if (groupAdmins[groupName].Count == 0)
                    {
                        groupAdmins.Remove(groupName);
                        RemoveGroup(groupName);
                    }
                }
            }
            SavePlayer(playerName);
        }

        public void RemoveGroup(string groupName)
        {
            lock (playerGroups)
            {
                if (groupAdmins.ContainsKey(groupName))
                {
                    groupAdmins.Remove(groupName);
                }
                List<string> playerNames = new List<string>(playerGroups.Keys);
                foreach (string playerName in playerNames)
                {
                    RemovePlayerFromGroup(playerName, groupName);
                }
            }
        }

        /// <summary>
        /// Gets a read only copy of groups a player is in
        /// </summary>
        /// <returns>An array of strings of the groups. Returns zero length array if player is in no groups</returns>
        /// <param name="playerName">Player name.</param>
        public string[] GetGroups(string playerName)
        {
            lock (playerGroups)
            {
                if (playerGroups.ContainsKey(playerName))
                {
                    return playerGroups[playerName].ToArray();
                }
            }
            return new string[0];
        }

        public bool PlayerInGroup(string playerName, string groupName)
        {
            lock (playerGroups)
            {
                if (playerGroups.ContainsKey(playerName) && playerGroups[playerName].Contains(groupName))
                {
                    return true;
                }
            }
            return false;
        }

        public bool PlayerIsAdmin(string playerName, string groupName)
        {
            lock (playerGroups)
            {
                if (groupAdmins.ContainsKey(groupName) && groupAdmins[groupName].Contains(playerName))
                {
                    return true;
                }
            }
            return false;
        }

        public bool GroupExists(string groupName)
        {
            lock (playerGroups)
            {
                return groupAdmins.ContainsKey(groupName);
            }
        }
    }
}