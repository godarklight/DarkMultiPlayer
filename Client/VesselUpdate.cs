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

        public static VesselUpdate CopyFromVessel(Vessel updateVessel)
        {
            VesselUpdate returnUpdate = new VesselUpdate();
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
            }
            catch (Exception e)
            {
                DarkLog.Debug("Failed to get vessel update, exception: " + e);
                returnUpdate = null;
            }
            return returnUpdate;
        }

        public void Apply()
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                return;
            }

            //Ignore updates to our own vessel if we are in flight and we aren't spectating
            if (!VesselWorker.fetch.isSpectating && (FlightGlobals.fetch.activeVessel != null ? FlightGlobals.fetch.activeVessel.id == vesselID : false) && HighLogic.LoadedScene == GameScenes.FLIGHT)
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
                Vector3d updateVelocity = updateBody.bodyTransform.rotation * new Vector3d(velocity[0], velocity[1], velocity[2]) + velocityFudge;
                Vector3d positionFudge = Vector3d.zero;

                if (Math.Abs(planetariumDifference) < 3f)
                {
                    //Use the average velocity to determine the new position
                    //Displacement = v0*t + 1/2at^2.
                    positionFudge = (updateVelocity * planetariumDifference) + (0.5d * updateAcceleration * planetariumDifference * planetariumDifference);
                }

                Vector3d updatePostion = updateBody.GetWorldSurfacePosition(position[0], position[1], position[2] + altitudeFudge) + positionFudge;

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
                        updateVessel.orbitDriver.orbit.UpdateFromStateVectors(orbitalPos.xzy, orbitalVel.xzy, updateBody, Planetarium.GetUniversalTime());
                        updateVessel.orbitDriver.pos = updateVessel.orbitDriver.orbit.pos.xzy;
                        updateVessel.orbitDriver.vel = updateVessel.orbitDriver.orbit.vel;
                    }
                }
                else
                {
                    Vector3d velocityOffset = updateVelocity - updateVessel.srf_velocity;
                    updateVessel.SetPosition(updatePostion, true);
                    updateVessel.ChangeWorldVelocity(velocityOffset);
                }
            }
            else
            {
                Orbit updateOrbit = new Orbit(orbit[0], orbit[1], orbit[2], orbit[3], orbit[4], orbit[5], orbit[6], updateBody);
                updateOrbit.Init();
                updateOrbit.UpdateFromUT(Planetarium.GetUniversalTime());

                double latitude = updateBody.GetLatitude(updateOrbit.pos);
                double longitude = updateBody.GetLongitude(updateOrbit.pos);
                double altitude = updateBody.GetAltitude(updateOrbit.pos);
                updateVessel.latitude = latitude;
                updateVessel.longitude = longitude;
                updateVessel.altitude = altitude;
                updateVessel.protoVessel.latitude = latitude;
                updateVessel.protoVessel.longitude = longitude;
                updateVessel.protoVessel.altitude = altitude;

                if (updateVessel.packed)
                {
                    //The OrbitDriver update call will set the vessel position on the next fixed update
                    VesselUtil.CopyOrbit(updateOrbit, updateVessel.orbitDriver.orbit);
                    updateVessel.orbitDriver.pos = updateVessel.orbitDriver.orbit.pos.xzy;
                    updateVessel.orbitDriver.vel = updateVessel.orbitDriver.orbit.vel;
                }
                else
                {
                    //Vessel.SetPosition is full of fun and games. Avoid at all costs.
                    //Also, It's quite difficult to figure out the world velocity due to Krakensbane, and the reference frame.
                    Vector3d posDelta = updateOrbit.getPositionAtUT(Planetarium.GetUniversalTime()) - updateVessel.orbitDriver.orbit.getPositionAtUT(Planetarium.GetUniversalTime());
                    Vector3d velDelta = updateOrbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime()).xzy - updateVessel.orbitDriver.orbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime()).xzy;
                    //Vector3d velDelta = updateOrbit.vel.xzy - updateVessel.orbitDriver.orbit.vel.xzy;
                    updateVessel.Translate(posDelta);
                    updateVessel.ChangeWorldVelocity(velDelta);
                }
            }

            //Rotation
            Quaternion unfudgedRotation = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
            Quaternion updateRotation = normalRotate * unfudgedRotation;
            updateVessel.SetRotation(updateVessel.mainBody.bodyTransform.rotation * updateRotation);
            if (updateVessel.packed)
            {
                updateVessel.srfRelRotation = updateRotation;
                updateVessel.protoVessel.rotation = updateVessel.srfRelRotation;
            }

            //Angular velocity
            //Vector3 angularVelocity = new Vector3(this.angularVelocity[0], this.angularVelocity[1], this.angularVelocity[2]);
            if (updateVessel.parts != null)
            {
                //Vector3 newAng = updateVessel.ReferenceTransform.rotation * angularVelocity;
                foreach (Part vesselPart in updateVessel.parts)
                {
                    if (vesselPart.rb != null && !vesselPart.rb.isKinematic && vesselPart.State == PartStates.ACTIVE)
                    {
                        //The parts can have different rotations - This transforms them into the root part direction which is where the angular velocity is transferred.
                        //vesselPart.rb.angularVelocity = (Quaternion.Inverse(updateVessel.rootPart.rb.rotation) * vesselPart.rb.rotation) * newAng;
                        vesselPart.rb.angularVelocity = Vector3.zero;
                    }
                }
            }

            //Flight state controls (Throttle etc)
            if (!VesselWorker.fetch.isSpectating)
            {
                updateVessel.ctrlState.CopyFrom(flightState);
            }
            else
            {
                FlightInputHandler.state.CopyFrom(flightState);
            }

            //Action group controls
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.Gear, actiongroupControls[0]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.Light, actiongroupControls[1]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, actiongroupControls[2]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.SAS, actiongroupControls[3]);
            updateVessel.ActionGroups.SetGroup(KSPActionGroup.RCS, actiongroupControls[4]);
        }
    }
}


