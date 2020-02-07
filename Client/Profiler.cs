using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DarkMultiPlayer
{
    //The lamest profiler in the world!
    public class Profiler
    {
        public static Stopwatch DMPReferenceTime;
        public bool samplingEnabled = false;
        private bool lastSamplingEnabled = false;
        private Dictionary<string, ProfilerData> profilerData = new Dictionary<string, ProfilerData>();

        public Profiler()
        {
            DMPReferenceTime = new Stopwatch();
            DMPReferenceTime.Start();
        }

        public long GetCurrentTime
        {
            get
            {
                return DMPReferenceTime.ElapsedTicks;
            }
        }

        public long GetCurrentMemory
        {
            get
            {
                return GC.GetTotalMemory(false);
            }
        }

        public void Report(string name, long startTime, long startMemory)
        {
            if (!samplingEnabled)
            {
                return;
            }
            lock (profilerData)
            {
                long elapsedTime = GetCurrentTime - startTime;
                if (!profilerData.ContainsKey(name))
                {
                    profilerData.Add(name, new ProfilerData());
                }
                ProfilerData data = profilerData[name];
                data.time.Add(elapsedTime);
                long memoryDelta = GetCurrentMemory - startMemory;
                data.memory.Add(memoryDelta);
            }
        }

        public void Update()
        {
            //One shot when we turn sampling off.
            if (lastSamplingEnabled != samplingEnabled)
            {
                lastSamplingEnabled = samplingEnabled;
                if (!samplingEnabled)
                {
                    string profilerPath = Path.Combine(KSPUtil.ApplicationRootPath, "DarkMultiPlayer-Profiler");
                    Directory.CreateDirectory(profilerPath);
                    foreach (string histogramFiles in Directory.GetFiles(profilerPath, "*"))
                    {
                        File.Delete(histogramFiles);
                    }
                    foreach (KeyValuePair<string, ProfilerData> data in profilerData)
                    {
                        using (StreamWriter sr = new StreamWriter(Path.Combine(profilerPath, data.Key + ".time.txt")))
                        {
                            foreach (long dataLine in data.Value.time)
                            {
                                sr.WriteLine(dataLine);
                            }
                        }
                    }
                    using (StreamWriter srtotal = new StreamWriter(Path.Combine(profilerPath, "totals-memory.txt")))
                    {
                        long absoluteTotalMemory = 0;
                        long absoluteTotalSamples = 0;
                        foreach (KeyValuePair<string, ProfilerData> data in profilerData)
                        {
                            long totalMemory = 0;
                            using (StreamWriter sr = new StreamWriter(Path.Combine(profilerPath, data.Key + ".memory.txt")))
                            {
                                foreach (long dataLine in data.Value.memory)
                                {
                                    absoluteTotalSamples++;
                                    totalMemory += dataLine;
                                    sr.WriteLine(dataLine);
                                }
                            }
                            absoluteTotalMemory += totalMemory;
                            srtotal.WriteLine(data.Key + ": " + totalMemory + ", samples: " + data.Value.memory.Count);
                        }
                        srtotal.WriteLine("TOTAL: " + absoluteTotalMemory + ", samples: " + absoluteTotalSamples);
                    }
                    profilerData = new Dictionary<string, ProfilerData>();
                    DarkLog.Debug("Profiling Finished");
                }
                else
                {
                    DarkLog.Debug("Profiling Started");
                }
            }
        }

        private class ProfilerData
        {
            public List<long> time = new List<long>();
            public List<long> memory = new List<long>();
        }
    }
}

