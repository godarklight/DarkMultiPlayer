using System;
using System.Collections.Generic;
namespace DarkMultiPlayer
{
    public static class SafetyBubble
    {
        private static List<SafetyBubblePosition> positions = new List<SafetyBubblePosition>();
        //Adapted from KMP. Called from PlayerStatusWorker.
        public static bool isInSafetyBubble(Vector3d worldPos, CelestialBody body, double safetyBubbleDistance)
        {
            if (body == null)
            {
                return false;
            }
            foreach (SafetyBubblePosition position in positions)
            {
                if (body.name == position.celestialBody)
                {
                    Vector3d testPos = body.GetWorldSurfacePosition(position.latitude, position.longitude, position.altitude);
                    double distance = Vector3d.Distance(worldPos, testPos);
                    if (distance < safetyBubbleDistance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static void RegisterLocation(double latitude, double longitude, double altitude, string celestialBody)
        {
            SafetyBubblePosition sbp = new SafetyBubblePosition(latitude, longitude, altitude, celestialBody);
            if (!positions.Contains(sbp))
            {
                positions.Add(sbp);
            }
        }

        internal static void RegisterDefaultLocations()
        {
            //Stock:
            RegisterLocation(-0.0971978130377757, 285.44237039111, 60, "Kerbin"); //Launch Pad
            RegisterLocation(-0.0486001121594686, 285.275552559723, 60, "Kerbin"); //Runway
        }

        private class SafetyBubblePosition
        {
            public readonly double latitude;
            public readonly double longitude;
            public readonly double altitude;
            public readonly string celestialBody;

            public SafetyBubblePosition(double latitude, double longitude, double altitude, string celestialBody)
            {
                this.latitude = latitude;
                this.longitude = longitude;
                this.altitude = altitude;
                this.celestialBody = celestialBody;
            }

            public override bool Equals(object obj)
            {
                if (obj == null)
                {
                    return false;
                }
                SafetyBubblePosition other = obj as SafetyBubblePosition;
                return this.latitude == other.latitude && this.longitude == other.longitude && this.altitude == other.altitude && this.celestialBody == other.celestialBody;
            }

            public override int GetHashCode()
            {
                return (int)(this.latitude * 10000 + this.longitude * 100 + this.altitude);
            }
        }


    }
}
