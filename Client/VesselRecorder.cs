using System;
using System.IO;
using MessageStream2;
using DarkMultiPlayerCommon;
namespace DarkMultiPlayer
{
    public class VesselRecorder
    {
        public bool active = false;
        public bool playback = false;
        ScreenMessage screenMessage = null;
        private MemoryStream recording;
        private WarpWorker warpWorker;
        private string recordPath = Path.Combine(KSPUtil.ApplicationRootPath, "DMPRecording.bin");
        private Action<byte[]> HandleProtoUpdate, HandleVesselUpdate, HandleVesselRemove;
        private VesselWorker vesselWorker;
        private DMPGame dmpGame;
        private double firstTime = 0;
        private double lastTime = 0;

        public VesselRecorder(DMPGame dmpGame, WarpWorker warpWorker, VesselWorker vesselWorker)
        {
            this.warpWorker = warpWorker;
            this.vesselWorker = vesselWorker;
            this.dmpGame = dmpGame;
            this.dmpGame.updateEvent.Add(Update);
        }

        public void SetHandlers(Action<byte[]> HandleProtoUpdate, Action<byte[]> HandleVesselUpdate, Action<byte[]> HandleVesselRemove)
        {
            this.HandleProtoUpdate = HandleProtoUpdate;
            this.HandleVesselUpdate = HandleVesselUpdate;
            this.HandleVesselRemove = HandleVesselRemove;
        }

        public void RecordSend(byte[] data, ClientMessageType messageType)
        {
            if (!active)
            {
                return;
            }
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)messageType);
                mw.Write<int>(data.Length);
                byte[] headerData = mw.GetMessageBytes();
                recording.Write(headerData, 0, headerData.Length);
            }
            recording.Write(data, 0, data.Length);

        }

        public void StartRecord()
        {
            active = true;
            recording = new MemoryStream();
        }

        public void StopRecord()
        {
            active = false;
            if (File.Exists(recordPath))
            {
                File.Delete(recordPath);
            }
            using (FileStream fs = new FileStream(recordPath, FileMode.Create, FileAccess.Write))
            {
                byte[] recordingData = recording.ToArray();
                fs.Write(recordingData, 0, recordingData.Length);
            }
            recording.Dispose();
            recording = null;
        }

        public void CancelRecord()
        {
            active = false;
            recording.Dispose();
            recording = null;
        }

        public void StartPlayback()
        {
            int messagesLoaded = 0;
            bool firstMessage = true;
            using (FileStream fs = new FileStream(recordPath, FileMode.Open))
            {
                while (fs.Position < fs.Length)
                {
                    messagesLoaded++;
                    byte[] headerBytes = new byte[8];
                    fs.Read(headerBytes, 0, 8);
                    using (MessageReader mr = new MessageReader(headerBytes))
                    {
                        ClientMessageType messageType = (ClientMessageType)mr.Read<int>();
                        int length = mr.Read<int>();
                        byte[] dataBytes = new byte[length];
                        fs.Read(dataBytes, 0, length);
                        using (MessageReader timeReader = new MessageReader(dataBytes))
                        {
                            //Planet time is the first part of the message for the three types we care about here
                            double planetTime = timeReader.Read<double>();
                            lastTime = planetTime;
                            if (firstMessage)
                            {
                                firstTime = planetTime;
                                firstMessage = false;
                                Planetarium.SetUniversalTime(planetTime - 5d);
                                warpWorker.SendNewSubspace();
                            }
                        }
                        using (MessageReader mrignore = new MessageReader(dataBytes))
                        {
                            //Planet time, don't care here
                            mrignore.Read<double>();
                            string vesselID = mrignore.Read<string>();
                            vesselWorker.IgnoreVessel(new Guid(vesselID));
                        }
                        switch (messageType)
                        {
                            case ClientMessageType.VESSEL_PROTO:
                                HandleProtoUpdate(dataBytes);
                                break;
                            case ClientMessageType.VESSEL_UPDATE:
                                HandleVesselUpdate(dataBytes);
                                break;
                            case ClientMessageType.VESSEL_REMOVE:
                                HandleVesselRemove(dataBytes);
                                break;
                        }
                    }
                }
            }

            ScreenMessages.PostScreenMessage("Loaded " + messagesLoaded + " saved updates.", 5f, ScreenMessageStyle.UPPER_CENTER);
            screenMessage = ScreenMessages.PostScreenMessage("Playback 0 / " + (int)(lastTime - firstTime) + " seconds.", float.MaxValue, ScreenMessageStyle.UPPER_CENTER);
            playback = true;
        }

        public void Update()
        {
            if (playback)
            {
                if (Planetarium.GetUniversalTime() > lastTime)
                {
                    playback = false;
                    ScreenMessages.RemoveMessage(screenMessage);
                    screenMessage = null;
                }
                else
                {
                    int timeLeft = (int)(lastTime - Planetarium.GetUniversalTime());
                    ScreenMessages.RemoveMessage(screenMessage);
                    screenMessage = ScreenMessages.PostScreenMessage("Playback time left: " + timeLeft + " / " + (int)(lastTime - firstTime) + " seconds", float.MaxValue, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }

        public void Stop()
        {
            dmpGame.updateEvent.Remove(Update);
        }
    }
}
