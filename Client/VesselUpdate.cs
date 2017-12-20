using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselUpdate
    {
        public Guid vesselID;
        public double planetTime;
        public string bodyName;
        public float[] rotation;
        public float[] angularVelocity;
        public FlightCtrlState flightState;
        public bool[] actiongroupControls;
        public bool isSurfaceUpdate;
        //Orbital parameters
        public double[] orbit;
        //Surface parameters
        //Position = lat,long,alt,ground height.
        public double[] position;
        public double[] velocity;
        public double[] acceleration;
        public float[] terrainNormal;
        //SAS
        public bool sasEnabled;
        public int autopilotMode;
        public float[] lockedRotation;
        //Private
        private VesselWorker vesselWorker;

        public VesselUpdate(VesselWorker vesselWorker)
        {
            this.vesselWorker = vesselWorker;
        }

        public static VesselUpdate CopyFromVessel(VesselWorker vesselWorker, Vessel updateVessel)
        {
            VesselUpdate returnUpdate = new VesselUpdate(vesselWorker);
            try
            {
                returnUpdate.vesselID = updateVessel.id;
                returnUpdate.planetTime = Planetarium.GetUniversalTime();
                returnUpdate.bodyName = updateVessel.mainBody.bodyName;

                returnUpdate.rotation = new float[4];
                returnUpdate.rotation[0] = updateVessel.srfRelRotation.x;
                returnUpdate.rotation[1] = updateVessel.srfRelRotation.y;
                returnUpdate.rotation[2] = updateVessel.srfRelRotation.z;
                returnUpdate.rotation[3] = updateVessel.srfRelRotation.w;

                returnUpdate.angularVelocity = new float[3];
                returnUpdate.angularVelocity[0] = updateVessel.angularVelocity.x;
                returnUpdate.angularVelocity[1] = updateVessel.angularVelocity.y;
                returnUpdate.angularVelocity[2] = updateVessel.angularVelocity.z;
                //Flight state
                returnUpdate.flightState = new FlightCtrlState();
                returnUpdate.flightState.CopyFrom(updateVessel.ctrlState);
                returnUpdate.actiongroupControls = new bool[5];
                returnUpdate.actiongroupControls[0] = updateVessel.ActionGroups[KSPActionGroup.Gear];
                returnUpdate.actiongroupControls[1] = updateVessel.ActionGroups[KSPActionGroup.Light];
                returnUpdate.actiongroupControls[2] = updateVessel.ActionGroups[KSPActionGroup.Brakes];
                returnUpdate.actiongroupControls[3] = updateVessel.ActionGroups[KSPActionGroup.SAS];
                returnUpdate.actiongroupControls[4] = updateVessel.ActionGroups[KSPActionGroup.RCS];

                if (updateVessel.altitude < 10000)
                {
                    //Use surface position under 10k
                    returnUpdate.isSurfaceUpdate = true;
                    returnUpdate.position = new double[4];
                    returnUpdate.position[0] = updateVessel.latitude;
                    returnUpdate.position[1] = updateVessel.longitude;
                    returnUpdate.position[2] = updateVessel.altitude;
                    VesselUtil.DMPRaycastPair groundRaycast = VesselUtil.RaycastGround(updateVessel.latitude, updateVessel.longitude, updateVessel.mainBody);
                    returnUpdate.position[3] = groundRaycast.altitude;
                    returnUpdate.terrainNormal = new float[3];
                    returnUpdate.terrainNormal[0] = groundRaycast.terrainNormal.x;
                    returnUpdate.terrainNormal[1] = groundRaycast.terrainNormal.y;
                    returnUpdate.terrainNormal[2] = groundRaycast.terrainNormal.z;
                    returnUpdate.velocity = new double[3];
                    Vector3d srfVel = Quaternion.Inverse(updateVessel.mainBody.bodyTransform.rotation) * updateVessel.srf_velocity;
                    returnUpdate.velocity[0] = srfVel.x;
                    returnUpdate.velocity[1] = srfVel.y;
                    returnUpdate.velocity[2] = srfVel.z;
                    returnUpdate.acceleration = new double[3];
                    Vector3d srfAcceleration = Quaternion.Inverse(updateVessel.mainBody.bodyTransform.rotation) * updateVessel.acceleration;
                    returnUpdate.acceleration[0] = srfAcceleration.x;
                    returnUpdate.acceleration[1] = srfAcceleration.y;
                    returnUpdate.acceleration[2] = srfAcceleration.z;
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
                returnUpdate.sasEnabled = updateVessel.Autopilot.Enabled;
                if (returnUpdate.sasEnabled)
                {
                    returnUpdate.autopilotMode = (int)updateVessel.Autopilot.Mode;
                    returnUpdate.lockedRotation = new float[4];
                    returnUpdate.lockedRotation[0] = updateVessel.Autopilot.SAS.lockedRotation.x;
                    returnUpdate.lockedRotation[1] = updateVessel.Autopilot.SAS.lockedRotation.y;
                    returnUpdate.lockedRotation[2] = updateVessel.Autopilot.SAS.lockedRotation.z;
                    returnUpdate.lockedRotation[3] = updateVessel.Autopilot.SAS.lockedRotation.w;
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Failed to get vessel update, exception: " + e);
                returnUpdate = null;
            }
            return returnUpdate;
        }

        public void Apply(PosistionStatistics posistionStatistics, Dictionary<Guid, VesselCtrlUpdate> ctrlUpdate)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                return;
            }

            //Ignore updates to our own vessel if we are in flight and we aren't spectating
            if (!vesselWorker.isSpectating && (FlightGlobals.fetch.activeVessel != null ? FlightGlobals.fetch.activeVessel.id == vesselID : false) && HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                return;
            }
            Vessel updateVessel = FlightGlobals.fetch.vessels.FindLast(v => v.id == vesselID);
            if (updateVessel == null)
            {
                //DarkLog.Debug("ApplyVesselUpdate - Got vessel update for " + vesselID + " but vessel does not exist");
                return;
            }
            CelestialBody updateBody = FlightGlobals.Bodies.Find(b => b.bodyName == bodyName);
            if (updateBody == null)
            {
                //DarkLog.Debug("ApplyVesselUpdate - updateBody not found");
                return;
            }

            Quaternion normalRotate = Quaternion.identity;
            double distanceError = 0;
            double velocityError = 0;
            double rotationalError = 0;
            //Position/Velocity
            if (isSurfaceUpdate)
            {
                //Get the new position/velocity
                double altitudeFudge = 0;
                VesselUtil.DMPRaycastPair dmpRaycast = VesselUtil.RaycastGround(position[0], position[1], updateBody);
                if (dmpRaycast.altitude != -1d && position[3] != -1d)
                {

                    Vector3 theirNormal = new Vector3(terrainNormal[0], terrainNormal[1], terrainNormal[2]);
                    altitudeFudge = dmpRaycast.altitude - position[3];
                    if (Math.Abs(position[2] - position[3]) < 50f)
                    {
                        normalRotate = Quaternion.FromToRotation(theirNormal, dmpRaycast.terrainNormal);
                    }
                }

                double planetariumDifference = Planetarium.GetUniversalTime() - planetTime;

                //Velocity fudge
                Vector3d updateAcceleration = updateBody.bodyTransform.rotation * new Vector3d(acceleration[0], acceleration[1], acceleration[2]);
                Vector3d velocityFudge = Vector3d.zero;
                if (Math.Abs(planetariumDifference) < 3f)
                {
                    //Velocity = a*t
                    velocityFudge = updateAcceleration * planetariumDifference;
                }

                //Position fudge
                Vector3d updateVelocity = updateBody.bodyTransform.rotation * new Vector3d(velocity[0], velocity[1], velocity[2]) + velocityFudge - Krakensbane.GetFrameVelocity();
                Vector3d positionFudge = Vector3d.zero;

                if (Math.Abs(planetariumDifference) < 3f)
                {
                    //Use the average velocity to determine the new position
                    //Displacement = v0*t + 1/2at^2.
                    positionFudge = (updateVelocity * planetariumDifference) + (0.5d * updateAcceleration * planetariumDifference * planetariumDifference);
                }

                Vector3d updatePostion = updateBody.GetWorldSurfacePosition(position[0], position[1], position[2] + altitudeFudge) + positionFudge;

                //Posisional Error Tracking
                distanceError = Vector3d.Distance(updatePostion, updateVessel.GetWorldPos3D());

                double latitude = updateBody.GetLatitude(updatePostion);
                double longitude = updateBody.GetLongitude(updatePostion);
                double altitude = updateBody.GetAltitude(updatePostion);
                updateVessel.latitude = latitude;
                updateVessel.longitude = longitude;
                updateVessel.altitude = altitude;
                updateVessel.protoVessel.latitude = latitude;
                updateVessel.protoVessel.longitude = longitude;
                updateVessel.protoVessel.altitude = altitude;

                if (updateVessel.packed)
                {
                    if (!updateVessel.LandedOrSplashed)
                    {
                        //Not landed but under 10km.
                        Vector3d orbitalPos = updatePostion - updateBody.position;
                        Vector3d surfaceOrbitVelDiff = updateBody.getRFrmVel(updatePostion);
                        Vector3d orbitalVel = updateVelocity + surfaceOrbitVelDiff;
                        velocityError = Vector3d.Distance(orbitalVel, updateVessel.obt_velocity);
                        updateVessel.orbitDriver.orbit.UpdateFromStateVectors(orbitalPos.xzy, orbitalVel.xzy, updateBody, Planetarium.GetUniversalTime());
                        updateVessel.orbitDriver.pos = updateVessel.orbitDriver.orbit.pos.xzy;
                        updateVessel.orbitDriver.vel = updateVessel.orbitDriver.orbit.vel;
                    }
                }
                else
                {
                    updateVessel.SetPosition(updatePostion, true);
                    updateVessel.SetWorldVelocity(updateVelocity);
                }
            }
            else
            {
                Orbit updateOrbit = new Orbit(orbit[0], orbit[1], orbit[2], orbit[3], orbit[4], orbit[5], orbit[6], updateBody);
                updateOrbit.Init();
                updateOrbit.UpdateFromUT(Planetarium.GetUniversalTime());

                //Positional Error Tracking
                distanceError = Vector3d.Distance(updateVessel.GetWorldPos3D(), updateOrbit.pos);
                double latitude = updateBody.GetLatitude(updateOrbit.pos);
                double longitude = updateBody.GetLongitude(updateOrbit.pos);
                double altitude = updateBody.GetAltitude(updateOrbit.pos);
                updateVessel.latitude = latitude;
                updateVessel.longitude = longitude;
                updateVessel.altitude = altitude;
                updateVessel.protoVessel.latitude = latitude;
                updateVessel.protoVessel.longitude = longitude;
                updateVessel.protoVessel.altitude = altitude;
                VesselUtil.CopyOrbit(updateOrbit, updateVessel.orbitDriver.orbit);
                updateVessel.orbitDriver.updateFromParameters();

                if (!updateVessel.packed)
                {
                    updateVessel.SetWorldVelocity(updateVessel.orbitDriver.orbit.GetVel() - updateBody.getRFrmVelOrbit(updateVessel.orbitDriver.orbit) - Krakensbane.GetFrameVelocity());
                }
            }

            //Rotation
            Quaternion unfudgedRotation = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
            Quaternion updateRotation = normalRotate * unfudgedRotation;
            //Rotational error tracking
            rotationalError = Quaternion.Angle(updateVessel.srfRelRotation, updateRotation);
            updateVessel.SetRotation(updateVessel.mainBody.bodyTransform.rotation * updateRotation);
            if (updateVessel.packed)
            {
                updateVessel.srfRelRotation = updateRotation;
                updateVessel.protoVessel.rotation = updateVessel.srfRelRotation;
            }

            //Angular velocity
            Vector3 angularVelocity = updateVessel.ReferenceTransform.rotation * new Vector3(this.angularVelocity[0], this.angularVelocity[1], this.angularVelocity[2]);
            updateVessel.precalc.CalculatePhysicsStats();

            if (updateVessel.parts != null)
            {
                for (int i = 0; i < updateVessel.parts.Count; i++)
                {
                    Part vesselPart = updateVessel.parts[i];
                    if (vesselPart.rb != null && vesselPart.State != PartStates.DEAD)
                    {
                        vesselPart.rb.angularVelocity = angularVelocity;
                        if (vesselPart != updateVessel.rootPart)
                        {
                            Vector3 rootVel = FlightGlobals.ActiveVessel.rootPart.rb.velocity;
                            Vector3 diffPos = vesselPart.rb.position - updateVessel.CoM;
                            Vector3 partVelDifference = Vector3.Cross(angularVelocity, diffPos);
                            vesselPart.rb.velocity = rootVel + partVelDifference;
                        }
                    }
                }
            }

            if (ctrlUpdate != null)
            {
                if (ctrlUpdate.ContainsKey(updateVessel.id))
                {
                    updateVessel.OnFlyByWire -= ctrlUpdate[updateVessel.id].UpdateControls;
                    ctrlUpdate.Remove(updateVessel.id);
                }
                VesselCtrlUpdate vcu = new VesselCtrlUpdate(updateVessel, ctrlUpdate, planetTime + 5, flightState);
                ctrlUpdate.Add(updateVessel.id, vcu);
                updateVessel.OnFlyByWire += vcu.UpdateControls;
            }

            //Action group controls
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.Gear, actiongroupControls[0]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.Light, actiongroupControls[1]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, actiongroupControls[2]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, actiongroupControls[3]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.RCS, actiongroupControls[4]);

            if (sasEnabled)
            {
                updateVessel.Autopilot.SetMode((VesselAutopilot.AutopilotMode)autopilotMode);
                updateVessel.Autopilot.SAS.LockRotation(new Quaternion(lockedRotation[0], lockedRotation[1], lockedRotation[2], lockedRotation[3]));
            }
            posistionStatistics.LogError(updateVessel.id, distanceError, velocityError, rotationalError, planetTime);
        }
    }
}