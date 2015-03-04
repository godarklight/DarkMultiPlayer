using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer
{
    public class NukeKSC
    {
        private static long lastNukeTime = 0;

        public static void RunNukeKSC(string commandText)
        {
            lock (Server.universeSizeLock)
            {
                string[] vesselList = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels"));
                int numberOfRemovals = 0;
                foreach (string vesselFile in vesselList)
                {
                    string vesselID = Path.GetFileNameWithoutExtension(vesselFile);
                    bool landedAtKSC = false;
                    bool landedAtRunway = false;
                    using (StreamReader sr = new StreamReader(vesselFile))
                    {
                        string currentLine = sr.ReadLine();
                        while (currentLine != null && !landedAtKSC && !landedAtRunway)
                        {
                            string trimmedLine = currentLine.Trim();
                            if (trimmedLine.StartsWith("landedAt = "))
                            {
                                string landedAt = trimmedLine.Substring(trimmedLine.IndexOf("=") + 2);
                                if (landedAt == "KSC")
                                {
                                    landedAtKSC = true;
                                }
                                if (landedAt == "Runway")
                                {
                                    landedAtRunway = true;
                                }
                            }
                            currentLine = sr.ReadLine();
                        }
                    }
                    if (landedAtKSC | landedAtRunway)
                    {
                        DarkLog.Normal("Removing vessel " + vesselID + " from KSC");
                        //Delete it from the universe
                        if (File.Exists(vesselFile))
                        {
                            File.Delete(vesselFile);
                        }
                        //Send a vessel remove message
                        ServerMessage newMessage = new ServerMessage();
                        newMessage.type = ServerMessageType.VESSEL_REMOVE;
                        using (MessageWriter mw = new MessageWriter())
                        {
                            //Send it with a delete time of 0 so it shows up for all players.
                            mw.Write<double>(0);
                            mw.Write<string>(vesselID);
                            mw.Write<bool>(false);
                            newMessage.data = mw.GetMessageBytes();
                        }
                        ClientHandler.SendToAll(null, newMessage, false);
                        numberOfRemovals++;
                    }
                }
                DarkLog.Normal("Nuked " + numberOfRemovals + " vessels around the KSC");
            }
        }

        public static void CheckTimer()
        {
            //0 or less is disabled.
            if (Settings.settingsStore.autoNuke > 0)
            {
                //Run it on server start or if the nuke time has elapsed.
                if (((Server.serverClock.ElapsedMilliseconds - lastNukeTime) > (Settings.settingsStore.autoNuke * 60 * 1000)) || lastNukeTime == 0)
                {
                    lastNukeTime = Server.serverClock.ElapsedMilliseconds;
                    RunNukeKSC("");
                }
            }
        }
    }
}
