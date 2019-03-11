using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer
{
    public class Dekessler
    {
        public static long lastDekesslerTime = 0;

        public static void RunDekessler(string commandText)
        {
            lock (Server.universeSizeLock)
            {
                string[] vesselList = Directory.GetFiles(Path.Combine(Server.universeDirectory, "Vessels"));
                int numberOfRemovals = 0;
                foreach (string vesselFile in vesselList)
                {
                    string vesselID = Path.GetFileNameWithoutExtension(vesselFile);
                    bool vesselIsDebris = false;
                    using (StreamReader sr = new StreamReader(vesselFile))
                    {
                        string currentLine = sr.ReadLine();
                        while (currentLine != null && !vesselIsDebris)
                        {
                            string trimmedLine = currentLine.Trim();
                            if (trimmedLine.StartsWith("type = ", StringComparison.Ordinal))
                            {
                                string vesselType = trimmedLine.Substring(trimmedLine.IndexOf("=", StringComparison.Ordinal) + 2);
                                if (vesselType == "Debris")
                                {
                                    vesselIsDebris = true;
                                }
                            }
                            currentLine = sr.ReadLine();
                        }
                    }
                    if (vesselIsDebris)
                    {
                        DarkLog.Normal("Removing vessel: " + vesselID);
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
                        DarkLog.Debug("Removed debris vessel " + vesselID);
                    }
                }
                DarkLog.Normal("Removed " + numberOfRemovals + " debris");
            }
        }

        public static void CheckTimer()
        {
            //0 or less is disabled.
            if (Settings.settingsStore.autoDekessler > 0)
            {
                //Run it on server start or if the nuke time has elapsed.
                if (((Server.serverClock.ElapsedMilliseconds - lastDekesslerTime) > (Settings.settingsStore.autoDekessler * 60 * 1000)) || lastDekesslerTime == 0)
                {
                    lastDekesslerTime = Server.serverClock.ElapsedMilliseconds;
                    RunDekessler("");
                }
            }
        }
    }
}

