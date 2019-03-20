using System;
using System.Collections.Generic;
using System.IO;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    public class Permissions
    {
        private Guid editVessel;
        private static Permissions instance;
        private static string permissionsFolder;
        private static string vesselsFolder;
        private Dictionary<Guid, VesselPermission> vesselPermissions = new Dictionary<Guid, VesselPermission>();

        internal void EditVesselCommand(string commandText)
        {
            editVessel = Guid.Empty;
            if (Guid.TryParse(commandText, out editVessel))
            {
                DarkLog.Normal("Now editing vessel " + editVessel);
            }
            else
            {
                DarkLog.Normal("Type the full ID for the vessel, will match the files in Universe/Vessels/");
            }
        }

        internal void SetVesselOwnerCommand(string commandText)
        {
            if (editVessel == Guid.Empty)
            {
                DarkLog.Normal("Set /editvessel first");
            }
            DarkLog.Normal(editVessel + " now belongs to player: " + commandText);
            SetVesselOwner(editVessel, commandText);
        }

        internal void SetVesselGroupCommand(string commandText)
        {
            if (editVessel == Guid.Empty)
            {
                DarkLog.Normal("Set /editvessel first");
            }
            if (!vesselPermissions.ContainsKey(editVessel))
            {
                DarkLog.Normal("Set /vesselowner first");
                return;
            }
            if (commandText == null || commandText == "")
            {
                DarkLog.Normal(editVessel + " no longer belongs to a group");
            }
            else
            {
                DarkLog.Normal(editVessel + " now belongs to group: " + commandText);
            }
            SetVesselGroup(editVessel, commandText);
        }

        internal void SetVesselProtectionCommand(string commandText)
        {
            if (editVessel == Guid.Empty)
            {
                DarkLog.Normal("Set /editvessel first");
            }
            if (!vesselPermissions.ContainsKey(editVessel))
            {
                DarkLog.Normal("Set /vesselowner first");
                return;
            }
            if (commandText == "public")
            {
                DarkLog.Normal("Set " + editVessel + " to public");
                SetVesselProtection(editVessel, VesselProtectionType.PUBLIC);
            }
            if (commandText == "private")
            {
                DarkLog.Normal("Set " + editVessel + " to private");
                SetVesselProtection(editVessel, VesselProtectionType.PRIVATE);
            }
            if (commandText == "group")
            {
                if (vesselPermissions[editVessel].group == null || vesselPermissions[editVessel].group == "")
                {
                    DarkLog.Normal("Set /vesselgroup first");
                    return;
                }
                SetVesselProtection(editVessel, VesselProtectionType.GROUP);
                DarkLog.Normal("Set " + editVessel + " to group");
            }
        }

        internal void ShowVesselsCommand(string commandText)
        {
            lock (vesselPermissions)
            {
                foreach (KeyValuePair<Guid, VesselPermission> kvp in vesselPermissions)
                {
                    if (kvp.Value.group == null || kvp.Value.group == "")
                    {
                        DarkLog.Normal("Guid: " + kvp.Key + ", Protection mode: " + kvp.Value.protection + ", (no group)");
                    }
                    else
                    {
                        DarkLog.Normal("Guid: " + kvp.Key + ", Protection mode: " + kvp.Value.protection + ", " + kvp.Value.group);
                    }
                }
            }
        }

        public static Permissions fetch
        {
            get
            {
                //Lazy loading
                if (instance == null)
                {
                    permissionsFolder = Path.Combine(Server.universeDirectory, "Permissions");
                    vesselsFolder = Path.Combine(permissionsFolder, "Vessels");
                    Directory.CreateDirectory(permissionsFolder);
                    Directory.CreateDirectory(vesselsFolder);
                    instance = new Permissions();
                    instance.Clean();
                    instance.Load();
                }
                return instance;
            }
        }

        private void Clean()
        {
            string vesselProtoPath = Path.Combine(Server.universeDirectory, "Vessels");
            string[] files = Directory.GetFiles(vesselsFolder);
            foreach (string file in files)
            {
                string vesselFileName = Path.GetFileName(file);
                string vesselFileNameWE = Path.GetFileNameWithoutExtension(file);
                if (!File.Exists(Path.Combine(vesselProtoPath, vesselFileName))) 
                {
                    DarkLog.Debug("Deleting permissions for unknown vessel: " + vesselFileNameWE);
                    File.Delete(file);
                }
            }
        }

        private void Load()
        {
            vesselPermissions.Clear();
            lock (vesselPermissions)
            {
                string[] files = Directory.GetFiles(vesselsFolder);
                foreach (string file in files)
                {

                    Guid vesselGuid = Guid.Parse(Path.GetFileNameWithoutExtension(file));
                    try
                    {
                        using (StreamReader sr = new StreamReader(file))
                        {
                            string playerString = sr.ReadLine();
                            string protectionString = sr.ReadLine();
                            string groupName = sr.ReadLine();
                            VesselPermission vp = new VesselPermission(vesselGuid, playerString);
                            vp.protection = (VesselProtectionType)Enum.Parse(typeof(VesselProtectionType), protectionString);
                            vp.group = groupName;
                            vesselPermissions.Add(vesselGuid, vp);
                        }
                    }
                    catch
                    {
                        DarkLog.Normal("Deleting vessel permissions file " + file + " as it is broken.");
                        File.Delete(file);
                    }
                }
            }
        }

        private void SaveVesselPermissions(Guid guid)
        {
            lock (vesselPermissions)
            {
                string filePath = Path.Combine(vesselsFolder, guid + ".txt");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                if (!vesselPermissions.ContainsKey(guid))
                {
                    return;
                }
                VesselPermission vesselPermission = vesselPermissions[guid];
                using (StreamWriter sw = new StreamWriter(filePath))
                {
                    sw.WriteLine(vesselPermission.owner);
                    sw.WriteLine(vesselPermission.protection);
                    if (!string.IsNullOrEmpty(vesselPermission.group))
                    {
                        sw.WriteLine(vesselPermission.group);
                    }
                }
                Messages.PermissionMessage.SendVesselPermissionToAll(guid);
            }
        }

        public Dictionary<Guid, VesselPermission> GetPermissionsCopy()
        {
            Dictionary<Guid, VesselPermission> retVal = new Dictionary<Guid, VesselPermission>();
            lock (vesselPermissions)
            {
                foreach (KeyValuePair<Guid, VesselPermission> kvp in vesselPermissions)
                {
                    VesselPermission vpCopy = new VesselPermission(kvp.Key, kvp.Value.owner);
                    vpCopy.protection = kvp.Value.protection;
                    vpCopy.group = kvp.Value.group;
                    retVal.Add(kvp.Key, vpCopy);
                }
            }
            return retVal;
        }

        public void SetVesselOwnerIfUnowned(Guid guid, string owner)
        {
            lock (vesselPermissions)
            {
                if (!vesselPermissions.ContainsKey(guid))
                {
                    SetVesselOwner(guid, owner);
                }
            }
        }

        public void SetVesselOwner(Guid guid, string owner)
        {
            lock (vesselPermissions)
            {
                if (!vesselPermissions.ContainsKey(guid))
                {
                    vesselPermissions.Add(guid, new VesselPermission(guid, owner));
                }
                else
                {
                    vesselPermissions[guid].owner = owner;
                }
                SaveVesselPermissions(guid);
            }
        }

        public void SetVesselGroup(Guid guid, string group)
        {
            lock (vesselPermissions)
            {
                if (!vesselPermissions.ContainsKey(guid))
                {
                    return;
                }
                vesselPermissions[guid].group = group;
                SaveVesselPermissions(guid);
            }
        }

        public void SetVesselProtection(Guid guid, VesselProtectionType protection)
        {
            lock (vesselPermissions)
            {
                if (!vesselPermissions.ContainsKey(guid))
                {
                    return;
                }
                vesselPermissions[guid].protection = protection;
                SaveVesselPermissions(guid);
            }
        }

        public void DeleteVessel(Guid guid)
        {
            lock (vesselPermissions)
            {
                if (vesselPermissions.ContainsKey(guid))
                {
                    vesselPermissions.Remove(guid);
                }
                SaveVesselPermissions(guid);
            }
        }

        public bool PlayerIsVesselOwner(string playerName, Guid vesselID)
        {
            lock (vesselPermissions)
            {
                return vesselPermissions.ContainsKey(vesselID) && vesselPermissions[vesselID].owner == playerName;
            }
        }

            public bool PlayerHasVesselPermission(string playerName, Guid vesselID)
        {
            lock (vesselPermissions)
            {
                if (!vesselPermissions.ContainsKey(vesselID))
                {
                    return true;
                }
                VesselPermission vp = vesselPermissions[vesselID];
                if (vp.owner == playerName)
                {
                    return true;
                }
                if (vp.protection == VesselProtectionType.PUBLIC)
                {
                    return true;
                }
                if (vp.protection == VesselProtectionType.GROUP && vp.group != null && vp.group != "")
                {
                    if (Groups.fetch.PlayerInGroup(playerName, vp.group))
                    {
                        return true;
                    }
                }
            } 
            return false;
        }
    }
}
