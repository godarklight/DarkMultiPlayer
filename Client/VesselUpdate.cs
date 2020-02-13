using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselUpdate
    {
        public bool updateOK = true;
        public Guid vesselID;
        public double planetTime;
        public string bodyName;
        public float[] rotation = new float[4];
        public float[] angularVelocity = new float[3];
        public FlightCtrlState flightState = new FlightCtrlState();
        public bool[] actiongroupControls = new bool[5];
        public bool isSurfaceUpdate;
        //Orbital parameters
        public double[] orbit = new double[7];
        //Surface parameters
        //Position = lat,long,alt,ground height.
        public double[] position = new double[4];
        public double[] velocity = new double[3];
        public double[] acceleration = new double[3];
        public float[] terrainNormal = new float[3];
        //SAS
        public bool sasEnabled;
        public int autopilotMode;
        public float[] lockedRotation= new float[4];
        //Private
        private const double STEP_DISTANCE = 0.05;
        private VesselWorker vesselWorker;

        public void SetVesselWorker(VesselWorker vesselWorker)
        {
            this.vesselWorker = vesselWorker;
        }

        public void CopyFromVessel(Vessel updateVessel)
        {
            updateOK = true;
            SetVesselWorker(vesselWorker);
            try
            {
                vesselID = updateVessel.id;
                planetTime = Planetarium.GetUniversalTime();
                bodyName = updateVessel.mainBody.bodyName;

                rotation[0] = updateVessel.srfRelRotation.x;
                rotation[1] = updateVessel.srfRelRotation.y;
                rotation[2] = updateVessel.srfRelRotation.z;
                rotation[3] = updateVessel.srfRelRotation.w;

                angularVelocity[0] = updateVessel.angularVelocity.x;
                angularVelocity[1] = updateVessel.angularVelocity.y;
                angularVelocity[2] = updateVessel.angularVelocity.z;
                //Flight state
                flightState.CopyFrom(updateVessel.ctrlState);
                actiongroupControls[0] = updateVessel.ActionGroups[KSPActionGroup.Gear];
                actiongroupControls[1] = updateVessel.ActionGroups[KSPActionGroup.Light];
                actiongroupControls[2] = updateVessel.ActionGroups[KSPActionGroup.Brakes];
                actiongroupControls[3] = updateVessel.ActionGroups[KSPActionGroup.SAS];
                actiongroupControls[4] = updateVessel.ActionGroups[KSPActionGroup.RCS];

                if (updateVessel.altitude < 10000)
                {
                    //Use surface position under 10k
                    isSurfaceUpdate = true;
                    position[0] = updateVessel.latitude;
                    position[1] = updateVessel.longitude;
                    position[2] = updateVessel.altitude;
                    VesselUtil.DMPRaycastPair groundRaycast = VesselUtil.RaycastGround(updateVessel.latitude, updateVessel.longitude, updateVessel.mainBody);
                    position[3] = groundRaycast.altitude;
                    terrainNormal[0] = groundRaycast.terrainNormal.x;
                    terrainNormal[1] = groundRaycast.terrainNormal.y;
                    terrainNormal[2] = groundRaycast.terrainNormal.z;
                    Vector3d srfVel = Quaternion.Inverse(updateVessel.mainBody.bodyTransform.rotation) * updateVessel.srf_velocity;
                    velocity[0] = srfVel.x;
                    velocity[1] = srfVel.y;
                    velocity[2] = srfVel.z;
                    Vector3d srfAcceleration = Quaternion.Inverse(updateVessel.mainBody.bodyTransform.rotation) * updateVessel.acceleration;
                    acceleration[0] = srfAcceleration.x;
                    acceleration[1] = srfAcceleration.y;
                    acceleration[2] = srfAcceleration.z;
                }
                else
                {
                    //Use orbital positioning over 10k
                    isSurfaceUpdate = false;
                    orbit[0] = updateVessel.orbit.inclination;
                    orbit[1] = updateVessel.orbit.eccentricity;
                    orbit[2] = updateVessel.orbit.semiMajorAxis;
                    orbit[3] = updateVessel.orbit.LAN;
                    orbit[4] = updateVessel.orbit.argumentOfPeriapsis;
                    orbit[5] = updateVessel.orbit.meanAnomalyAtEpoch;
                    orbit[6] = updateVessel.orbit.epoch;
                }
                sasEnabled = updateVessel.Autopilot.Enabled;
                if (sasEnabled)
                {
                    autopilotMode = (int)updateVessel.Autopilot.Mode;
                    lockedRotation[0] = updateVessel.Autopilot.SAS.lockedRotation.x;
                    lockedRotation[1] = updateVessel.Autopilot.SAS.lockedRotation.y;
                    lockedRotation[2] = updateVessel.Autopilot.SAS.lockedRotation.z;
                    lockedRotation[3] = updateVessel.Autopilot.SAS.lockedRotation.w;
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("Failed to get vessel update, exception: " + e);
                updateOK = false;
            }
        }

        public void CopyFromUpdate(VesselUpdate source)
        {
            updateOK = source.updateOK;
            SetVesselWorker(source.vesselWorker);
            vesselID = source.vesselID;
            planetTime = source.planetTime;
            bodyName = source.bodyName;
            rotation[0] = source.rotation[0];
            rotation[1] = source.rotation[1];
            rotation[2] = source.rotation[2];
            rotation[3] = source.rotation[3];
            angularVelocity[0] = source.angularVelocity[0];
            angularVelocity[1] = source.angularVelocity[1];
            angularVelocity[2] = source.angularVelocity[2];
            flightState.CopyFrom(source.flightState);
            actiongroupControls[0] = source.actiongroupControls[0];
            actiongroupControls[1] = source.actiongroupControls[1];
            actiongroupControls[2] = source.actiongroupControls[2];
            actiongroupControls[3] = source.actiongroupControls[3];
            actiongroupControls[4] = source.actiongroupControls[4];
            isSurfaceUpdate = source.isSurfaceUpdate;
            terrainNormal[0] = source.terrainNormal[0];
            terrainNormal[1] = source.terrainNormal[1];
            terrainNormal[2] = source.terrainNormal[2];
            velocity[0] = source.velocity[0];
            velocity[1] = source.velocity[1];
            velocity[2] = source.velocity[2];
            acceleration[0] = source.acceleration[0];
            acceleration[1] = source.acceleration[1];
            acceleration[2] = source.acceleration[2];
            orbit[0] = source.orbit[0];
            orbit[1] = source.orbit[1];
            orbit[2] = source.orbit[2];
            orbit[3] = source.orbit[3];
            orbit[4] = source.orbit[4];
            orbit[5] = source.orbit[5];
            orbit[6] = source.orbit[6];
            sasEnabled = source.sasEnabled;
            autopilotMode = source.autopilotMode;
            lockedRotation[0] = source.lockedRotation[0];
            lockedRotation[1] = source.lockedRotation[1];
            lockedRotation[2] = source.lockedRotation[2];
            lockedRotation[3] = source.lockedRotation[3];
        }

        public void Apply(PosistionStatistics posistionStatistics, Dictionary<Guid, VesselCtrlUpdate> ctrlUpdate, VesselUpdate previousUpdate, VesselUpdate nextUpdate, Settings dmpSettings)
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

            double interpolatorDelay = 0f;
            if (dmpSettings.interpolatorType == InterpolatorType.INTERPOLATE1S)
            {
                interpolatorDelay = 1f;
            }
            if (dmpSettings.interpolatorType == InterpolatorType.INTERPOLATE3S)
            {
                interpolatorDelay = 3f;
            }

            bool interpolatorEnabled = dmpSettings.interpolatorType == InterpolatorType.INTERPOLATE1S || dmpSettings.interpolatorType == InterpolatorType.INTERPOLATE3S;
            bool extrapolatorEnabled = dmpSettings.interpolatorType == InterpolatorType.EXTRAPOLATE;
            Quaternion normalRotate = Quaternion.identity;
            Vector3 oldPos = updateVessel.GetWorldPos3D();
            Vector3 oldVelocity = updateVessel.orbitDriver.orbit.GetVel();
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
                Vector3d updateAcceleration = updateBody.bodyTransform.rotation * new Vector3d(acceleration[0], acceleration[1], acceleration[2]);
                Vector3d updateVelocity = updateBody.bodyTransform.rotation * new Vector3d(velocity[0], velocity[1], velocity[2]);
                Vector3d updatePostion = updateBody.GetWorldSurfacePosition(position[0], position[1], position[2] + altitudeFudge);
                Vector3d newUpdatePostion = updatePostion;
                Vector3d newUpdateVelocity = updateVelocity;
                double planetariumDifference = Planetarium.GetUniversalTime() - (planetTime + interpolatorDelay);

                if (extrapolatorEnabled)
                {
                    if (Math.Abs(planetariumDifference) < 3f)
                    {
                        Extrapolate(updatePostion, updateVelocity, updateAcceleration, planetariumDifference, out newUpdatePostion, out newUpdateVelocity);
                    }
                }

                if (interpolatorEnabled)
                {
                    if (nextUpdate != null && (Math.Abs(nextUpdate.planetTime - Planetarium.GetUniversalTime())) < 5f)
                    {
                        double scaling = (Planetarium.GetUniversalTime() - interpolatorDelay - planetTime) / (nextUpdate.planetTime - planetTime);
                        Vector3d nextPosition = updateBody.GetWorldSurfacePosition(nextUpdate.position[0], nextUpdate.position[1], nextUpdate.position[2] + altitudeFudge);
                        Vector3d nextVelocity = updateBody.bodyTransform.rotation * new Vector3d(nextUpdate.velocity[0], nextUpdate.velocity[1], nextUpdate.velocity[2]);
                        newUpdatePostion = Vector3d.Lerp(updatePostion, nextPosition, scaling);
                        newUpdateVelocity = Vector3d.Lerp(updateVelocity, nextVelocity, scaling);
                    }
                    else
                    {
                        if (Math.Abs(planetariumDifference) < 3f)
                        {
                            Extrapolate(updatePostion, updateVelocity, updateAcceleration, planetariumDifference, out newUpdatePostion, out newUpdateVelocity);
                        }
                    }
                }
                Vector3d vectorBetweenCoMandRoot = updateVessel.orbitDriver.driverTransform.rotation * updateVessel.localCoM;
                Vector3d orbitalPos = newUpdatePostion + vectorBetweenCoMandRoot - updateBody.position;
                Vector3d surfaceOrbitVelDiff = updateBody.getRFrmVel(newUpdatePostion);
                Vector3d orbitalVel = newUpdateVelocity + surfaceOrbitVelDiff;
                updateVessel.orbitDriver.orbit.UpdateFromStateVectors(orbitalPos.xzy, orbitalVel.xzy, updateBody, Planetarium.GetUniversalTime());
            }
            else
            {
                updateVessel.orbit.SetOrbit(orbit[0], orbit[1], orbit[2], orbit[3], orbit[4], orbit[5], orbit[6], updateBody);
            }
            //Updates orbit.pos/vel
            updateVessel.orbitDriver.orbit.UpdateFromOrbitAtUT(updateVessel.orbitDriver.orbit, Planetarium.GetUniversalTime(), updateBody);
            //Updates vessel pos from the orbit, as if on rails
            updateVessel.orbitDriver.updateFromParameters();
            //Rotation
            Quaternion unfudgedRotation = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
            //Rotation extrapolation? :O
            if (previousUpdate != null && (extrapolatorEnabled || (interpolatorEnabled && !isSurfaceUpdate)))
            {
                double deltaUpdateT = planetTime - previousUpdate.planetTime;
                double deltaRealT = Planetarium.GetUniversalTime() - previousUpdate.planetTime;
                float scaling = (float)(deltaRealT / deltaUpdateT);
                if (Math.Abs(deltaRealT) < 3f)
                {
                    Quaternion previousRotation = new Quaternion(previousUpdate.rotation[0], previousUpdate.rotation[1], previousUpdate.rotation[2], previousUpdate.rotation[3]);
                    unfudgedRotation = RotationLerp(previousRotation, unfudgedRotation, scaling);
                }
            }
            if (nextUpdate != null && interpolatorEnabled && isSurfaceUpdate)
            {
                double deltaUpdateT = nextUpdate.planetTime - planetTime;
                double deltaRealT = Planetarium.GetUniversalTime() - interpolatorDelay - planetTime;
                float scaling = (float)(deltaRealT / deltaUpdateT);
                if (Math.Abs(deltaRealT) < 3f)
                {
                    Quaternion nextRotation = new Quaternion(nextUpdate.rotation[0], nextUpdate.rotation[1], nextUpdate.rotation[2], nextUpdate.rotation[3]);
                    unfudgedRotation = RotationLerp(unfudgedRotation, nextRotation, scaling);
                }
            }
            Quaternion updateRotation = normalRotate * unfudgedRotation;
            //Rotational error tracking
            double rotationalError = Quaternion.Angle(updateVessel.srfRelRotation, updateRotation);
            updateVessel.SetRotation(updateVessel.mainBody.bodyTransform.rotation * updateRotation);
            updateVessel.srfRelRotation = updateRotation;

            Vector3 angularVel = updateVessel.ReferenceTransform.rotation * new Vector3(angularVelocity[0], angularVelocity[1], angularVelocity[2]);
            if (updateVessel.parts != null)
            {
                for (int i = 0; i < updateVessel.parts.Count; i++)
                {
                    Part thisPart = updateVessel.parts[i];
                    thisPart.vel = updateVessel.orbit.GetVel() - Krakensbane.GetFrameVelocity();
                    if (thisPart.orbit.referenceBody.inverseRotation)
                    {
                        thisPart.vel -= updateBody.getRFrmVel(thisPart.partTransform.position);
                    }
                    if (thisPart.rb != null && thisPart.State != PartStates.DEAD)
                    {
                        thisPart.rb.velocity = thisPart.vel;
                        //Angular Vel
                        thisPart.rb.angularVelocity = angularVel;
                        if (thisPart != updateVessel.rootPart)
                        {
                            Vector3 diffPos = thisPart.rb.position - updateVessel.CoM;
                            Vector3 partVelDifference = Vector3.Cross(angularVel, diffPos);
                            thisPart.rb.velocity = thisPart.rb.velocity + partVelDifference;
                        }
                    }
                }
            }
            //Updates Vessel.CoMD, which is used for GetWorldPos3D
            updateVessel.precalc.CalculatePhysicsStats();
            updateVessel.latitude = updateBody.GetLatitude(updateBody.position + updateVessel.orbitDriver.pos);
            updateVessel.longitude = updateBody.GetLongitude(updateBody.position + updateVessel.orbitDriver.pos);
            updateVessel.altitude = updateVessel.orbitDriver.orbit.altitude;

            double distanceError = Vector3d.Distance(oldPos, updateVessel.GetWorldPos3D());
            double velocityError = Vector3d.Distance(oldVelocity, updateVessel.orbitDriver.orbit.GetVel());
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
            UpdateProtovessel(updateVessel);
            posistionStatistics.LogError(updateVessel.id, distanceError, velocityError, rotationalError, planetTime);
        }

        private void UpdateProtovessel(Vessel updateVessel)
        {
            updateVessel.protoVessel.latitude = updateVessel.latitude;
            updateVessel.protoVessel.longitude = updateVessel.longitude;
            updateVessel.protoVessel.altitude = updateVessel.altitude;
            updateVessel.protoVessel.rotation = updateVessel.srfRelRotation;
            updateVessel.protoVessel.height = updateVessel.heightFromTerrain;
            updateVessel.protoVessel.normal = updateVessel.terrainNormal;
            updateVessel.protoVessel.rotation = updateVessel.srfRelRotation;
            updateVessel.protoVessel.orbitSnapShot.argOfPeriapsis = updateVessel.orbit.argumentOfPeriapsis;
            updateVessel.protoVessel.orbitSnapShot.eccentricity = updateVessel.orbit.eccentricity;
            updateVessel.protoVessel.orbitSnapShot.epoch = updateVessel.orbit.epoch;
            updateVessel.protoVessel.orbitSnapShot.inclination = updateVessel.orbit.inclination;
            updateVessel.protoVessel.orbitSnapShot.LAN = updateVessel.orbit.LAN;
            updateVessel.protoVessel.orbitSnapShot.meanAnomalyAtEpoch = updateVessel.orbit.meanAnomalyAtEpoch;
            updateVessel.protoVessel.orbitSnapShot.semiMajorAxis = updateVessel.orbit.semiMajorAxis;
            updateVessel.protoVessel.orbitSnapShot.ReferenceBodyIndex = updateVessel.orbit.referenceBody.flightGlobalsIndex;
            updateVessel.protoVessel.CoM = updateVessel.localCoM;

        }

        private static void Extrapolate(Vector3d pos, Vector3d vel, Vector3d acc, double timeDiff, out Vector3d newPostion, out Vector3d newVelocity)
        {
            vel = vel + acc * timeDiff;
            pos = pos + 0.5 * vel * timeDiff;
            newPostion = pos;
            newVelocity = vel;
        }

        //Supports scaling past 1x, unlike Quaternion.Lerp
        public static Quaternion RotationLerp(Quaternion from, Quaternion to, float scaling)
        {
            Quaternion rotationDelta = to * Quaternion.Inverse(from);
            Vector3 rotAxis;
            float rotAngle;
            rotationDelta.ToAngleAxis(out rotAngle, out rotAxis);
            if (rotAngle > 180)
            {
                rotAngle -= 360;
            }
            rotAngle = (rotAngle * scaling) % 360;
            return Quaternion.AngleAxis(rotAngle, rotAxis) * from;
        }
    }
}