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
        public string players;

        [DataMember]
        public int player_count;

        [DataMember]
        public int max_players;

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
            players = Server.players;
            max_players = settings.maxPlayers;
            game_mode = settings.gameMode.ToString();
            warp_mode = settings.warpMode.ToString();
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
