using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;
using MessageStream2;

namespace DarkMultiPlayer
{
    public class Permissions
    {
        public bool synced;
        //Services
        private DMPGame dmpGame;
        private NetworkWorker networkWorker;
        private Settings dmpSettings;
        private Groups groups;
        //Backing
        private Queue<ByteArray> messageQueue = new Queue<ByteArray>();
        internal Dictionary<Guid, VesselPermission> vesselPermissions = new Dictionary<Guid, VesselPermission>();
        private NamedAction updateAction;

        public Permissions(DMPGame dmpGame, NetworkWorker networkWorker, Settings dmpSettings, Groups groups)
        {
            this.dmpGame = dmpGame;
            this.networkWorker = networkWorker;
            this.dmpSettings = dmpSettings;
            this.groups = groups;
            updateAction = new NamedAction(ProcessMessages);
            dmpGame.updateEvent.Add(updateAction);
        }

        private void ProcessMessages()
        {
            lock (messageQueue)
            {
                while (messageQueue.Count > 0)
                {
                    ByteArray queueByteArray = messageQueue.Dequeue();
                    HandleMessage(queueByteArray.data);
                    ByteRecycler.ReleaseObject(queueByteArray);
                }
            }
        }

        public void Stop()
        {
            dmpGame.updateEvent.Remove(updateAction);
        }

        public void QueueMessage(ByteArray data)
        {
            lock (messageQueue)
            {
                ByteArray queueByteArray = ByteRecycler.GetObject(data.Length);
                Array.Copy(data.data, 0, queueByteArray.data, 0, data.Length);
                messageQueue.Enqueue(queueByteArray);
            }
        }

        private void HandleMessage(byte[] data)
        {
            using (MessageReader mr = new MessageReader(data))
            {
                PermissionMessageType type = (PermissionMessageType)mr.Read<int>();
                lock (vesselPermissions)
                {
                    switch (type)
                    {
                        case PermissionMessageType.PERMISSION_INFO:
                            Guid vesselID = new Guid(mr.Read<string>());
                            string vesselOwner = mr.Read<string>();
                            VesselPermission vesselPermission = new VesselPermission(vesselID, vesselOwner);
                            vesselPermission.protection = (VesselProtectionType)Enum.Parse(typeof(VesselProtectionType), mr.Read<string>());
                            if (mr.Read<bool>())
                            {
                                vesselPermission.group = mr.Read<string>();
                            }
                            vesselPermissions[vesselID] = vesselPermission;
                            break;
                        case PermissionMessageType.PERMISSION_SYNCED:
                            synced = true;
                            break;
                    }
                }
            }
        }

        public void SetVesselOwner(Guid guid, string owner)
        {
            if (!PlayerIsVesselOwner(dmpSettings.playerName, guid))
            {
                return;
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)PermissionMessageType.SET_OWNER);
                mw.Write<string>(guid.ToString());
                mw.Write<string>(owner);
                networkWorker.SendPermissionsMessage(mw.GetMessageBytes());
            }
        }

        public void SetVesselGroup(Guid guid, string group)
        {
            if (!PlayerIsVesselOwner(dmpSettings.playerName, guid))
            {
                return;
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)PermissionMessageType.SET_GROUP);
                mw.Write<string>(guid.ToString());
                mw.Write<string>(group);
                networkWorker.SendPermissionsMessage(mw.GetMessageBytes());
            }
        }

        public void SetVesselProtection(Guid guid, VesselProtectionType protection)
        {
            if (!PlayerIsVesselOwner(dmpSettings.playerName, guid))
            {
                return;
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)PermissionMessageType.SET_PERMISSION_LEVEL);
                mw.Write<string>(guid.ToString());
                mw.Write<string>(protection.ToString());
                networkWorker.SendPermissionsMessage(mw.GetMessageBytes());
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
                    if (groups.PlayerInGroup(playerName, vp.group))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
