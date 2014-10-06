using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DarkMultiPlayer
{
    //The lamest profiler in the world!
    public class Profiler
    {
        public static Stopwatch DMPReferenceTime = new Stopwatch();
        public static ProfilerData fixedUpdateData = new ProfilerData();
        public static ProfilerData updateData = new ProfilerData();
        public static ProfilerData guiData = new ProfilerData();
        public long FPS;
    }

    public class ProfilerData
    {
        //Tick time is how long the method takes to run.
        public long tickMinTime = long.MaxValue;
        public long tickMaxTime = long.MinValue;
        public long tickTime;
        List<long> tickHistory = new List<long>();
        public long tickAverage;
        //Delta time is how long it takes inbetween the method runs.
        public long deltaMinTime = long.MaxValue;
        public long deltaMaxTime = long.MinValue;
        public long lastDeltaTime;
        public long deltaTime;
        List<long> deltaHistory = new List<long>();
        public long deltaAverage;

        public void ReportTime(long startClock)
        {
            long currentClock = Profiler.DMPReferenceTime.ElapsedTicks;
            tickTime = currentClock - startClock;
            deltaTime = startClock - lastDeltaTime;
            lastDeltaTime = currentClock;
            if (tickTime < tickMinTime)
            {
                tickMinTime = tickTime;
            }
            if (tickTime > tickMaxTime)
            {
                tickMaxTime = tickTime;
            }
            //Ignore the first delta as it will be incorrect on reset.
            if (deltaHistory.Count != 0)
            {
                if (deltaTime < deltaMinTime)
                {
                    deltaMinTime = deltaTime;
                }
                if (deltaTime > deltaMaxTime)
                {
                    deltaMaxTime = deltaTime;
                }
            }
            tickHistory.Add(tickTime);
            if (tickHistory.Count > 300)
            {
                tickHistory.RemoveAt(0);
            }
            tickAverage = 0;
            foreach (long entry in tickHistory)
            {
                tickAverage += entry;
            }
            tickAverage /= tickHistory.Count;
            deltaHistory.Add(deltaTime);
            if (deltaHistory.Count > 300)
            {
                deltaHistory.RemoveAt(0);
            }
            deltaAverage = 0;
            foreach (long entry in deltaHistory)
            {
                deltaAverage += entry;
            }
            deltaAverage /= deltaHistory.Count;
        }

        public override string ToString()
        {
            double tickMS = Math.Round(tickTime / (double)(Stopwatch.Frequency / 1000), 3);
            double tickMinMS = Math.Round(tickMinTime / (double)(Stopwatch.Frequency / 1000), 3);
            double tickMaxMS = Math.Round(tickMaxTime / (double)(Stopwatch.Frequency / 1000), 3);
            double tickAverageMS = Math.Round(tickAverage / (double)(Stopwatch.Frequency / 1000), 3);
            double deltaMS = Math.Round(deltaTime / (double)(Stopwatch.Frequency / 1000), 3);
            double deltaMinMS = Math.Round(deltaMinTime / (double)(Stopwatch.Frequency / 1000), 3);
            double deltaMaxMS = Math.Round(deltaMaxTime / (double)(Stopwatch.Frequency / 1000), 3);
            double deltaAverageMS = Math.Round(deltaAverage / (double)(Stopwatch.Frequency / 1000), 3);
            string returnString = "tick: " + tickMS + " (min/max/avg) " + tickMinMS + "/" + tickMaxMS + "/" + tickAverageMS + "\n";
            returnString += "delta: " + deltaMS + " (min/max/avg) " + deltaMinMS + "/" + deltaMaxMS + "/" + deltaAverageMS + "\n";
            return returnString;
        }
    }
}

