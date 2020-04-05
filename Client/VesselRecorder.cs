using System;
using System.Collections.Generic;
using System.IO;
using MessageStream2;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;

namespace DarkMultiPlayer
{
    public class VesselRecorder
    {
        public bool active;
        public bool playback;
        private Guid playbackID;
        ScreenMessage screenMessage;
        private MemoryStream recording;
        private MemoryStream recordingVector;
        private WarpWorker warpWorker;
        private Queue<VesselUpdate> playbackQueue;
        private VesselUpdate lastUpdate;
        private string recordPath = Path.Combine(KSPUtil.ApplicationRootPath, "DMPRecording.bin");
        private string recordVectorPath = Path.Combine(KSPUtil.ApplicationRootPath, "DMPRecording-vector.bin");
        private Action<ByteArray, Connection<ClientObject>> HandleProtoUpdate, HandleVesselRemove, HandleVesselUpdate;
        private VesselWorker vesselWorker;
        private NetworkWorker networkWorker;
        private Settings dmpSettings;
        private DMPGame dmpGame;
        private double firstTime;
        private double lastTime;
        private NamedAction updateAction;

        public VesselRecorder(DMPGame dmpGame, WarpWorker warpWorker, VesselWorker vesselWorker, NetworkWorker networkWorker, Settings dmpSettings)
        {
            this.warpWorker = warpWorker;
            this.vesselWorker = vesselWorker;
            this.networkWorker = networkWorker;
            this.dmpSettings = dmpSettings;
            this.dmpGame = dmpGame;
            updateAction = new NamedAction(Update);
            dmpGame.updateEvent.Add(updateAction);
        }

        public void SetHandlers(Action<ByteArray, Connection<ClientObject>> HandleProtoUpdate, Action<ByteArray, Connection<ClientObject>> HandleVesselUpdate, Action<ByteArray, Connection<ClientObject>> HandleVesselRemove)
        {
            this.HandleProtoUpdate = HandleProtoUpdate;
            this.HandleVesselUpdate = HandleVesselUpdate;
            this.HandleVesselRemove = HandleVesselRemove;
        }

        public void RecordSend(ByteArray data, ClientMessageType messageType, Guid vesselID)
        {
            if (!active)
            {
                return;
            }
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.fetch != null && FlightGlobals.fetch.activeVessel != null && FlightGlobals.fetch.activeVessel.id == vesselID)
            {
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)messageType);
                    mw.Write<int>(data.Length);
                    byte[] headerData = mw.GetMessageBytes();
                    recording.Write(headerData, 0, headerData.Length);
                }
                recording.Write(data.data, 0, data.Length);
            }
        }

        public void StartRecord()
        {
            active = true;
            recording = new MemoryStream();
            recordingVector = new MemoryStream();
            VesselUpdate update = Recycler<VesselUpdate>.GetObject();
            update.SetVesselWorker(vesselWorker);
            update.CopyFromVessel(FlightGlobals.fetch.activeVessel);
            networkWorker.SendVesselUpdate(update);
            Recycler<VesselUpdate>.ReleaseObject(update);
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

            if (File.Exists(recordVectorPath))
            {
                File.Delete(recordVectorPath);
            }
            using (FileStream fs = new FileStream(recordVectorPath, FileMode.Create, FileAccess.Write))
            {
                byte[] recordingData = recordingVector.ToArray();
                fs.Write(recordingData, 0, recordingData.Length);
            }
            recordingVector.Dispose();
            recordingVector = null;
        }

        public void CancelRecord()
        {
            active = false;
            recording.Dispose();
            recording = null;
            recordingVector.Dispose();
            recordingVector = null;
        }

        byte[] headerBytesInt = new byte[4];
        public void StartPlayback()
        {
            int messagesLoaded = 0;
            bool firstMessage = true;
            ByteArray headerBytes = ByteRecycler.GetObject(8);
            using (FileStream fs = new FileStream(recordPath, FileMode.Open))
            {
                while (fs.Position < fs.Length)
                {
                    messagesLoaded++;
                    fs.Read(headerBytes.data, 0, 8);
                    using (MessageReader mr = new MessageReader(headerBytes.data))
                    {
                        ClientMessageType messageType = (ClientMessageType)mr.Read<int>();
                        int length = mr.Read<int>();
                        ByteArray dataBytes = ByteRecycler.GetObject(length);
                        fs.Read(dataBytes.data, 0, length);
                        using (MessageReader timeReader = new MessageReader(dataBytes.data))
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
                        using (MessageReader mrignore = new MessageReader(dataBytes.data))
                        {
                            //Planet time, don't care here
                            mrignore.Read<double>();
                            string vesselID = mrignore.Read<string>();
                            vesselWorker.IgnoreVessel(new Guid(vesselID));
                        }
                        switch (messageType)
                        {
                            case ClientMessageType.VESSEL_PROTO:
                                HandleProtoUpdate(dataBytes, null);
                                break;
                            case ClientMessageType.VESSEL_UPDATE:
                                HandleVesselUpdate(dataBytes, null);
                                break;
                            case ClientMessageType.VESSEL_REMOVE:
                                HandleVesselRemove(dataBytes, null);
                                break;
                            default:
                                break;
                        }
                        ByteRecycler.ReleaseObject(dataBytes);
                    }
                }
                ByteRecycler.ReleaseObject(headerBytes);
            }

            playbackQueue = new Queue<VesselUpdate>();
            using (FileStream fs = new FileStream(recordVectorPath, FileMode.Open))
            {
                while (fs.Position < fs.Length)
                {
                    fs.Read(headerBytesInt, 0, 4);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(headerBytesInt);
                    }
                    int updateLength = BitConverter.ToInt32(headerBytesInt, 0);
                    ByteArray updateBytes = ByteRecycler.GetObject(updateLength);
                    fs.Read(updateBytes.data, 0, updateLength);
                    VesselUpdate vu = networkWorker.VeselUpdateFromBytes(updateBytes.data);
                    playbackQueue.Enqueue(vu);
                    ByteRecycler.ReleaseObject(updateBytes);
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
                DisplayUpdateVesselOffset();
                if (Planetarium.GetUniversalTime() > (lastTime))
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

            if (active)
            {
                VesselUpdate vu = Recycler<VesselUpdate>.GetObject();
                vu.SetVesselWorker(vesselWorker);
                vu.CopyFromVessel(FlightGlobals.fetch.activeVessel);
                NetworkMessage updateBytes = networkWorker.GetVesselUpdateMessage(vu);
                byte[] lengthBytes = BitConverter.GetBytes(updateBytes.data.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthBytes);
                }
                recordingVector.Write(lengthBytes, 0, lengthBytes.Length);
                recordingVector.Write(updateBytes.data.data, 0, updateBytes.data.Length);
                ByteRecycler.ReleaseObject(updateBytes.data);
                Recycler<VesselUpdate>.ReleaseObject(vu);
            }
        }

        public void DisplayUpdateVesselOffset()
        {
            double interpolatorDelay = 0;
            if (dmpSettings.interpolatorType == InterpolatorType.INTERPOLATE1S)
            {
                interpolatorDelay = 1;
            }
            if (dmpSettings.interpolatorType == InterpolatorType.INTERPOLATE3S)
            {
                interpolatorDelay = 3;
            }

            while (playbackQueue.Count > 0 && Planetarium.GetUniversalTime() > (playbackQueue.Peek().planetTime + interpolatorDelay))
            {
                if (lastUpdate != null)
                {
                    Recycler<VesselUpdate>.ReleaseObject(lastUpdate);
                }
                lastUpdate = playbackQueue.Dequeue();
                playbackID = lastUpdate.vesselID;
            }
            if (playbackQueue.Count > 0)
            {
                VesselUpdate vu = playbackQueue.Peek();
                if (lastUpdate != null && vu != null && lastUpdate.isSurfaceUpdate && vu.isSurfaceUpdate)
                {
                    Vessel av = FlightGlobals.fetch.vessels.Find(v => v.id == vu.vesselID);
                    if (av != null)
                    {
                        double scaling = (Planetarium.GetUniversalTime() - interpolatorDelay - lastUpdate.planetTime) / (vu.planetTime - lastUpdate.planetTime);
                        Vector3d orgPos = new Vector3d(lastUpdate.position[0], lastUpdate.position[1], lastUpdate.position[2]);
                        Vector3d nextPos = new Vector3d(vu.position[0], vu.position[1], vu.position[2]);
                        Vector3d updatePos = Vector3d.Lerp(orgPos, nextPos, scaling);
                        Vector3d distanceInPos = av.mainBody.GetWorldSurfacePosition(av.latitude, av.longitude, av.altitude) - av.mainBody.GetWorldSurfacePosition(updatePos.x, updatePos.y, updatePos.z);
                        double timeDiff = Planetarium.GetUniversalTime() - interpolatorDelay - lastUpdate.planetTime;
                        DarkLog.Debug("Difference in position: " + Math.Round(distanceInPos.magnitude, 3) + ", scaling: " + Math.Round(scaling, 3));
                    }
                }
            }
            else
            {
                //Free the last update too!
                if (lastUpdate != null)
                {
                    Recycler<VesselUpdate>.ReleaseObject(lastUpdate);
                    lastUpdate = null;
                }
            }
        }

        public void Stop()
        {
            dmpGame.updateEvent.Remove(updateAction);
        }
    }
}
