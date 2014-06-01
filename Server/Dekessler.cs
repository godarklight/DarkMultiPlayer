using System;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream;

namespace DarkMultiPlayerServer
{
    public class Dekessler
    {
        public static void RunDekessler(string commandText)
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
                        if (trimmedLine.StartsWith("type = "))
                        {
                            string vesselType = trimmedLine.Substring(trimmedLine.IndexOf("=") + 2);
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
            DarkLog.Normal("Removed " + numberOfRemovals + " debris");
        }
    }
}

