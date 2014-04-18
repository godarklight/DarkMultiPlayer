using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselWorker
    {
        private const float VESSEL_PROTOVESSEL_UPDATE_INTERVAL = 30f;
        private const float VESSEL_POSITION_UPDATE_INTERVAL = .2f;
        public bool enabled;
        private Client parent;
        //Incoming queue
        private Queue<ConfigNode> vesselProtoQueue;
        private Queue<VesselUpdate> vesselUpdateQueue;
        private Queue<ConfigNode> kerbalProtoQueue;
        private Queue<ActiveVesselEntry> newActiveVessels;
        //Vessel state tracking
        private string lastVessel;
        //Known kerbals
        private CrewRoster serverKerbals;
        //Known vessels and last send/receive time
        private Dictionary<string, float> serverVesselsProtoUpdate;
        private Dictionary<string, float> serverVesselsPositionUpdate;
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
                while (kerbalProtoQueue.Count > 0)
                {
                    ConfigNode kerbalNode = kerbalProtoQueue.Dequeue();
                    LoadKerbal(kerbalNode);
                }
                while (vesselProtoQueue.Count > 0)
                {
                    ConfigNode vesselNode = vesselProtoQueue.Dequeue();
                    LoadVessel(vesselNode);
                }
                while (vesselUpdateQueue.Count > 0)
                {
                    VesselUpdate vesselUpdate = vesselUpdateQueue.Dequeue();
                    ApplyVesselUpdate(vesselUpdate);
                }
                //Tell other players we have taken a vessel
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
                //Send updates of needed vessels
                if (isActiveVesselOk)
                {
                    SendVesselUpdates();
                }
            }
        }

        private void SendVesselUpdates()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                //We aren't in flight so we have nothing to send
                return;
            }
            if (FlightGlobals.ActiveVessel == null)
            {
                //We don't have an active vessel
                return;
            }
            if (!FlightGlobals.ActiveVessel.loaded || FlightGlobals.ActiveVessel.packed)
            {
                //We haven't loaded into the game yet
                return;
            }
            foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
            {
                //Send updates for unpacked vessels that aren't being flown by other players
                bool oursOrNotInUse = inUse.ContainsKey(checkVessel.id.ToString()) ? (inUse[checkVessel.id.ToString()] == parent.playerName) : true;
                bool notRecentlySentProtoUpdate = serverVesselsProtoUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsProtoUpdate[checkVessel.id.ToString()]) > VESSEL_PROTOVESSEL_UPDATE_INTERVAL) : true;
                bool notRecentlySentPositionUpdate = serverVesselsPositionUpdate.ContainsKey(checkVessel.id.ToString()) ? ((UnityEngine.Time.realtimeSinceStartup - serverVesselsPositionUpdate[checkVessel.id.ToString()]) > VESSEL_POSITION_UPDATE_INTERVAL) : true;
                if (checkVessel.loaded && !checkVessel.packed && oursOrNotInUse)
                {
                    //Check that is hasn't been recently sent
                    if (notRecentlySentProtoUpdate)
                    {
                        //Send a protovessel update
                        serverVesselsProtoUpdate[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        //Also delay the position send
                        serverVesselsPositionUpdate[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        ProtoVessel checkProto = new ProtoVessel(checkVessel);
                        if (checkProto != null)
                        {
                            if (checkProto.vesselID != Guid.Empty)
                            {
                                //Also check for kerbal state changes
                                foreach (ProtoPartSnapshot part in checkProto.protoPartSnapshots)
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
                                parent.networkWorker.SendVesselProtoMessage(checkProto);
                            }
                            else
                            {
                                DarkLog.Debug(checkVessel.vesselName + " does not have a guid!");
                            }
                        }
                        else
                        {
                            DarkLog.Debug("Failed to send protovessel for " + checkVessel.id);
                        }
                    }
                    else if (notRecentlySentPositionUpdate)
                    {
                        //Send a position update
                        serverVesselsPositionUpdate[checkVessel.id.ToString()] = UnityEngine.Time.realtimeSinceStartup;
                        bool anotherPlayerCloser = false;
                        if (!inUse.ContainsKey(checkVessel.id.ToString()))
                        {
                            foreach (KeyValuePair<string,string> entry in inUse)
                            {
                                if (entry.Value != parent.playerName)
                                {
                                    Vessel playerVessel = FlightGlobals.fetch.vessels.Find(v => v.id.ToString() == entry.Key);
                                    if (playerVessel != null)
                                    {
                                        double ourDistance = Vector3d.Distance(FlightGlobals.ActiveVessel.GetWorldPos3D(), checkVessel.GetWorldPos3D());
                                        double theirDistance = Vector3d.Distance(FlightGlobals.ActiveVessel.GetWorldPos3D(), checkVessel.GetWorldPos3D());
                                        if (theirDistance < ourDistance)
                                        {
                                            DarkLog.Debug("Player " + entry.Value + " is closer to " + entry.Key + ", theirs: " + (float)theirDistance + ", ours: " + (float)ourDistance);
                                            anotherPlayerCloser = true;
                                        }
                                    }
                                }
                            }
                        }
                        if (!anotherPlayerCloser)
                        {
                            VesselUpdate update = GetVesselUpdate(checkVessel);
                            if (update != null)
                            {
                                parent.networkWorker.SendVesselUpdate(update);
                            }
                        }
                    }
                }
            }
        }

        private VesselUpdate GetVesselUpdate(Vessel updateVessel)
        {
            VesselUpdate returnUpdate = new VesselUpdate();
            try
            {
                returnUpdate.vesselID = updateVessel.id.ToString();
                returnUpdate.bodyName = updateVessel.mainBody.bodyName;
                returnUpdate.rotation = new float[4];
                returnUpdate.rotation[0] = updateVessel.transform.localRotation.x;
                returnUpdate.rotation[1] = updateVessel.transform.localRotation.y;
                returnUpdate.rotation[2] = updateVessel.transform.localRotation.z;
                returnUpdate.rotation[3] = updateVessel.transform.localRotation.w;
                if (updateVessel.altitude < 10000)
                {
                    //Use surface position under 10k
                    returnUpdate.isSurfaceUpdate = true;
                    returnUpdate.position = new double[3];
                    returnUpdate.position[0] = updateVessel.latitude;
                    returnUpdate.position[1] = updateVessel.longitude;
                    returnUpdate.position[2] = updateVessel.altitude;
                    returnUpdate.velocity = new double[3];
                    returnUpdate.velocity[0] = updateVessel.srf_velocity.x;
                    returnUpdate.velocity[1] = updateVessel.srf_velocity.y;
                    returnUpdate.velocity[2] = updateVessel.srf_velocity.z;
                }
                else
                {
                    //Use orbital positioning over 10k
                    returnUpdate.isSurfaceUpdate = false;
                    returnUpdate.orbit = new double[7];
                    returnUpdate.orbit[0] = updateVessel.orbit.inclination;
                    returnUpdate.orbit[1] = updateVessel.orbit.eccentricity;
                    returnUpdate.orbit[2] = updateVessel.orbit.semiMajorAxis;
                    returnUpdate.orbit[3] = updateVessel.orbit.LAN;
                    returnUpdate.orbit[4] = updateVessel.orbit.argumentOfPeriapsis;
                    returnUpdate.orbit[5] = updateVessel.orbit.meanAnomalyAtEpoch;
                    returnUpdate.orbit[6] = updateVessel.orbit.epoch;
                }

            }
            catch (Exception e)
            {
                DarkLog.Debug("Failed to get vessel update, exception: " + e);
                returnUpdate = null;
            }
            return returnUpdate;
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
            DarkLog.Debug("Loading " + kerbalProtoQueue.Count + " received kerbals");
            while (kerbalProtoQueue.Count > 0)
            {
                ConfigNode currentNode = kerbalProtoQueue.Dequeue();
                LoadKerbal(currentNode);
            }
            int serverKerbalsCount = 0;
            int generateKerbals = 0;
            foreach (ProtoCrewMember pcm in serverKerbals)
            {
                serverKerbalsCount++;
            }
            if (serverKerbalsCount < 50)
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
                        if (!HighLogic.CurrentGame.CrewRoster.ExistsInRoster(protoCrew.name))
                        {
                            HighLogic.CurrentGame.CrewRoster.AddCrewMember(protoCrew);
                            serverKerbals.AddCrewMember(new ProtoCrewMember(protoCrew));
                        }
                        else
                        {
                            foreach (ProtoCrewMember existingKerbal in HighLogic.CurrentGame.CrewRoster)
                            {
                                if (existingKerbal.name == protoCrew.name)
                                {
                                    existingKerbal.courage = protoCrew.courage;
                                    existingKerbal.isBadass = protoCrew.isBadass;
                                    existingKerbal.rosterStatus = protoCrew.rosterStatus;
                                    existingKerbal.seatIdx = protoCrew.seatIdx;
                                    existingKerbal.stupidity = protoCrew.stupidity;
                                    existingKerbal.UTaR = protoCrew.UTaR;
                                }
                            }
                            foreach (ProtoCrewMember existingKerbal in serverKerbals)
                            {
                                if (existingKerbal.name == protoCrew.name)
                                {
                                    existingKerbal.courage = protoCrew.courage;
                                    existingKerbal.isBadass = protoCrew.isBadass;
                                    existingKerbal.rosterStatus = protoCrew.rosterStatus;
                                    existingKerbal.seatIdx = protoCrew.seatIdx;
                                    existingKerbal.stupidity = protoCrew.stupidity;
                                    existingKerbal.UTaR = protoCrew.UTaR;
                                }
                            }
                        }
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
            DarkLog.Debug("Loading " + vesselProtoQueue.Count + " vessels");
            while (vesselUpdateQueue.Count > 0)
            {
                ConfigNode currentNode = vesselProtoQueue.Dequeue();
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
                    Vessel oldVessel = null;
                    foreach (Vessel checkVessel in FlightGlobals.fetch.vessels)
                    {
                        if (checkVessel.id == currentProto.vesselID)
                        {
                            oldVessel = checkVessel;
                        }
                    }
                    if (oldVessel != null)
                    {
                        DarkLog.Debug("Replacing old vessel, packed: " + oldVessel.packed);
                        oldVessel.Die();
                    }
                    serverVesselsProtoUpdate[currentProto.vesselID.ToString()] = UnityEngine.Time.realtimeSinceStartup;
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

        private void ApplyVesselUpdate(VesselUpdate update)
        {
            //Get updating player
            string updatePlayer = inUse.ContainsKey(update.vesselID) ? inUse[update.vesselID] : "Unknown";
            //Ignore updates to our own vessel
            if (FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.id.ToString() == update.vesselID : false)
            {
                DarkLog.Debug("ApplyVesselUpdate - Ignoring update for active vessel from " + updatePlayer);
                return;
            }
            Vessel updateVessel = FlightGlobals.fetch.vessels.Find(v => v.id.ToString() == update.vesselID);
            if (updateVessel == null)
            {
                DarkLog.Debug("ApplyVesselUpdate - Got vessel update for " + update.vesselID + " but vessel does not exist");
                return;
            }
            CelestialBody updateBody = FlightGlobals.Bodies.Find(b => b.bodyName == update.bodyName);
            if (updateBody == null)
            {
                DarkLog.Debug("ApplyVesselUpdate - updateBody not found");
                return;
            }
            if (update.isSurfaceUpdate)
            {
                Vector3d updatePostion = updateBody.GetWorldSurfacePosition(update.position[0], update.position[1], update.position[2]);
                Vector3d updateVelocity = new Vector3d(update.velocity[0], update.velocity[1], update.velocity[2]);
                Vector3d velocityOffset = updateVelocity - updateVessel.srf_velocity;
                updateVessel.SetPosition(updatePostion);
                updateVessel.ChangeWorldVelocity(velocityOffset);
            }
            else
            {
                Orbit updateOrbit = new Orbit(update.orbit[0], update.orbit[1], update.orbit[2], update.orbit[3], update.orbit[4], update.orbit[5], update.orbit[6], updateBody);
                CopyOrbit(updateOrbit, updateVessel.orbitDriver.orbit);
            }
            Quaternion updateRotation = new Quaternion(update.rotation[0], update.rotation[1], update.rotation[2], update.rotation[3]);
            updateVessel.SetRotation(updateRotation);
            updateVessel.angularVelocity = Vector3.zero;
        }

        //Credit where credit is due, Thanks hyperedit.
        private void CopyOrbit(Orbit sourceOrbit, Orbit destinationOrbit) {
            destinationOrbit.inclination = sourceOrbit.inclination;
            destinationOrbit.eccentricity = sourceOrbit.eccentricity;
            destinationOrbit.semiMajorAxis = sourceOrbit.semiMajorAxis;
            destinationOrbit.LAN = sourceOrbit.LAN;
            destinationOrbit.argumentOfPeriapsis = sourceOrbit.argumentOfPeriapsis;
            destinationOrbit.meanAnomalyAtEpoch = sourceOrbit.meanAnomalyAtEpoch;
            destinationOrbit.epoch = sourceOrbit.epoch;
            destinationOrbit.referenceBody = sourceOrbit.referenceBody;
        }

        //Called from networkWorker
        public void QueueKerbal(ConfigNode kerbalNode)
        {
            kerbalProtoQueue.Enqueue(kerbalNode);
        }
        //Called from networkWorker
        public void QueueVesselProto(ConfigNode vesselNode)
        {
            vesselProtoQueue.Enqueue(vesselNode);
        }

        public void QueueVesselUpdate(VesselUpdate update)
        {
            vesselUpdateQueue.Enqueue(update);
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
            kerbalProtoQueue = new Queue<ConfigNode>();
            vesselProtoQueue = new Queue<ConfigNode>();
            vesselUpdateQueue = new Queue<VesselUpdate>();
            serverKerbals = new CrewRoster();
            serverVesselsProtoUpdate = new Dictionary<string, float>();
            serverVesselsPositionUpdate = new Dictionary<string, float>();
            inUse = new Dictionary<string, string>();
            lastVessel = "";
        }
    }

    class ActiveVesselEntry
    {
        public string player;
        public string vesselID;
    }

    public class VesselUpdate
    {
        public string vesselID;
        public string bodyName;
        public float[] rotation;
        public bool isSurfaceUpdate;
        //Orbital parameters
        public double[] orbit;
        //Surface parameters
        //Position = lat,long,alt.
        public double[] position;
        public double[] velocity;
    }
}

