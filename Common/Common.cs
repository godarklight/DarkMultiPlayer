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
        public const int PROTOCOL_VERSION = 45;
        //Program version. This is written in the build scripts.
        public const string PROGRAM_VERSION = "Custom";
        //Mod control version - The last version to add parts
        public const string MODCONTROL_VERSION = "1.2";
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
            List<string> stockPartList = new List<string>();
            stockPartList.Add("StandardCtrlSrf");
            stockPartList.Add("CanardController");
            stockPartList.Add("noseCone");
            stockPartList.Add("AdvancedCanard");
            stockPartList.Add("airplaneTail");
            stockPartList.Add("deltaWing");
            stockPartList.Add("noseConeAdapter");
            stockPartList.Add("rocketNoseCone");
            stockPartList.Add("smallCtrlSrf");
            stockPartList.Add("standardNoseCone");
            stockPartList.Add("sweptWing");
            stockPartList.Add("tailfin");
            stockPartList.Add("wingConnector");
            stockPartList.Add("winglet");
            stockPartList.Add("R8winglet");
            stockPartList.Add("winglet3");
            stockPartList.Add("Mark1Cockpit");
            stockPartList.Add("Mark2Cockpit");
            stockPartList.Add("Mark1-2Pod");
            stockPartList.Add("advSasModule");
            stockPartList.Add("asasmodule1-2");
            stockPartList.Add("avionicsNoseCone");
            stockPartList.Add("crewCabin");
            stockPartList.Add("cupola");
            stockPartList.Add("landerCabinSmall");
            stockPartList.Add("mark3Cockpit");
            stockPartList.Add("mk1pod");
            stockPartList.Add("mk2LanderCabin");
            stockPartList.Add("probeCoreCube");
            stockPartList.Add("probeCoreHex");
            stockPartList.Add("probeCoreOcto");
            stockPartList.Add("probeCoreOcto2");
            stockPartList.Add("probeCoreSphere");
            stockPartList.Add("probeStackLarge");
            stockPartList.Add("probeStackSmall");
            stockPartList.Add("sasModule");
            stockPartList.Add("seatExternalCmd");
            stockPartList.Add("rtg");
            stockPartList.Add("batteryBank");
            stockPartList.Add("batteryBankLarge");
            stockPartList.Add("batteryBankMini");
            stockPartList.Add("batteryPack");
            stockPartList.Add("ksp.r.largeBatteryPack");
            stockPartList.Add("largeSolarPanel");
            stockPartList.Add("solarPanels1");
            stockPartList.Add("solarPanels2");
            stockPartList.Add("solarPanels3");
            stockPartList.Add("solarPanels4");
            stockPartList.Add("solarPanels5");
            stockPartList.Add("JetEngine");
            stockPartList.Add("engineLargeSkipper");
            stockPartList.Add("ionEngine");
            stockPartList.Add("liquidEngine");
            stockPartList.Add("liquidEngine1-2");
            stockPartList.Add("liquidEngine2");
            stockPartList.Add("liquidEngine2-2");
            stockPartList.Add("liquidEngine3");
            stockPartList.Add("liquidEngineMini");
            stockPartList.Add("microEngine");
            stockPartList.Add("nuclearEngine");
            stockPartList.Add("radialEngineMini");
            stockPartList.Add("radialLiquidEngine1-2");
            stockPartList.Add("sepMotor1");
            stockPartList.Add("smallRadialEngine");
            stockPartList.Add("solidBooster");
            stockPartList.Add("solidBooster1-1");
            stockPartList.Add("toroidalAerospike");
            stockPartList.Add("turboFanEngine");
            stockPartList.Add("MK1Fuselage");
            stockPartList.Add("Mk1FuselageStructural");
            stockPartList.Add("RCSFuelTank");
            stockPartList.Add("RCSTank1-2");
            stockPartList.Add("rcsTankMini");
            stockPartList.Add("rcsTankRadialLong");
            stockPartList.Add("fuelTank");
            stockPartList.Add("fuelTank1-2");
            stockPartList.Add("fuelTank2-2");
            stockPartList.Add("fuelTank3-2");
            stockPartList.Add("fuelTank4-2");
            stockPartList.Add("fuelTankSmall");
            stockPartList.Add("fuelTankSmallFlat");
            stockPartList.Add("miniFuelTank");
            stockPartList.Add("mk2Fuselage");
            stockPartList.Add("mk2SpacePlaneAdapter");
            stockPartList.Add("mk3Fuselage");
            stockPartList.Add("mk3spacePlaneAdapter");
            stockPartList.Add("radialRCSTank");
            stockPartList.Add("toroidalFuelTank");
            stockPartList.Add("xenonTank");
            stockPartList.Add("xenonTankRadial");
            stockPartList.Add("adapterLargeSmallBi");
            stockPartList.Add("adapterLargeSmallQuad");
            stockPartList.Add("adapterLargeSmallTri");
            stockPartList.Add("adapterSmallMiniShort");
            stockPartList.Add("adapterSmallMiniTall");
            stockPartList.Add("nacelleBody");
            stockPartList.Add("radialEngineBody");
            stockPartList.Add("smallHardpoint");
            stockPartList.Add("stationHub");
            stockPartList.Add("structuralIBeam1");
            stockPartList.Add("structuralIBeam2");
            stockPartList.Add("structuralIBeam3");
            stockPartList.Add("structuralMiniNode");
            stockPartList.Add("structuralPanel1");
            stockPartList.Add("structuralPanel2");
            stockPartList.Add("structuralPylon");
            stockPartList.Add("structuralWing");
            stockPartList.Add("strutConnector");
            stockPartList.Add("strutCube");
            stockPartList.Add("strutOcto");
            stockPartList.Add("trussAdapter");
            stockPartList.Add("trussPiece1x");
            stockPartList.Add("trussPiece3x");
            stockPartList.Add("CircularIntake");
            stockPartList.Add("landingLeg1");
            stockPartList.Add("landingLeg1-2");
            stockPartList.Add("RCSBlock");
            stockPartList.Add("stackDecoupler");
            stockPartList.Add("airScoop");
            stockPartList.Add("commDish");
            stockPartList.Add("decoupler1-2");
            stockPartList.Add("dockingPort1");
            stockPartList.Add("dockingPort2");
            stockPartList.Add("dockingPort3");
            stockPartList.Add("dockingPortLarge");
            stockPartList.Add("dockingPortLateral");
            stockPartList.Add("fuelLine");
            stockPartList.Add("ladder1");
            stockPartList.Add("largeAdapter");
            stockPartList.Add("largeAdapter2");
            stockPartList.Add("launchClamp1");
            stockPartList.Add("linearRcs");
            stockPartList.Add("longAntenna");
            stockPartList.Add("miniLandingLeg");
            stockPartList.Add("parachuteDrogue");
            stockPartList.Add("parachuteLarge");
            stockPartList.Add("parachuteRadial");
            stockPartList.Add("parachuteSingle");
            stockPartList.Add("radialDecoupler");
            stockPartList.Add("radialDecoupler1-2");
            stockPartList.Add("radialDecoupler2");
            stockPartList.Add("ramAirIntake");
            stockPartList.Add("roverBody");
            stockPartList.Add("sensorAccelerometer");
            stockPartList.Add("sensorBarometer");
            stockPartList.Add("sensorGravimeter");
            stockPartList.Add("sensorThermometer");
            stockPartList.Add("spotLight1");
            stockPartList.Add("spotLight2");
            stockPartList.Add("stackBiCoupler");
            stockPartList.Add("stackDecouplerMini");
            stockPartList.Add("stackPoint1");
            stockPartList.Add("stackQuadCoupler");
            stockPartList.Add("stackSeparator");
            stockPartList.Add("stackSeparatorBig");
            stockPartList.Add("stackSeparatorMini");
            stockPartList.Add("stackTriCoupler");
            stockPartList.Add("telescopicLadder");
            stockPartList.Add("telescopicLadderBay");
            stockPartList.Add("SmallGearBay");
            stockPartList.Add("roverWheel1");
            stockPartList.Add("roverWheel2");
            stockPartList.Add("roverWheel3");
            stockPartList.Add("wheelMed");
            stockPartList.Add("flag");
            stockPartList.Add("kerbalEVA");
            stockPartList.Add("mediumDishAntenna");
            stockPartList.Add("GooExperiment");
            stockPartList.Add("science.module");
            stockPartList.Add("RAPIER");
            stockPartList.Add("Large.Crewed.Lab");
            //0.23.5 parts
            stockPartList.Add("GrapplingDevice");
            stockPartList.Add("LaunchEscapeSystem");
            stockPartList.Add("MassiveBooster");
            stockPartList.Add("PotatoRoid");
            stockPartList.Add("Size2LFB");
            stockPartList.Add("Size3AdvancedEngine");
            stockPartList.Add("size3Decoupler");
            stockPartList.Add("Size3EngineCluster");
            stockPartList.Add("Size3LargeTank");
            stockPartList.Add("Size3MediumTank");
            stockPartList.Add("Size3SmallTank");
            stockPartList.Add("Size3to2Adapter");
            //0.24 parts
            stockPartList.Add("omsEngine");
            stockPartList.Add("vernierEngine");
            //0.25 parts
            stockPartList.Add("delta.small");
            stockPartList.Add("elevon2");
            stockPartList.Add("elevon3");
            stockPartList.Add("elevon5");
            stockPartList.Add("IntakeRadialLong");
            stockPartList.Add("MK1IntakeFuselage");
            stockPartList.Add("mk2.1m.AdapterLong");
            stockPartList.Add("mk2.1m.Bicoupler");
            stockPartList.Add("mk2CargoBayL");
            stockPartList.Add("mk2CargoBayS");
            stockPartList.Add("mk2Cockpit.Inline");
            stockPartList.Add("mk2Cockpit.Standard");
            stockPartList.Add("mk2CrewCabin");
            stockPartList.Add("mk2DockingPort");
            stockPartList.Add("mk2DroneCore");
            stockPartList.Add("mk2FuselageLongLFO");
            stockPartList.Add("mk2FuselageShortLFO");
            stockPartList.Add("mk2FuselageShortLiquid");
            stockPartList.Add("mk2FuselageShortMono");
            stockPartList.Add("shockConeIntake");
            stockPartList.Add("structuralWing2");
            stockPartList.Add("structuralWing3");
            stockPartList.Add("structuralWing4");
            stockPartList.Add("sweptWing1");
            stockPartList.Add("sweptWing2");
            stockPartList.Add("wingConnector2");
            stockPartList.Add("wingConnector3");
            stockPartList.Add("wingConnector4");
            stockPartList.Add("wingConnector5");
            stockPartList.Add("wingStrake");
            //0.90 parts
            stockPartList.Add("adapterMk3-Mk2");
            stockPartList.Add("adapterMk3-Size2");
            stockPartList.Add("adapterMk3-Size2Slant");
            stockPartList.Add("adapterSize2-Mk2");
            stockPartList.Add("adapterSize2-Size1");
            stockPartList.Add("adapterSize2-Size1Slant");
            stockPartList.Add("adapterSize3-Mk3");
            stockPartList.Add("mk3CargoBayL");
            stockPartList.Add("mk3CargoBayM");
            stockPartList.Add("mk3CargoBayS");
            stockPartList.Add("mk3Cockpit.Shuttle");
            stockPartList.Add("mk3CrewCabin");
            stockPartList.Add("mk3FuselageLF.100");
            stockPartList.Add("mk3FuselageLF.25");
            stockPartList.Add("mk3FuselageLF.50");
            stockPartList.Add("mk3FuselageLFO.100");
            stockPartList.Add("mk3FuselageLFO.25");
            stockPartList.Add("mk3FuselageLFO.50");
            stockPartList.Add("mk3FuselageMONO");
            //1.0 parts
            stockPartList.Add("kerbalEVAfemale");
            stockPartList.Add("airbrake1");
            stockPartList.Add("airlinerCtrlSrf");
            stockPartList.Add("airlinerMainWing");
            stockPartList.Add("airlinerTailFin");
            stockPartList.Add("pointyNoseConeA");
            stockPartList.Add("pointyNoseConeB");
            stockPartList.Add("airplaneTailB");
            stockPartList.Add("fairingSize1");
            stockPartList.Add("fairingSize2");
            stockPartList.Add("fairingSize3");
            stockPartList.Add("HeatShield1");
            stockPartList.Add("HeatShield2");
            stockPartList.Add("HeatShield3");
            stockPartList.Add("wingShuttleDelta");
            stockPartList.Add("elevonMk3");
            stockPartList.Add("wingShuttleElevon1");
            stockPartList.Add("wingShuttleElevon2");
            stockPartList.Add("wingShuttleRudder");
            stockPartList.Add("wingShuttleStrake");
            stockPartList.Add("delta.small");
            stockPartList.Add("mk2Cockpit.Inline");
            stockPartList.Add("mk2Cockpit.Standard");
            stockPartList.Add("mk3Cockpit.Shuttle");
            stockPartList.Add("ksp.r.largeBatteryPack");
            stockPartList.Add("solidBooster.sm");
            stockPartList.Add("fuelTank.long");
            stockPartList.Add("mk2.1m.Bicoupler");
            stockPartList.Add("mk2.1m.AdapterLong");
            stockPartList.Add("mk3FuselageLFO.100");
            stockPartList.Add("mk3FuselageLFO.25");
            stockPartList.Add("mk3FuselageLFO.50");
            stockPartList.Add("mk3FuselageLF.100");
            stockPartList.Add("mk3FuselageLF.25");
            stockPartList.Add("mk3FuselageLF.50");
            stockPartList.Add("xenonTankLarge");
            stockPartList.Add("mk3Cockpit.Shuttle");
            stockPartList.Add("FuelCell");
            stockPartList.Add("FuelCellArray");
            stockPartList.Add("ISRU");
            stockPartList.Add("LargeTank");
            stockPartList.Add("OrbitalScanner");
            stockPartList.Add("RadialDrill");
            stockPartList.Add("SmallTank");
            stockPartList.Add("SurfaceScanner");
            stockPartList.Add("SurveyScanner");
            stockPartList.Add("sensorAtmosphere");
            stockPartList.Add("Large.Crewed.Lab");
            stockPartList.Add("science.module");
            stockPartList.Add("radialDrogue");
            stockPartList.Add("ServiceBay.125");
            stockPartList.Add("ServiceBay.250");
            stockPartList.Add("GearFixed");
            stockPartList.Add("GearFree");
            stockPartList.Add("GearLarge");
            stockPartList.Add("GearMedium");
            //1.0.1 parts
            stockPartList.Add("basicFin");
            //1.0.3 parts
            stockPartList.Add("foldingRadLarge");
            stockPartList.Add("foldingRadMed");
            stockPartList.Add("foldingRadSmall");
            stockPartList.Add("radPanelLg");
            stockPartList.Add("radPanelSm");
            //1.0.5 parts
            stockPartList.Add("turboJet");
            stockPartList.Add("turboFanSize2");
            stockPartList.Add("miniJetEngine");
            stockPartList.Add("SSME");
            stockPartList.Add("adapterEngines");
            stockPartList.Add("miniFuselage");
            stockPartList.Add("miniIntake");
            stockPartList.Add("MK1CrewCabin");
            stockPartList.Add("MiniISRU");
            stockPartList.Add("MiniDrill");
            stockPartList.Add("RadialOreTank");
            stockPartList.Add("radPanelEdge");
            stockPartList.Add("mk3CargoRamp");
			//1.1.0 parts
			stockPartList.Add("InflatableHeatShield");
			stockPartList.Add("HECS2.ProbeCore");
			stockPartList.Add("HighGainAntenna");
			stockPartList.Add("LgRadialSolarPanel");
			stockPartList.Add("GearSmall");
            // 1.2 parts
            stockPartList.Add("ScienceBox");
            stockPartList.Add("SurfAntenna");
            stockPartList.Add("HighGainAntenna5");
            stockPartList.Add("RelayAntenna100");
            stockPartList.Add("RelayAntenna5");
            stockPartList.Add("RelayAntenna50");
            stockPartList.Add("HeatShield0");
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
