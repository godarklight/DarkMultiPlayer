using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream;

namespace DarkMultiPlayerServer
{
    public class NukeKSC
    {
        public static void RunNukeKSC(string commandText)
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
                    DarkLog.Normal("Removing vessel: " + vesselID);
                    //Delete it from the universe
                    File.Delete(vesselFile);
                    //Send a vessel remove message
                    ServerMessage newMessage = new ServerMessage();
                    newMessage.type = ServerMessageType.VESSEL_REMOVE;
                    using (MessageWriter mw = new MessageWriter())
                    {
                        //Send it with a delete time of 0 so it shows up for all players.
                        mw.Write<int>(0);
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
}
