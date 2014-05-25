using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Net;
using DarkMultiPlayerServer;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    [DataContract]
    class ServerInfo
    {
        [DataMember]
        public string server_name;

        [DataMember]
        public int player_count;

        [DataMember]
        public int max_players; // not yet implemented

        [DataMember]
        public int port;

        [DataMember]
        public string game_mode;

        [DataMember]
        public string warp_mode;

        [DataMember]
        public bool mod_control;

        [DataMember]
        public bool cheats;

        public ServerInfo(SettingsStore settings)
        {
            server_name = settings.serverName;
            player_count = Server.playerCount;
            max_players = settings.maxPlayers;

            switch (settings.gameMode)
            {
                default:
                case GameMode.SANDBOX:
                    game_mode = "SANDBOX";
                    break;
                case GameMode.CAREER:
                    game_mode = "CAREER";
                    break;
            }

            switch (settings.warpMode)
            {
                default:
                    warp_mode = "UNDEFINED";
                    break;
                case WarpMode.MCW_FORCE:
                    warp_mode = "MCW_FORCE";
                    break;
                case WarpMode.MCW_VOTE:
                    warp_mode = "MCW_VOTE";
                    break;
                case WarpMode.MCW_LOWEST:
                    warp_mode = "MCW_LOWEST";
                    break;
                case WarpMode.SUBSPACE_SIMPLE:
                    warp_mode = "SUBSPACE_SIMPLE";
                    break;
                case WarpMode.SUBSPACE:
                    warp_mode = "SUBSPACE";
                    break;
                case WarpMode.NONE:
                    warp_mode = "NONE";
                    break;
            }

            port = settings.port;

            mod_control = settings.modControl;

            cheats = settings.cheats;
            
        }

        public string GetJSON()
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ServerInfo));

            MemoryStream outStream = new MemoryStream();
            serializer.WriteObject(outStream, this);

            outStream.Position = 0;

            StreamReader sr = new StreamReader(outStream);

            return sr.ReadToEnd();
        }
        
    }
}
