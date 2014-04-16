using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselWorker
    {
        public bool enabled;
        private Client parent;
        private Queue<ConfigNode> newVessels;
        private Queue<ConfigNode> newKerbals;
        private List<string> loadedVessels;
        private List<string> loadedKerbals;

        public VesselWorker (Client parent)
        {
            this.parent = parent;
            Reset();
            if (this.parent != null)
            {
                //Shutup compiler
            }
        }

        public void FixedUpdate()
        {
            if (enabled == true)
            {
                if (newKerbals.Count > 0)
                {
                    //Do something
                }
                if (newVessels.Count > 0)
                {
                    //Do something
                }
            }
        }

        public void LoadKerbalsIntoGame()
        {
            DarkLog.Debug("Loading " + newKerbals.Count + " received kerbals");
            while (newKerbals.Count > 0)
            {
                ConfigNode currentNode = newKerbals.Dequeue();
                if (currentNode != null)
                {
                    ProtoCrewMember protoCrew = new ProtoCrewMember(currentNode);
                    if (protoCrew != null)
                    {
                        if (!String.IsNullOrEmpty(protoCrew.name))
                        {
                            DarkLog.Debug("Loading " + protoCrew.name);
                            HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoCrew);
                            loadedKerbals.Add(protoCrew.name);
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
                    DarkLog.Debug("currentNode is null!");
                }
            }
            if (loadedKerbals.Count <= 50)
            {
                DarkLog.Debug("Generating " + (50 - newKerbals.Count) + " new kerbals");
            }
            while (loadedKerbals.Count <= 50)
            {
                ProtoCrewMember protoKerbal = CrewGenerator.RandomCrewMemberPrototype();
                if (!HighLogic.CurrentGame.CrewRoster.ExistsInRoster(protoKerbal.name))
                {
                    parent.networkWorker.SendKerbalMessage(protoKerbal);
                    HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoKerbal);
                    loadedKerbals.Add(protoKerbal.name);
                }
            }
        }

        public void LoadVesselsIntoGame()
        {
            DarkLog.Debug("Loading " + newVessels.Count + " vessels");
            while (newVessels.Count > 0)
            {
                ConfigNode currentNode = newVessels.Dequeue();
                if (currentNode != null)
                {
                    ProtoVessel currentProto = new ProtoVessel(currentNode, HighLogic.CurrentGame);
                    if (currentProto != null)
                    {
                        DarkLog.Debug("Loading " + currentProto.vesselID + ", name: " + currentProto.vesselName + ", type: " + currentProto.vesselType);
                        loadedVessels.Add(currentProto.vesselID.ToString());
                        currentProto.Load(HighLogic.CurrentGame.flightState);
                        if (currentProto.vesselRef != null)
                        {

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
                    DarkLog.Debug("currentNode is null!");
                }
            }
        }

        public void QueueKerbal(ConfigNode kerbalNode)
        {
            newKerbals.Enqueue(kerbalNode);
        }

        public void QueueVessel(ConfigNode vesselNode)
        {
            newVessels.Enqueue(vesselNode);
        }

        public void Reset()
        {
            enabled = false;
            newKerbals = new Queue<ConfigNode>();
            newVessels = new Queue<ConfigNode>();
            loadedVessels = new List<string>();
            loadedKerbals = new List<string>();
        }
    }
}

