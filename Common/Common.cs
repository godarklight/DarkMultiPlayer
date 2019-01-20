using System;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Collections.Generic;

namespace DarkMultiPlayerCommon
{
    public class Common
    {
        //Timeouts in milliseconds
        public const long HEART_BEAT_INTERVAL = 5000;
        public const long INITIAL_CONNECTION_TIMEOUT = 5000;
        public const long CONNECTION_TIMEOUT = 20000;
        //Any message bigger than 5MB will be invalid
        public const int MAX_MESSAGE_SIZE = 5242880;
        //Split messages into 8kb chunks to higher priority messages have more injection points into the TCP stream.
        public const int SPLIT_MESSAGE_LENGTH = 8192;
        //Bump this every time there is a network change (Basically, if MessageWriter or MessageReader is touched).
        public const int PROTOCOL_VERSION = 48;
        //Program version. This is written in the build scripts.
        public const string PROGRAM_VERSION = "Custom";
        //Mod control version - The last version to add parts
        public const string MODCONTROL_VERSION = "1.6";
        //Compression threshold
        public const int COMPRESSION_THRESHOLD = 4096;

        public static string CalculateSHA256Hash(string fileName)
        {
            return CalculateSHA256Hash(File.ReadAllBytes(fileName));
        }

        public static string CalculateSHA256Hash(byte[] fileData)
        {
            StringBuilder sb = new StringBuilder();
            using (SHA256Managed sha = new SHA256Managed())
            {
                byte[] fileHashData = sha.ComputeHash(fileData);
                //Byte[] to string conversion adapted from MSDN...
                for (int i = 0; i < fileHashData.Length; i++)
                {
                    sb.Append(fileHashData[i].ToString("x2"));
                }
            }
            return sb.ToString();
        }

        public static byte[] PrependNetworkFrame(int messageType, byte[] messageData)
        {
            byte[] returnBytes;
            //Get type bytes
            byte[] typeBytes = BitConverter.GetBytes(messageType);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(typeBytes);
            }
            if (messageData == null || messageData.Length == 0)
            {
                returnBytes = new byte[8];
                typeBytes.CopyTo(returnBytes, 0);
            }
            else
            {
                //Get length bytes if we have a payload
                byte[] lengthBytes = BitConverter.GetBytes(messageData.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthBytes);
                }
                returnBytes = new byte[8 + messageData.Length];
                typeBytes.CopyTo(returnBytes, 0);
                lengthBytes.CopyTo(returnBytes, 4);
                messageData.CopyTo(returnBytes, 8);
            }
            return returnBytes;
        }

        public static string ConvertConfigStringToGUIDString(string configNodeString)
        {
            if (configNodeString == null || configNodeString.Length != 32)
            {
                return null;
            }
            string[] returnString = new string[5];
            returnString[0] = configNodeString.Substring(0, 8);
            returnString[1] = configNodeString.Substring(8, 4);
            returnString[2] = configNodeString.Substring(12, 4);
            returnString[3] = configNodeString.Substring(16, 4);
            returnString[4] = configNodeString.Substring(20);
            return String.Join("-", returnString);
        }

        public static List<string> GetStockParts()
        {
            List<string> stockPartList = new List<string>()
            {
                "StandardCtrlSrf",
                "CanardController",
                "noseCone",
                "AdvancedCanard",
                "airplaneTail",
                "deltaWing",
                "noseConeAdapter",
                "rocketNoseCone",
                "smallCtrlSrf",
                "standardNoseCone",
                "sweptWing",
                "tailfin",
                "wingConnector",
                "winglet",
                "R8winglet",
                "winglet3",
                "Mark1Cockpit",
                "Mark2Cockpit",
                "Mark1-2Pod",
                "advSasModule",
                "asasmodule1-2",
                "avionicsNoseCone",
                "crewCabin",
                "cupola",
                "landerCabinSmall",
                "mark3Cockpit",
                "mk1pod",
                "mk2LanderCabin",
                "probeCoreCube",
                "probeCoreHex",
                "probeCoreOcto",
                "probeCoreOcto2",
                "probeCoreSphere",
                "probeStackLarge",
                "probeStackSmall",
                "sasModule",
                "seatExternalCmd",
                "rtg",
                "batteryBank",
                "batteryBankLarge",
                "batteryBankMini",
                "batteryPack",
                "ksp.r.largeBatteryPack",
                "largeSolarPanel",
                "solarPanels1",
                "solarPanels2",
                "solarPanels3",
                "solarPanels4",
                "solarPanels5",
                "JetEngine",
                "engineLargeSkipper",
                "ionEngine",
                "liquidEngine",
                "liquidEngine1-2",
                "liquidEngine2",
                "liquidEngine2-2",
                "liquidEngine3",
                "liquidEngineMini",
                "microEngine",
                "nuclearEngine",
                "radialEngineMini",
                "radialLiquidEngine1-2",
                "sepMotor1",
                "smallRadialEngine",
                "solidBooster",
                "solidBooster1-1",
                "toroidalAerospike",
                "turboFanEngine",
                "MK1Fuselage",
                "Mk1FuselageStructural",
                "RCSFuelTank",
                "RCSTank1-2",
                "rcsTankMini",
                "rcsTankRadialLong",
                "fuelTank",
                "fuelTank1-2",
                "fuelTank2-2",
                "fuelTank3-2",
                "fuelTank4-2",
                "fuelTankSmall",
                "fuelTankSmallFlat",
                "miniFuelTank",
                "mk2Fuselage",
                "mk2SpacePlaneAdapter",
                "mk3Fuselage",
                "mk3spacePlaneAdapter",
                "radialRCSTank",
                "toroidalFuelTank",
                "xenonTank",
                "xenonTankRadial",
                "adapterLargeSmallBi",
                "adapterLargeSmallQuad",
                "adapterLargeSmallTri",
                "adapterSmallMiniShort",
                "adapterSmallMiniTall",
                "nacelleBody",
                "radialEngineBody",
                "smallHardpoint",
                "stationHub",
                "structuralIBeam1",
                "structuralIBeam2",
                "structuralIBeam3",
                "structuralMiniNode",
                "structuralPanel1",
                "structuralPanel2",
                "structuralPylon",
                "structuralWing",
                "strutConnector",
                "strutCube",
                "strutOcto",
                "trussAdapter",
                "trussPiece1x",
                "trussPiece3x",
                "CircularIntake",
                "landingLeg1",
                "landingLeg1-2",
                "RCSBlock",
                "stackDecoupler",
                "airScoop",
                "commDish",
                "decoupler1-2",
                "dockingPort1",
                "dockingPort2",
                "dockingPort3",
                "dockingPortLarge",
                "dockingPortLateral",
                "fuelLine",
                "ladder1",
                "largeAdapter",
                "largeAdapter2",
                "launchClamp1",
                "linearRcs",
                "longAntenna",
                "miniLandingLeg",
                "parachuteDrogue",
                "parachuteLarge",
                "parachuteRadial",
                "parachuteSingle",
                "radialDecoupler",
                "radialDecoupler1-2",
                "radialDecoupler2",
                "ramAirIntake",
                "roverBody",
                "sensorAccelerometer",
                "sensorBarometer",
                "sensorGravimeter",
                "sensorThermometer",
                "spotLight1",
                "spotLight2",
                "stackBiCoupler",
                "stackDecouplerMini",
                "stackPoint1",
                "stackQuadCoupler",
                "stackSeparator",
                "stackSeparatorBig",
                "stackSeparatorMini",
                "stackTriCoupler",
                "telescopicLadder",
                "telescopicLadderBay",
                "SmallGearBay",
                "roverWheel1",
                "roverWheel2",
                "roverWheel3",
                "wheelMed",
                "flag",
                "kerbalEVA",
                "mediumDishAntenna",
                "GooExperiment",
                "science.module",
                "RAPIER",
                "Large.Crewed.Lab",
                //0.23.5 parts
                "GrapplingDevice",
                "LaunchEscapeSystem",
                "MassiveBooster",
                "PotatoRoid",
                "Size2LFB",
                "Size3AdvancedEngine",
                "size3Decoupler",
                "Size3EngineCluster",
                "Size3LargeTank",
                "Size3MediumTank",
                "Size3SmallTank",
                "Size3to2Adapter",
                //0.24 parts
                "omsEngine",
                "vernierEngine",
                //0.25 parts
                "delta.small",
                "elevon2",
                "elevon3",
                "elevon5",
                "IntakeRadialLong",
                "MK1IntakeFuselage",
                "mk2.1m.AdapterLong",
                "mk2.1m.Bicoupler",
                "mk2CargoBayL",
                "mk2CargoBayS",
                "mk2Cockpit.Inline",
                "mk2Cockpit.Standard",
                "mk2CrewCabin",
                "mk2DockingPort",
                "mk2DroneCore",
                "mk2FuselageLongLFO",
                "mk2FuselageShortLFO",
                "mk2FuselageShortLiquid",
                "mk2FuselageShortMono",
                "shockConeIntake",
                "structuralWing2",
                "structuralWing3",
                "structuralWing4",
                "sweptWing1",
                "sweptWing2",
                "wingConnector2",
                "wingConnector3",
                "wingConnector4",
                "wingConnector5",
                "wingStrake",
                //0.90 parts
                "adapterMk3-Mk2",
                "adapterMk3-Size2",
                "adapterMk3-Size2Slant",
                "adapterSize2-Mk2",
                "adapterSize2-Size1",
                "adapterSize2-Size1Slant",
                "adapterSize3-Mk3",
                "mk3CargoBayL",
                "mk3CargoBayM",
                "mk3CargoBayS",
                "mk3Cockpit.Shuttle",
                "mk3CrewCabin",
                "mk3FuselageLF.100",
                "mk3FuselageLF.25",
                "mk3FuselageLF.50",
                "mk3FuselageLFO.100",
                "mk3FuselageLFO.25",
                "mk3FuselageLFO.50",
                "mk3FuselageMONO",
                //1.0 parts
                "kerbalEVAfemale",
                "airbrake1",
                "airlinerCtrlSrf",
                "airlinerMainWing",
                "airlinerTailFin",
                "pointyNoseConeA",
                "pointyNoseConeB",
                "airplaneTailB",
                "fairingSize1",
                "fairingSize2",
                "fairingSize3",
                "HeatShield1",
                "HeatShield2",
                "HeatShield3",
                "wingShuttleDelta",
                "elevonMk3",
                "wingShuttleElevon1",
                "wingShuttleElevon2",
                "wingShuttleRudder",
                "wingShuttleStrake",
                "delta.small",
                "mk2Cockpit.Inline",
                "mk2Cockpit.Standard",
                "mk3Cockpit.Shuttle",
                "ksp.r.largeBatteryPack",
                "solidBooster.sm",
                "fuelTank.long",
                "mk2.1m.Bicoupler",
                "mk2.1m.AdapterLong",
                "mk3FuselageLFO.100",
                "mk3FuselageLFO.25",
                "mk3FuselageLFO.50",
                "mk3FuselageLF.100",
                "mk3FuselageLF.25",
                "mk3FuselageLF.50",
                "xenonTankLarge",
                "mk3Cockpit.Shuttle",
                "FuelCell",
                "FuelCellArray",
                "ISRU",
                "LargeTank",
                "OrbitalScanner",
                "RadialDrill",
                "SmallTank",
                "SurfaceScanner",
                "SurveyScanner",
                "sensorAtmosphere",
                "Large.Crewed.Lab",
                "science.module",
                "radialDrogue",
                "ServiceBay.125",
                "ServiceBay.250",
                "GearFixed",
                "GearFree",
                "GearLarge",
                "GearMedium",
                //1.0.1 parts
                "basicFin",
                //1.0.3 parts
                "foldingRadLarge",
                "foldingRadMed",
                "foldingRadSmall",
                "radPanelLg",
                "radPanelSm",
                //1.0.5 parts
                "turboJet",
                "turboFanSize2",
                "miniJetEngine",
                "SSME",
                "adapterEngines",
                "miniFuselage",
                "miniIntake",
                "MK1CrewCabin",
                "MiniISRU",
                "MiniDrill",
                "RadialOreTank",
                "radPanelEdge",
                "mk3CargoRamp",
                //1.1.0 parts
                "InflatableHeatShield",
                "HECS2.ProbeCore",
                "HighGainAntenna",
                "LgRadialSolarPanel",
                "GearSmall",
                // 1.2 parts
                "ScienceBox",
                "SurfAntenna",
                "HighGainAntenna5",
                "RelayAntenna100",
                "RelayAntenna5",
                "RelayAntenna50",
                "HeatShield0",
                // 1.3 part
                "InfraredTelescope",
                // 1.4.0 parts
                "kerbalEVAVintage",
                "kerbalEVAfemaleVintage",
                "mk1-3pod",
                "Decoupler.0",
                "Decoupler.1",
                "Decoupler.2",
                "Decoupler.3",
                "Separator.0",
                "Separator.1",
                "Separator.2",
                "Separator.3",
                "externalTankCapsule",
                "externalTankRound",
                "externalTankToroid",
                "Rockomax16.BW",
                "Rockomax32.BW",
                "Rockomax64.BW",
                "Rockomax8BW",
                // 1.5.1 parts
                "mk1pod.v2",
                "probeCoreHex.v2",
                "probeCoreOcto2.v2",
                "probeCoreOcto.v2",
                "roverBody.v2",
                "probeCoreSphere.v2",
                "solidBooster.v2",
                "solidBooster.sm.v2",
                // 1.6 parts
                "rocketNoseCone.v2",
                "mk2LanderCabin.v2",
                "liquidEngineMini.v2",
                "liquidEngine3.v2",
                "liquidEngine2-2.v2",
                "Size3To2Adapter.v2",
                "stackBiCoupler.v2",
                "stackTriCoupler.v2"
            };
            return stockPartList;
            //MAKE SURE TO CHANGE Common.MODCONTROL_VERSION
        }

        public static string GenerateModFileStringData(string[] requiredFiles, string[] optionalFiles, bool isWhiteList, string[] whitelistBlacklistFiles, string[] partsList)
        {
            //This is the same format as KMPModControl.txt. It's a fairly sane format, and it makes sense to remain compatible.
            StringBuilder sb = new StringBuilder();
            //Header stuff
            sb.AppendLine("#MODCONTROLVERSION=" + Common.MODCONTROL_VERSION);
            sb.AppendLine("#You can comment by starting a line with a #, these are ignored by the server.");
            sb.AppendLine("#Commenting will NOT work unless the line STARTS with a '#'.");
            sb.AppendLine("#You can also indent the file with tabs or spaces.");
            sb.AppendLine("#Sections supported are required-files, optional-files, partslist, resource-blacklist and resource-whitelist.");
            sb.AppendLine("#The client will be required to have the files found in required-files, and they must match the SHA hash if specified (this is where part mod files and play-altering files should go, like KWRocketry or Ferram Aerospace Research#The client may have the files found in optional-files, but IF they do then they must match the SHA hash (this is where mods that do not affect other players should go, like EditorExtensions or part catalogue managers");
            sb.AppendLine("#You cannot use both resource-blacklist AND resource-whitelist in the same file.");
            sb.AppendLine("#resource-blacklist bans ONLY the files you specify");
            sb.AppendLine("#resource-whitelist bans ALL resources except those specified in the resource-whitelist section OR in the SHA sections. A file listed in resource-whitelist will NOT be checked for SHA hash. This is useful if you want a mod that modifies files in its own directory as you play.");
            sb.AppendLine("#Each section has its own type of formatting. Examples have been given.");
            sb.AppendLine("#Sections are defined as follows:");
            sb.AppendLine("");
            //Required section
            sb.AppendLine("!required-files");
            sb.AppendLine("#To generate the SHA256 of a file you can use a utility such as this one: http://hash.online-convert.com/sha256-generator (use the 'hex' string), or use sha256sum on linux.");
            sb.AppendLine("#File paths are read from inside GameData.");
            sb.AppendLine("#If there is no SHA256 hash listed here (i.e. blank after the equals sign or no equals sign), SHA matching will not be enforced.");
            sb.AppendLine("#You may not specify multiple SHAs for the same file. Do not put spaces around equals sign. Follow the example carefully.");
            sb.AppendLine("#Syntax:");
            sb.AppendLine("#[File Path]=[SHA] or [File Path]");
            sb.AppendLine("#Example: MechJeb2/Plugins/MechJeb2.dll=B84BB63AE740F0A25DA047E5EDA35B26F6FD5DF019696AC9D6AF8FC3E031F0B9");
            sb.AppendLine("#Example: MechJeb2/Plugins/MechJeb2.dll");
            foreach (string requiredFile in requiredFiles)
            {
                sb.AppendLine(requiredFile);
            }
            sb.AppendLine("");
            sb.AppendLine("");
            sb.AppendLine("!optional-files");
            sb.AppendLine("#Formatting for this section is the same as the 'required-files' section");
            foreach (string optionalFile in optionalFiles)
            {
                sb.AppendLine(optionalFile);
            }
            sb.AppendLine("");
            sb.AppendLine("");
            //Whitelist or blacklist section
            if (isWhiteList)
            {
                sb.AppendLine("!resource-whitelist");
                sb.AppendLine("#!resource-blacklist");
            }
            else
            {
                sb.AppendLine("!resource-blacklist");
                sb.AppendLine("#!resource-whitelist");
            }
            sb.AppendLine("#Only select one of these modes.");
            sb.AppendLine("#Resource blacklist: clients will be allowed to use any dll's, So long as they are not listed in this section");
            sb.AppendLine("#Resource whitelist: clients will only be allowed to use dll's listed here or in the 'required-files' and 'optional-files' sections.");
            sb.AppendLine("#Syntax:");
            sb.AppendLine("#[File Path]");
            sb.AppendLine("#Example: MechJeb2/Plugins/MechJeb2.dll");
            foreach (string whitelistBlacklistFile in whitelistBlacklistFiles)
            {
                sb.AppendLine(whitelistBlacklistFile);
            }
            sb.AppendLine("");
            sb.AppendLine("");
            //Parts section
            sb.AppendLine("!partslist");
            sb.AppendLine("#This is a list of parts to allow users to put on their ships.");
            sb.AppendLine("#If a part the client has doesn't appear on this list, they can still join the server but not use the part.");
            sb.AppendLine("#The default stock parts have been added already for you.");
            sb.AppendLine("#To add a mod part, add the name from the part's .cfg file. The name is the name from the PART{} section, where underscores are replaced with periods.");
            sb.AppendLine("#[partname]");
            sb.AppendLine("#Example: mumech.MJ2.Pod (NOTE: In the part.cfg this MechJeb2 pod is named mumech_MJ2_Pod. The _ have been replaced with .)");
            sb.AppendLine("#You can use this application to generate partlists from a KSP installation if you want to add mod parts: http://forum.kerbalspaceprogram.com/threads/57284 ");
            foreach (string partName in partsList)
            {
                sb.AppendLine(partName);
            }
            sb.AppendLine("");
            return sb.ToString();
        }

        public static long GetCurrentUnixTime()
        {
            return ((DateTime.UtcNow.Ticks - new DateTime(1970, 1, 1).Ticks) / TimeSpan.TicksPerSecond);
        }
    }

    public enum CraftType
    {
        VAB,
        SPH,
        SUBASSEMBLY
    }

    public enum ClientMessageType
    {
        HEARTBEAT,
        HANDSHAKE_RESPONSE,
        CHAT_MESSAGE,
        PLAYER_STATUS,
        PLAYER_COLOR,
        SCENARIO_DATA,
        KERBALS_REQUEST,
        KERBAL_PROTO,
        KERBAL_REMOVE,
        VESSELS_REQUEST,
        VESSEL_PROTO,
        VESSEL_UPDATE,
        VESSEL_REMOVE,
        CRAFT_LIBRARY,
        SCREENSHOT_LIBRARY,
        FLAG_SYNC,
        SYNC_TIME_REQUEST,
        PING_REQUEST,
        MOTD_REQUEST,
        WARP_CONTROL,
        LOCK_SYSTEM,
        MOD_DATA,
        SPLIT_MESSAGE,
        CONNECTION_END
    }

    public enum ServerMessageType
    {
        HEARTBEAT,
        HANDSHAKE_CHALLANGE,
        HANDSHAKE_REPLY,
        SERVER_SETTINGS,
        CHAT_MESSAGE,
        PLAYER_STATUS,
        PLAYER_COLOR,
        PLAYER_JOIN,
        PLAYER_DISCONNECT,
        SCENARIO_DATA,
        KERBAL_REPLY,
        KERBAL_COMPLETE,
        KERBAL_REMOVE,
        VESSEL_LIST,
        VESSEL_PROTO,
        VESSEL_UPDATE,
        VESSEL_COMPLETE,
        VESSEL_REMOVE,
        CRAFT_LIBRARY,
        SCREENSHOT_LIBRARY,
        FLAG_SYNC,
        SET_SUBSPACE,
        SYNC_TIME_REPLY,
        PING_REPLY,
        MOTD_REPLY,
        WARP_CONTROL,
        ADMIN_SYSTEM,
        LOCK_SYSTEM,
        MOD_DATA,
        SPLIT_MESSAGE,
        CONNECTION_END
    }

    public enum ConnectionStatus
    {
        DISCONNECTED,
        CONNECTING,
        CONNECTED
    }

    public enum ClientState
    {
        DISCONNECTED,
        CONNECTING,
        CONNECTED,
        HANDSHAKING,
        AUTHENTICATED,
        TIME_SYNCING,
        TIME_SYNCED,
        SYNCING_KERBALS,
        KERBALS_SYNCED,
        SYNCING_VESSELS,
        VESSELS_SYNCED,
        TIME_LOCKING,
        TIME_LOCKED,
        STARTING,
        RUNNING,
        DISCONNECTING
    }

    public enum WarpMode
    {
        MCW_FORCE,
        MCW_VOTE,
        MCW_LOWEST,
        SUBSPACE_SIMPLE,
        SUBSPACE,
        NONE
    }

    public enum GameMode
    {
        SANDBOX,
        SCIENCE,
        CAREER
    }

    public enum ModControlMode
    {
        DISABLED,
        ENABLED_STOP_INVALID_PART_SYNC,
        ENABLED_STOP_INVALID_PART_LAUNCH
    }

    public enum WarpMessageType
    {
        //MCW_VOTE
        REQUEST_VOTE,
        REPLY_VOTE,
        //ALL
        CHANGE_WARP,
        //MCW_VOTE/FORCE
        REQUEST_CONTROLLER,
        RELEASE_CONTROLLER,
        //MCW_VOTE/FORCE/LOWEST
        IGNORE_WARP,
        SET_CONTROLLER,
        //ALL
        NEW_SUBSPACE,
        CHANGE_SUBSPACE,
        RELOCK_SUBSPACE,
        REPORT_RATE
    }

    public enum CraftMessageType
    {
        LIST,
        REQUEST_FILE,
        RESPOND_FILE,
        UPLOAD_FILE,
        ADD_FILE,
        DELETE_FILE,
    }

    public enum ScreenshotMessageType
    {
        NOTIFY,
        SEND_START_NOTIFY,
        WATCH,
        SCREENSHOT,
    }

    public enum ChatMessageType
    {
        LIST,
        JOIN,
        LEAVE,
        CHANNEL_MESSAGE,
        PRIVATE_MESSAGE,
        CONSOLE_MESSAGE
    }

    public enum AdminMessageType
    {
        LIST,
        ADD,
        REMOVE,
    }

    public enum LockMessageType
    {
        LIST,
        ACQUIRE,
        RELEASE,
    }

    public enum FlagMessageType
    {
        LIST,
        FLAG_DATA,
        UPLOAD_FILE,
        DELETE_FILE,
    }

    public enum PlayerColorMessageType
    {
        LIST,
        SET,
    }

    public class ClientMessage
    {
        public bool handled;
        public ClientMessageType type;
        public byte[] data;
    }

    public class ServerMessage
    {
        public ServerMessageType type;
        public byte[] data;
    }

    public class PlayerStatus
    {
        public string playerName;
        public string vesselText;
        public string statusText;
    }

    public class Subspace
    {
        public long serverClock;
        public double planetTime;
        public float subspaceSpeed;
    }

    public class PlayerWarpRate
    {
        public bool isPhysWarp = false;
        public int rateIndex = 0;
        public long serverClock = 0;
        public double planetTime = 0;
    }

    public enum HandshakeReply : int
    {
        HANDSHOOK_SUCCESSFULLY = 0,
        PROTOCOL_MISMATCH = 1,
        ALREADY_CONNECTED = 2,
        RESERVED_NAME = 3,
        INVALID_KEY = 4,
        PLAYER_BANNED = 5,
        SERVER_FULL = 6,
        NOT_WHITELISTED = 7,
        INVALID_PLAYERNAME = 98,
        MALFORMED_HANDSHAKE = 99
    }

    public enum GameDifficulty : int
    {
        EASY,
        NORMAL,
        MODERATE,
        HARD,
        CUSTOM
    }
}
