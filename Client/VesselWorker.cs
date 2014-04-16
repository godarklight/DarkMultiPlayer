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

        //Vessel state tracking
        private string lastVessel;

        //Known kerbals
        private List<string> serverKerbals;
        //Known vessels and last send time
        private Dictionary<string, float> serverVessels;
        //Vessel id (key) owned by player (value)
        private Dictionary<string, string> inUse;


        public VesselWorker (Client parent)
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
                if (FlightGlobals.ActiveVessel != null && HighLogic.LoadedSceneIsFlight)
                {
                    if (!inUse.ContainsKey(FlightGlobals.ActiveVessel.id.ToString())) {
                        SetInUse(FlightGlobals.ActiveVessel.id.ToString(), parent.playerName);
                        parent.networkWorker.SendActiveVessel(FlightGlobals.ActiveVessel.id.ToString());
                    }
                }
                else
                {
                    if (lastVessel != "")
                    {
                        lastVessel = "";
                        SetNotInUse(parent.playerName);
                        parent.networkWorker.SendActiveVessel(lastVessel);
                    }
                }
                foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
                {
                    //Send updates for unpacked vessels that aren't being flown by other players
                    bool notInUseOrOurs = inUse.ContainsKey(checkVessel.id.ToString()) ? (inUse[checkVessel.id.ToString()] == parent.playerName) : true;
                    if (!checkVessel.packed && notInUseOrOurs)
                    {
                        //Check that is hasn't been recently sent
                        bool notRecentlySent = serverVessels.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVessels[checkVessel.id.ToString()]) > VESSEL_SEND_INTERVAL) : true;
                        if (notRecentlySent)
                        {
                            serverVessels[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                            parent.networkWorker.SendVesselProtoMessage(checkVessel.protoVessel);
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
            if (serverKerbals.Count <= 50)
            {
                DarkLog.Debug("Generating " + (50 - newKerbals.Count) + " new kerbals");
            }
            while (serverKerbals.Count <= 50)
            {
                ProtoCrewMember protoKerbal = CrewGenerator.RandomCrewMemberPrototype();
                if (!HighLogic.CurrentGame.CrewRoster.ExistsInRoster(protoKerbal.name))
                {
                    parent.networkWorker.SendKerbalProtoMessage(protoKerbal);
                    HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoKerbal);
                    serverKerbals.Add(protoKerbal.name);
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
                        serverKerbals.Add(protoCrew.name);
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
                    if (FlightGlobals.activeTarget != null ? FlightGlobals.activeTarget.vessel.id == currentProto.vesselID : false) {
                        wasTarget = true;
                    }
                    if (FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.id == currentProto.vesselID : false)
                    {
                        wasActive = true;
                    }
                    foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
                    {
                        if (checkVessel.id == currentProto.vesselID)
                        {
                            DarkLog.Debug("Replacing old vessel");
                            checkVessel.Die();
                        }
                    }
                    serverVessels.Add(currentProto.vesselID.ToString(), UnityEngine.Time.realtimeSinceStartup);
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

        //Called from main
        public void Reset()
        {
            enabled = false;
            newKerbals = new Queue<ConfigNode>();
            newVessels = new Queue<ConfigNode>();
            serverKerbals = new List<string>();
            serverVessels = new Dictionary<string, float>();
            inUse = new Dictionary<string, string>();
        }
    }
}

