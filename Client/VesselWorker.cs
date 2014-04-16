using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselWorker
    {
        private const float VESSEL_SEND_INTERVAL = 30f;
        public bool enabled;
        private Client parent;
        //Incoming queue
        private Queue<ConfigNode> newVessels;
        private Queue<ConfigNode> newKerbals;
        private Queue<ActiveVesselEntry> newActiveVessels;
        //Vessel state tracking
        private string lastVessel;
        //Known kerbals
        private CrewRoster serverKerbals;
        //Known vessels and last send/receive time
        private Dictionary<string, float> serverVessels;
        //Vessel id (key) owned by player (value)
        private Dictionary<string, string> inUse;

        public VesselWorker(Client parent)
        {
            this.parent = parent;
            Reset();
            if (this.parent != null)
            {
                //Shutup compiler
            }
        }
        //Called from main
        public void FixedUpdate()
        {
            if (enabled == true)
            {
                while (newActiveVessels.Count > 0)
                {
                    ActiveVesselEntry ave = newActiveVessels.Dequeue();
                    if (ave.vesselID != "")
                    {
                        DarkLog.Debug("Player " + ave.player + " is now flying " + ave.vesselID);
                        SetInUse(ave.vesselID, ave.player);
                    }
                    else
                    {
                        DarkLog.Debug("Player " + ave.player + " has released their vessel");
                        SetNotInUse(ave.player);
                    }
                }
                while (newKerbals.Count > 0)
                {
                    //Do something
                    ConfigNode newKerbal = newKerbals.Dequeue();
                    LoadKerbal(newKerbal);
                }
                while (newVessels.Count > 0)
                {
                    //Do something
                    ConfigNode newVessel = newVessels.Dequeue();
                    LoadVessel(newVessel);
                }
                bool isActiveVesselOk = FlightGlobals.ActiveVessel != null ? (FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed) : false;
                if (isActiveVesselOk && HighLogic.LoadedScene == GameScenes.FLIGHT)
                {
                    if (!inUse.ContainsKey(FlightGlobals.ActiveVessel.id.ToString()))
                    {
                        SetInUse(FlightGlobals.ActiveVessel.id.ToString(), parent.playerName);
                        parent.networkWorker.SendActiveVessel(FlightGlobals.ActiveVessel.id.ToString());
                    }
                }
                else
                {
                    if (lastVessel != "")
                    {
                        lastVessel = "";
                        parent.networkWorker.SendActiveVessel("");
                        SetNotInUse(parent.playerName);
                    }
                }
                foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
                {
                    //Send updates for unpacked vessels that aren't being flown by other players
                    bool notInUseOrOurs = inUse.ContainsKey(checkVessel.id.ToString()) ? (inUse[checkVessel.id.ToString()] == parent.playerName) : true;
                    if (checkVessel.loaded && !checkVessel.packed && notInUseOrOurs)
                    {
                        //Check that is hasn't been recently sent
                        bool notRecentlySent = serverVessels.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVessels[checkVessel.id.ToString()]) > VESSEL_SEND_INTERVAL) : true;
                        if (notRecentlySent)
                        {
                            serverVessels[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                            if (checkVessel.protoVessel != null)
                            {
                                //Also check for kerbal state changes
                                foreach (ProtoPartSnapshot part in checkVessel.protoVessel.protoPartSnapshots)
                                {
                                    foreach (ProtoCrewMember pcm in part.protoModuleCrew)
                                    {
                                        if (!serverKerbals.ExistsInRoster(pcm.name))
                                        {
                                            //New kerbal
                                            parent.networkWorker.SendKerbalProtoMessage(pcm);
                                        }
                                        else
                                        {

                                            foreach (ProtoCrewMember serverPcm in serverKerbals)
                                            {
                                                if (pcm.name == serverPcm.name)
                                                {
                                                    bool kerbalDifferent = false;
                                                    kerbalDifferent = (pcm.courage != serverPcm.courage) || kerbalDifferent;
                                                    kerbalDifferent = (pcm.isBadass != serverPcm.isBadass) || kerbalDifferent;
                                                    kerbalDifferent = (pcm.rosterStatus != serverPcm.rosterStatus) || kerbalDifferent;
                                                    kerbalDifferent = (pcm.seatIdx != serverPcm.seatIdx) || kerbalDifferent;
                                                    kerbalDifferent = (pcm.stupidity != serverPcm.stupidity) || kerbalDifferent;
                                                    kerbalDifferent = (pcm.UTaR != serverPcm.UTaR) || kerbalDifferent;
                                                    if (kerbalDifferent)
                                                    {
                                                        DarkLog.Debug("Found changed kerbal, sending...");
                                                        parent.networkWorker.SendKerbalProtoMessage(pcm);
                                                        serverPcm.courage = pcm.courage;
                                                        serverPcm.isBadass = pcm.isBadass;
                                                        serverPcm.rosterStatus = pcm.rosterStatus;
                                                        serverPcm.seatIdx = pcm.seatIdx;
                                                        serverPcm.stupidity = pcm.stupidity;
                                                        serverPcm.UTaR = pcm.UTaR;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                parent.networkWorker.SendVesselProtoMessage(checkVessel.protoVessel);
                            }
                            else
                            {
                                DarkLog.Debug("Failed to send protovessel for " + checkVessel.id);
                            }
                        }
                    }
                }
            }
        }

        private void SetNotInUse(string player)
        {
            string deleteKey = "";
            foreach (KeyValuePair<string, string> inUseEntry in inUse)
            {
                if (inUseEntry.Value == player)
                {
                    deleteKey = inUseEntry.Key;
                }
            }
            if (deleteKey != "")
            {
                inUse.Remove(deleteKey);
            }
        }

        private void SetInUse(string vesselID, string player)
        {
            SetNotInUse(player);
            inUse[vesselID] = player;
        }
        //Called from main
        public void LoadKerbalsIntoGame()
        {
            DarkLog.Debug("Loading " + newKerbals.Count + " received kerbals");
            while (newKerbals.Count > 0)
            {
                ConfigNode currentNode = newKerbals.Dequeue();
                LoadKerbal(currentNode);
            }
            int serverKerbalsCount = 0;
            int generateKerbals = 0;
            foreach (ProtoCrewMember pcm in serverKerbals)
            {
                serverKerbalsCount++;
            }
            if (serverKerbalsCount <= 50)
            {
                generateKerbals = 50 - serverKerbalsCount;
                DarkLog.Debug("Generating " + generateKerbals + " new kerbals");
            }
            while (generateKerbals > 0)
            {
                ProtoCrewMember protoKerbal = CrewGenerator.RandomCrewMemberPrototype();
                if (!HighLogic.CurrentGame.CrewRoster.ExistsInRoster(protoKerbal.name))
                {
                    parent.networkWorker.SendKerbalProtoMessage(protoKerbal);
                    HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoKerbal);
                    serverKerbals.AddCrewMember(new ProtoCrewMember(protoKerbal));
                    generateKerbals--;
                }
            }
        }

        private void LoadKerbal(ConfigNode crewNode)
        {
            if (crewNode != null)
            {
                ProtoCrewMember protoCrew = new ProtoCrewMember(crewNode);
                if (protoCrew != null)
                {
                    if (!String.IsNullOrEmpty(protoCrew.name))
                    {
                        //DarkLog.Debug("Loading " + protoCrew.name);
                        HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoCrew);
                        serverKerbals.AddCrewMember(new ProtoCrewMember(protoCrew));
                    }
                    else
                    {
                        DarkLog.Debug("protoName is blank!");
                    }
                }
                else
                {
                    DarkLog.Debug("protoCrew is null!");
                }
            }
            else
            {
                DarkLog.Debug("crewNode is null!");
            }
        }
        //Called from main
        public void LoadVesselsIntoGame()
        {
            DarkLog.Debug("Loading " + newVessels.Count + " vessels");
            while (newVessels.Count > 0)
            {
                ConfigNode currentNode = newVessels.Dequeue();
                LoadVessel(currentNode);
            }
        }

        private void LoadVessel(ConfigNode vesselNode)
        {
            if (vesselNode != null)
            {
                ProtoVessel currentProto = new ProtoVessel(vesselNode, HighLogic.CurrentGame);
                if (currentProto != null)
                {
                    DarkLog.Debug("Loading " + currentProto.vesselID + ", name: " + currentProto.vesselName + ", type: " + currentProto.vesselType);
                    bool wasActive = false;
                    bool wasTarget = false;
                    if (HighLogic.LoadedSceneIsFlight)
                    {
                        if (FlightGlobals.activeTarget != null)
                        {
                            wasTarget = (FlightGlobals.activeTarget.vessel != null) ? (FlightGlobals.activeTarget.vessel.id == currentProto.vesselID) : false;
                        }
                        wasActive = (FlightGlobals.ActiveVessel != null) ? (FlightGlobals.ActiveVessel.id == currentProto.vesselID) : false;
                    }
                    foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
                    {
                        if (checkVessel.id == currentProto.vesselID)
                        {
                            DarkLog.Debug("Replacing old vessel");
                            checkVessel.Die();
                        }
                    }
                    serverVessels[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                    currentProto.Load(HighLogic.CurrentGame.flightState);
                    if (currentProto.vesselRef != null)
                    {
                        if (wasActive)
                        {
                            DarkLog.Debug("Set active vessel");
                            FlightGlobals.ForceSetActiveVessel(currentProto.vesselRef);
                        }
                        if (wasTarget)
                        {
                            DarkLog.Debug("Set docking target");
                            FlightGlobals.fetch.SetVesselTarget(currentProto.vesselRef);
                        }
                        DarkLog.Debug("Protovessel Loaded");
                    }
                    else
                    {
                        DarkLog.Debug("Protovessel " + currentProto.vesselID + " failed to create a vessel!");
                    }
                }
                else
                {
                    DarkLog.Debug("protoVessel is null!");
                }
            }
            else
            {
                DarkLog.Debug("vesselNode is null!");
            }
        }
        //Called from networkWorker
        public void QueueKerbal(ConfigNode kerbalNode)
        {
            newKerbals.Enqueue(kerbalNode);
        }
        //Called from networkWorker
        public void QueueVessel(ConfigNode vesselNode)
        {
            newVessels.Enqueue(vesselNode);
        }

        public void QueueActiveVessel(string player, string vesselID)
        {
            ActiveVesselEntry ave = new ActiveVesselEntry();
            ave.player = player;
            ave.vesselID = vesselID;
            newActiveVessels.Enqueue(ave);
        }
        //Called from main
        public void Reset()
        {
            enabled = false;
            newActiveVessels = new Queue<ActiveVesselEntry>();
            newKerbals = new Queue<ConfigNode>();
            newVessels = new Queue<ConfigNode>();
            serverKerbals = new CrewRoster();
            serverVessels = new Dictionary<string, float>();
            inUse = new Dictionary<string, string>();
            lastVessel = "";
        }
    }

    class ActiveVesselEntry
    {
        public string player;
        public string vesselID;
    }
}

