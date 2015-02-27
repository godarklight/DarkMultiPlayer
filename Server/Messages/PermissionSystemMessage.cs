using DarkMultiPlayerCommon;
using MessageStream2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DarkMultiPlayerServer.Messages
{
    class PermissionSystemMessage
    {
        // Handles all the requests client/server messages could have.

        internal static void HandlePermissionSystem(ClientObject client, byte[] data)
        {
            DarkLog.Debug("Permission System: Entering permission system handler");
            using(MessageReader mr = new MessageReader(data))
            {
                PermissionSystemMessageType messagetype = (PermissionSystemMessageType)mr.Read<int>();
                string pname, vguid;
                switch(messagetype)
                {
                    case PermissionSystemMessageType.Check:
                        DarkLog.Debug("Permission System: Check request recognised");
                        pname = mr.Read<string>();
                        vguid = mr.Read<string>();
                        HandlePermissionCheckRequest(client, pname, vguid);
                        break;
                    case PermissionSystemMessageType.Claim:
                        DarkLog.Debug("Permission System: Claim request recognised");
                        pname = mr.Read<string>();
                        string pOrG = mr.Read<string>();
                        string aT = mr.Read<string>();
                        vguid = mr.Read<string>();
                        HandlePermissionClaimRequest(client, pname, vguid, pOrG, aT);
                        break;
                    default:
                        // report unknown messagetype
                        break;
                }
            }
        }

        static private void HandlePermissionClaimRequest(ClientObject client, string playername, string vesselguid, string personalOrGroup, string AccessType)
        {
            if (playername != client.playerName)
            {
                client.disconnectClient = true;
                DarkLog.Debug("Kicked client for stealing a vessel from permission message. Section 3.");
                Messages.ConnectionEnd.SendConnectionEnd(client, "Stealing vessels is not allowed.");
                ClientHandler.DisconnectClient(client);
                return;
            }
            // Has been determined the requesting player is the same player as the ClientObject client, so continue.
            if (!PermissionSystem.Core.Player.isProtected(client.playerName))
            {
                playername = client.playerName;
                DarkLog.Debug("Saving player to file.");
                PermissionSystem.Core.Player.SaveCredentials(playername);
            }
            DarkLog.Debug("Handling player claim request");
            if (HandleClaimRequest(playername, personalOrGroup, AccessType, vesselguid))
            {
                // Report the vessel has been claimed.
                DarkLog.Debug("Vessel has been claimed and handled.");
                HandleResponse(client, "claim", true);
            }
            else
            {
                // Report the vessel cannot be claimed because it has already been claimed by someone else
                HandleResponse(client, "claim", false);
            }

        }
        static private void HandlePermissionCheckRequest(ClientObject client, string playername, string vesselguid)
        {
            // update to use personal message system
            if (!PermissionSystem.Core.AntiCheatSystem.SAHSCheck(client, vesselguid))
            {
                // Kick the client for failed anti-cheat check for some reason
                // Failed check only comes up when attemting to steal vessels.
                // So issue a kick command for it.
                client.disconnectClient = true;
                DarkLog.Debug("Kicked client for stealing a vessel from permission message. Section 3.");
                Messages.ConnectionEnd.SendConnectionEnd(client, "Stealing vessels is not allowed.");
                ClientHandler.DisconnectClient(client);
                return;
            }
            else
            {
                // Report the client has been recognised as the owner of the vessel
                bool spectateAllowed = PermissionSystem.Core.PVessel.SpectatingAllowed(vesselguid);
                string responseLine = "No";
                string spectate = "No";

                if (spectateAllowed)
                {
                    spectate = "Yes";
                }
                if (PermissionSystem.Core.PVessel.IsOwner(client.playerName, client.activeVessel))
                {
                    responseLine = "Yes";
                }
                responseLine = string.Format("{0},{1}", responseLine, spectate);
                if (!PermissionSystem.Core.PVessel.IsProtected(vesselguid))
                {
                    responseLine = "VesselNotProtected";
                    spectate = "Yes";
                }
                HandleResponse(client, "ownercheck", responseLine, spectate);
            }


            //DarkLog.Debug("Handling permission handled.");
        }
        static private void HandleResponse(ClientObject client, string command, string ownerYesNo,string spectateYesNo)
        {
            ServerMessage returnMessage = new ServerMessage();
            returnMessage.type = ServerMessageType.SYNTAX_BRIDGE;
            //string messageline = string.Format("{0},{1}", command, response);
            bool spectate = false;
            //string[] args = response.Split(',');
            if(ownerYesNo == "Yes")
            {
                spectate = true;
            }
            try
            {
                // Convert the messageline into bytes
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)PermissionSystemMessageType.Check);
                    mw.Write<string>(ownerYesNo);
                    mw.Write<string>(spectateYesNo);
                    mw.Write<bool>(spectate);
                    returnMessage.data = mw.GetMessageBytes();
                }

                // Send the message to the client
                //DarkLog.Debug("Sending respone message to client..");
                ClientHandler.SendToClient(client, returnMessage, true);
                DarkLog.Debug("Permission System: Permission Check Response message sent to client" + client.playerName + " succesfully.");
            }
            catch
            {
                DarkLog.Debug("Permission System - Permission Check Response handling failed!");
            }
        }
        static private void HandleResponse(ClientObject client, string command, bool vesselClaimSuccess)
        {
            ServerMessage returnMessage = new ServerMessage();
            returnMessage.type = ServerMessageType.SYNTAX_BRIDGE;
            try
            {
                string claimResponse = "UNKNOWN";
                if(vesselClaimSuccess)
                {
                    claimResponse = "Vessel has been claimed for client: " + client.playerName;
                }
                else
                {
                    claimResponse = "CannotBeClaimed";
                }
                // Convert the messageline into bytes
                using (MessageWriter mw = new MessageWriter())
                {
                    mw.Write<int>((int)PermissionSystemMessageType.Claim);
                    mw.Write<string>(claimResponse);
                    returnMessage.data = mw.GetMessageBytes();
                }

                // Send the message to the client
                //DarkLog.Debug("Sending respone message to client..");
                ClientHandler.SendToClient(client, returnMessage, true);
                DarkLog.Debug("Permission System: Permission Check Response message sent to client" + client.playerName + " succesfully.");
            }
            catch
            {
                DarkLog.Debug("Permission System - Claim Response handling failed!");
            }
        }
        static private bool HandleClaimRequest(string pName, string personalOrGroup, string vesselAccessType, string vesselGuid)
        {
            bool flag = false;
            string personalorgroupFormatted = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(personalOrGroup.ToLower());
            DarkLog.Normal("Determining player vessel arguments");
            DarkLog.Normal(string.Format("SyntaxCode: For debug sake show arguments: {0},{1},{2},{3}", pName, personalOrGroup, vesselAccessType, vesselGuid));
            if (personalOrGroup == "Personal")
            {
                DarkLog.Normal("SyntaxCodes: Claiming vessel: " + vesselGuid + " for player " + pName);
                if(PermissionSystem.Core.PVessel.ClaimVessel(pName,vesselGuid,(PermissionSystem.VesselAccessibilityTypes)Enum.Parse(typeof(PermissionSystem.VesselAccessibilityTypes), vesselAccessType)))
                {
                    flag = true;
                    DarkLog.Normal("SyntaxCodes: Vessel claimed as "+ vesselAccessType + " .");
                }
                else
                {
                    DarkLog.Debug("Vessel claiming unsuccesfull.");
                }
            }
            else if (personalOrGroup == "Group")
            {
                if (PermissionSystem.Core.Player.HasGroup(pName))
                {
                    if (PermissionSystem.Core.PGroup.ClaimVesselForGroup(pName, vesselGuid, (PermissionSystem.VesselAccessibilityTypes)Enum.Parse(typeof(PermissionSystem.VesselAccessibilityTypes), vesselAccessType)))
                    {
                        flag = true;
                        DarkLog.Normal("SyntaxCodes: Vessel claimed as " + vesselAccessType + " vessel within group.");
                    }
                    else
                    {
                        DarkLog.Debug("Vessel claiming unsuccesfull.");
                    }
                }
            }
            else
            {
                // report invalid /claim command
                DarkLog.Debug("Vessel claiming message unreadable.");
            }
            return flag;
        }

    }
}
