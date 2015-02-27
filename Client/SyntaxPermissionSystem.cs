using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KSP;
using UnityEngine;
using DarkMultiPlayer;
using DarkMultiPlayerCommon;
using System.Timers;
using System.Globalization;

namespace PermissionSystem
{
    public static class SyntaxPermissionSystem
    {
        #region Permission System basics

        private static string currentVesselGuid = "";
        private static bool ownerVerified = false;
        
        public static bool spectateAllowed = false;

        public static bool OwnerVerified
        {
            get { return ownerVerified; }
        }

        internal static void PermissionSystemResponseHandler(ServerMessage message)
        {
            PermissionSystemMessageType messagetype;

            using (MessageStream2.MessageReader mr = new MessageStream2.MessageReader(message.data))
            {
                messagetype = (PermissionSystemMessageType)mr.Read<int>();

                switch (messagetype)
                {
                    case PermissionSystemMessageType.Check:

                        string ownerYesNo = mr.Read<string>();
                        string spectateYesNo = mr.Read<string>();
                        bool spectateAllowed = mr.Read<bool>();
                        PermissionCheckResponse(ownerYesNo, spectateYesNo, spectateAllowed);
                        break;
                    case PermissionSystemMessageType.Claim:
                        string responseLine = mr.Read<string>();
                        PermissionClaimResponse(responseLine);
                        break;
                }
            }

        }

        #endregion

        #region Permission Check
        /// <summary>
        /// Prevents controlling already claimed vessels.
        /// </summary>
        /// <param name="vesselToCheck">Vessel to check for claim</param>
        /// <param name="playername">The player trying to access the vessel</param>
        internal static void PermissionCheck(Vessel vesselToCheck, string playername)
        {
            PermissionChecker(vesselToCheck, playername);
        }
        
        // syntaxcode connection
        private static void PermissionChecker(Vessel vesselToCheck,string username)
        {
            ScreenMessages.print("Sending permission check..");
            string vesselguid = vesselToCheck.id.ToString();
            currentVesselGuid = vesselguid;
            using (MessageStream2.MessageWriter mw = new MessageStream2.MessageWriter())
            {
                mw.Write<int>((int)PermissionSystemMessageType.Check);
                mw.Write<string>(username);
                mw.Write<string>(vesselguid);
                NetworkWorker.fetch.SendPermissionRequest(mw.GetMessageBytes());
            }
        }

        // The response from the server
        private static void PermissionCheckResponse(string ownerYesNo, string spectateYesNo, bool spectateAllowed)
        {
            ScreenMessages.print("Receiving permission check..");
            bool owner = false; // inserted for possible later usage.
            if(ownerYesNo=="")
            {
                // report no response was written thus an internal failure of the code
                DarkLog.Debug("Response was empty. Terminated code to prevent crashing.");
                return;
            }
            if(ownerYesNo == "VesselNotProtected")
            {
                owner = true;
                ReleaseFromSpectator();
            }
            if (ownerYesNo == "Yes")
            {
                owner = true;
                ReleaseFromSpectator();
            }
            if(owner)
            {
                ownerVerified = true;
            }
            if(!spectateAllowed)
            {
                // Send to KSC for prohibited spectating
                if(VesselWorker.fetch.isSpectating == true)
                {
                    HighLogic.LoadScene(GameScenes.SPACECENTER);
                    ScreenMessages.print("Kicked to Space Centre for trying to spectate a private vessel.");
                    return;
                }

                //NetworkWorker.fetch.Disconnect("Spectating on this vessel is not allowed.");
            }
            if(!VesselWorker.fetch.isSpectating)
            {
                // report vesselworker is reporting not spectating, but force it anyway because of permission system.
                if(ownerYesNo == "No")
                {
                    owner = false;
                    if (spectateYesNo == "Yes")
                    {
                        LockToSpectator();
                    }
                    else
                    {
                        // kick the player to ksc for spectating
                        HighLogic.LoadScene(GameScenes.SPACECENTER);
                        ScreenMessages.print("Kicked to Space Centre for trying to spectate a private vessel.");
                    }
                }
            }
        }
        #endregion

        #region Claim command

        /// <summary>
        /// Claims a vessel given the vessel hasn't been claimed yet.
        /// </summary>
        /// <param name="playerName">The playername to claim the vessel for</param>
        /// <param name="personalorgroup">Personal or Group vessel</param>
        /// <param name="privateorpublic">Private or Public vessel</param>
        /// <param name="vesselguid">The vesselguid of the vessel to claim</param>
        internal static void PermissionClaim(string playerName, string personalorgroup, string accessChoice, string vesselguid)
        {
            // reformat for automated enum
            string accesstype = ToTitleCase(accessChoice);
            using (MessageStream2.MessageWriter mw = new MessageStream2.MessageWriter())
            {
                mw.Write<int>((int)PermissionSystemMessageType.Claim);
                mw.Write<string>(playerName);
                mw.Write<string>(personalorgroup);
                mw.Write<string>(accesstype);
                mw.Write<string>(vesselguid);
                NetworkWorker.fetch.SendPermissionClaimRequest(mw.GetMessageBytes());
            }
        }
        public static string ToTitleCase(string str)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str.ToLower());
        }
        // todo: print the message in the chatbox, visible only to the vesselowner.
        private static void PermissionClaimResponse(string responseline)
        {
            if (responseline == "" || responseline == "Unknown")
            {
                // report error in debug for internal permission system error
                DarkLog.Debug("Permission System: Claim response internal error diagnosed.");
                return;
            }
            if (responseline == "CannotBeClaimed")
            {
                ScreenMessages.print("Vessel Claiming failed - Vessel can't be claimed.");
                return;
            }
            else
            {
                // We have determined every other outcome, thus the vessel has been claimed.
                // Thus relaying the success message.
                ScreenMessages.print(responseline);
                // todo: print the message in the chatbox, visible only to the vesselowner.
                return;
            }

        }

        private const ControlTypes BLOCK_ALL_CONTROLS = ControlTypes.ALL_SHIP_CONTROLS | ControlTypes.ACTIONS_ALL | ControlTypes.EVA_INPUT | ControlTypes.TIMEWARP | ControlTypes.MISC | ControlTypes.GROUPS_ALL | ControlTypes.CUSTOM_ACTION_GROUPS;

        #endregion

        #region SpectateMode
        static private void LockToSpectator()
        {
            InputLockManager.SetControlLock(BLOCK_ALL_CONTROLS,"PermissionSystem-Spectator");
        }
        static private void ReleaseFromSpectator()
        {
            InputLockManager.RemoveControlLock("PermissionSystem-Spectator");
        }
        #endregion

        // outdated since removal of bool usage to return ownership
        private static bool WaitAsSpectator()
        {
            DarkLog.Debug("Setting spectate lock accordingly whilst awaiting ownership determination");
            LockToSpectator();
            ScreenMessages.print("Waiting for ownership determination..");
            return true; // return true since the vessel is locked anyway.
        }
        //Planetarium

        //private void AuthenticateUser(){}
        //private void GetVesselID(){}
        //private void SendRequest(){}
        //private void BlockAccess(){}
        //private void BlockSpectate(){}
        //private void Log(){}

    }
}
