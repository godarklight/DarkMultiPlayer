using System;
using System.Collections.Generic;
namespace DarkMultiPlayer
{
    public class PosistionStatistics
    {
        public bool active = false;
        public Guid selectedVessel = Guid.Empty;
        public double distanceError = 0;
        public double velocityError = 0;
        public double rotationError = 0;
        public double updateHz = 0;
        private DMPPosError[] errors = new DMPPosError[10];
        private int errorCount = 0;
        private int currentPos = 0;

        public void LogError(Guid updateID, double distanceError, double velocityError, double rotationError, double planetTime)
        {
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && FlightGlobals.fetch != null && FlightGlobals.fetch.activeVessel != null && FlightGlobals.fetch.VesselTarget != null && FlightGlobals.fetch.VesselTarget.GetVessel() != null)
            {
                if (FlightGlobals.fetch.VesselTarget.GetVessel().id == updateID && selectedVessel != updateID)
                {
                    errorCount = 0;
                    currentPos = 0;
                    distanceError = 0;
                    velocityError = 0;
                    rotationError = 0;
                    selectedVessel = updateID;
                }
                DMPPosError newError = new DMPPosError(distanceError, velocityError, rotationError, planetTime);
                AddError(newError);
            }
        }

        private void AddError(DMPPosError error)
        {
            if (errorCount < errors.Length)
            {
                errorCount++;
            }
            errors[currentPos] = error;
            if (currentPos > 1)
            {
                updateHz = Math.Round(1 / (errors[currentPos].planetTime - errors[currentPos - 1].planetTime), 1);
            }
            distanceError = 0;
            velocityError = 0;
            rotationError = 0;
            for (int avgPos = 0; avgPos < errorCount; avgPos++)
            {
                distanceError += errors[avgPos].distance / (double)errorCount;
                velocityError += errors[avgPos].velocity / (double)errorCount;
                rotationError += errors[avgPos].rotation / (double)errorCount;
            }
            currentPos++;
            if (currentPos == errors.Length)
            {
                currentPos = 0;
            }
        }

        private struct DMPPosError
        {
            public double distance;
            public double velocity;
            public double rotation;
            public double planetTime;
            public DMPPosError(double distance, double velocity, double rotation, double planetTime)
            {
                this.distance = distance;
                this.velocity = velocity;
                this.rotation = rotation;
                this.planetTime = planetTime;
            }
        }
    }
}
