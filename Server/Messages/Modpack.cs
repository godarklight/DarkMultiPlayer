using System;
using System.Collections.Generic;
using System.IO;
using DarkMultiPlayerCommon;
using MessageStream2;

namespace DarkMultiPlayerServer.Messages
{
    public class Modpack
    {
        public static void HandleModpackMessage(ClientObject client, byte[] messageData)
        {
            if (messageData == null || messageData.Length == 0)
            {
                ConnectionEnd.SendConnectionEnd(client, "Invalid mod control message from client");
                return;
            }

            if (!client.authenticated)
            {
                ConnectionEnd.SendConnectionEnd(client, "Unauthenticated client tried to send modpack message");
            }
            
            using (MessageReader mr = new MessageReader(messageData))
            {
                ModpackDataMessageType type = (ModpackDataMessageType)mr.Read<int>();
                switch (type)
                {
                    case ModpackDataMessageType.CKAN:
                        {
                            if (!DarkMultiPlayerServer.AdminSystem.fetch.IsAdmin(client.playerName))
                            {
                                ConnectionEnd.SendConnectionEnd(client, "Kicked from the server, non admin " + client.playerName + " tried to upload modpack");
                                return;
                            }
                            if (Settings.settingsStore.modpackMode != ModpackMode.CKAN)
                            {
                                ConnectionEnd.SendConnectionEnd(client, "Please set server modpackMode to CKAN");
                                return;
                            }
                            byte[] fileBytes = mr.Read<byte[]>();
                            ModpackSystem.fetch.SaveCKANData(fileBytes);
                        }
                        break;
                    case ModpackDataMessageType.MOD_LIST:
                        {
                            if (!DarkMultiPlayerServer.AdminSystem.fetch.IsAdmin(client.playerName))
                            {
                                ConnectionEnd.SendConnectionEnd(client, "Kicked from the server, non admin " + client.playerName + " tried to upload modpack");
                                return;
                            }
                            if (Settings.settingsStore.modpackMode != ModpackMode.GAMEDATA)
                            {
                                ConnectionEnd.SendConnectionEnd(client, "Please set server modpackMode to GAMEDATA");
                                return;
                            }
                            DarkLog.Normal("Modpack uploaded from " + client.playerName);
                            string[] files = mr.Read<string[]>();
                            string[] sha = mr.Read<string[]>();
                            ModpackSystem.fetch.HandleNewGameData(files, sha, client);
                        }
                        break;
                    case ModpackDataMessageType.REQUEST_OBJECT:
                        {
                            string[] sha256sums = mr.Read<string[]>();
                            ModpackSystem.fetch.HandleSendList(client, sha256sums);
                        }
                        break;
                    case ModpackDataMessageType.RESPONSE_OBJECT:
                        {
                            if (!DarkMultiPlayerServer.AdminSystem.fetch.IsAdmin(client.playerName))
                            {
                                ConnectionEnd.SendConnectionEnd(client, "Kicked from the server, non admin " + client.playerName + " tried to upload modpack");
                                return;
                            }
                            string sha256sum = mr.Read<string>();
                            if (mr.Read<bool>())
                            {
                                byte[] fileBytes = mr.Read<byte[]>();
                                DarkLog.Debug("Received object: " + sha256sum);
                                ModpackSystem.fetch.SaveModObject(fileBytes, sha256sum);
                            }
                            else
                            {
                                DarkLog.Normal("Failed to recieve: " + sha256sum);
                            }
                        }
                        break;
                    case ModpackDataMessageType.MOD_DONE:
                        {
                            if (!DarkMultiPlayerServer.AdminSystem.fetch.IsAdmin(client.playerName))
                            {
                                ConnectionEnd.SendConnectionEnd(client, "Kicked from the server, non admin " + client.playerName + " tried to upload modpack");
                                return;
                            }
                            //Has gamedata upload
                            if (mr.Read<bool>())
                            {
                                DarkLog.Debug("Mod control file updated");
                                byte[] newModControl = mr.Read<byte[]>();
                                File.WriteAllBytes(Server.modFile, newModControl);
                            }
                            ModpackSystem.fetch.HandleModDone();
                        }
                        break;
                }
            }
        }

        public static void SendModData(ClientObject client, byte[] data)
        {
            ServerMessage sm = new ServerMessage();
            sm.type = ServerMessageType.MODPACK_DATA;
            sm.data = data;
            ClientHandler.SendToClient(client, sm, false);
        }

        public static void SendModList(ClientObject client)
        {
            ServerMessage sm = new ServerMessage();
            sm.type = ServerMessageType.MODPACK_DATA;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ModpackDataMessageType.MOD_LIST);
                Dictionary<string, string> sendData = ModpackSystem.fetch.GetModListData();
                List<string> files = new List<string>(sendData.Keys);
                List<string> sha = new List<string>(sendData.Values);
                mw.Write<string[]>(files.ToArray());
                mw.Write<string[]>(sha.ToArray());
                sm.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, sm, false);
        }

        public static void SendModDone(ClientObject client)
        {
            ServerMessage sm = new ServerMessage();
            sm.type = ServerMessageType.MODPACK_DATA;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<int>((int)ModpackDataMessageType.MOD_DONE);
                sm.data = mw.GetMessageBytes();
            }
            ClientHandler.SendToClient(client, sm, false);
        }

        public static void SendCkan(ClientObject client)
        {
            byte[] fileData = ModpackSystem.fetch.GetCKANData();
            if (fileData != null)
            {
                ServerMessage sm = new ServerMessage();
                sm.type = ServerMessageType.MODPACK_DATA;
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)ModpackDataMessageType.CKAN);
                    mw.Write<byte[]>(fileData);
                    sm.data = mw.GetMessageBytes();
                }
                ClientHandler.SendToClient(client, sm, false);
            }
            else
            {
                ConnectionEnd.SendConnectionEnd(client, "Config/DarkMultiPlayer.ckan file missing!");
            }
        }
    }
}

