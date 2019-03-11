using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class VesselUtil
    {
        public static DMPRaycastPair RaycastGround(double latitude, double longitude, CelestialBody body)
        {
            //We can only find the ground on bodies that actually *have* ground and if we are in flight near the origin
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch.activeVessel == null || body.pqsController == null)
            {
                return new DMPRaycastPair(-1f, Vector3.up);
            }
            //Math functions take radians.
            double latRadians = latitude * Mathf.Deg2Rad;
            double longRadians = longitude * Mathf.Deg2Rad;
            //Radial vector
            Vector3d surfaceRadial = new Vector3d(Math.Cos(latRadians) * Math.Cos(longRadians), Math.Sin(latRadians), Math.Cos(latRadians) * Math.Sin(longRadians));
            double surfaceHeight = body.pqsController.GetSurfaceHeight(surfaceRadial) - body.pqsController.radius;
            Vector3d origin = body.GetWorldSurfacePosition(latitude, longitude, surfaceHeight + 500);
            //Only return the surface if it's really close.
            double highestHit = double.NegativeInfinity;
            Vector3 rotatedVector = Vector3.up;
            if (Vector3d.Distance(FlightGlobals.fetch.activeVessel.GetWorldPos3D(), origin) < 2500)
            {
                //Down vector
                Vector3d downVector = -body.GetSurfaceNVector(latitude, longitude);
                //Magic numbers, comes from Vessel.GetHeightFromTerrain
                LayerMask groundMask = 32768;
                RaycastHit[] raycastHits = Physics.RaycastAll(origin, downVector, 1000f, groundMask);
                foreach (RaycastHit raycastHit in raycastHits)
                {
                    if (raycastHit.collider == null)
                    {
                        //I don't think this is technically possible, but unity's weird enough that we should probably check this anyway.
                        continue;
                    }
                    if (raycastHit.collider.name == body.name)
                    {
                        continue;
                    }
                    double hitAltitude = body.GetAltitude(raycastHit.point);
                    if ((hitAltitude > highestHit) && (!body.ocean || hitAltitude > 0))
                    {
                        highestHit = hitAltitude;
                        rotatedVector = Quaternion.Inverse(body.rotation) * raycastHit.normal;
                    }
                }
            }
            if (double.IsNegativeInfinity(highestHit))
            {
                return new DMPRaycastPair(-1f, Vector3.up);
            }
            else
            {
                return new DMPRaycastPair(highestHit, rotatedVector);
            }
        }

        public struct DMPRaycastPair
        {
            public readonly double altitude;
            public readonly Vector3 terrainNormal;
            public DMPRaycastPair(double altitude, Vector3 terrainNormal)
            {
                this.altitude = altitude;
                this.terrainNormal = terrainNormal;
            }
        }
    }
}

