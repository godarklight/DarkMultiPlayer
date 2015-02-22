using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PermissionSystem
{
    /// <summary>
    /// Access codes for the player vessel protection codes
    /// </summary>
    public partial class Core
    {
        // Used for programmatic access to the syntax permission system
        static private PlayerSecurity ps = new PlayerSecurity();

        public static void InitializePermissionFolder()
        {
            ps.InitialDirectoryAndFileCreation();
        }

        /// <summary>
        ///  Initializes the SyntaxCode permission system automaticly
        /// </summary>
        public Core()
        {

        }

        /// <summary>
        /// Save all SyntaxPermissionSystem memory data to file.
        /// </summary>
        static public void SaveToFile()
        {
            if (ps.SaveAll())
            {
                DarkMultiPlayerServer.DarkLog.Debug("SyntaxCode: Permissions system memory data saved to file.");
            }
            else
            {
                DarkMultiPlayerServer.DarkLog.Debug("SyntaxCode: Permissions system failed to save memory data to file.");
            }
        }

        /// <summary>
        /// Contains all player specific access codes
        /// </summary>
        static public class Player
        {
            /// <summary>
            /// Saves player credentials to the protection list, if playername exists, updates the pass/keyword
            /// </summary>
            /// <param name="playername">The player to add</param>
            /// <param name="pass">Player pass/keyword</param>
            static public void SaveCredentials(string playername)
            {
                DarkMultiPlayerServer.DarkLog.Normal("Saving player: " + playername + " to permissions users list.");
                ps.SaveCredentials(playername);
                DarkMultiPlayerServer.DarkLog.Normal("Player saved.");
            }
            /// <summary>
            /// Check wether player exists in the protection list
            /// </summary>
            /// <param name="playername">The player to check</param>
            /// <returns>True or false depending on existance of player in the protection list</returns>
            static public bool isProtected(string playername)
            {
                return ps.CheckPlayerIsProtected(playername);
            }

            /// <summary>
            /// Determine wether the player is part of a group
            /// </summary>
            /// <param name="playername">The player to check</param>
            /// <returns></returns>
            static public bool HasGroup(string playername)
            {
                bool flag = false;

                if(ps.HasGroup(playername))
                {
                    flag = true;
                }

                return flag;
            }
        }
        /// <summary>
        /// Contains all player specific vessel access codes
        /// </summary>
        static public class PVessel
        {
            /// <summary>
            /// Claims a specific vessel for the given player
            /// </summary>
            /// <param name="playername">The player to claim the vessel for</param>
            /// <param name="vesselid">The vesselid to claim</param>
            /// <returns>True or false depending on existane of player credentials</returns>
            static public bool ClaimVessel(string playername, string vesselid, VesselAccessibilityTypes _vat)
            {
                bool flag = false;
                if (Player.isProtected(playername))
                {
                    DarkMultiPlayerServer.DarkLog.Normal("Syntax Codes - Creating object - Claiming vessel: " + vesselid);
                    // Reports false if player isn't in the protection list or vessel has already been claimed
                    if (ps.ProtectVessel(playername, vesselid, _vat))
                    {
                        DarkMultiPlayerServer.DarkLog.Normal("SyntaxCodes - Creating object - claiming vessel " + vesselid + " Succesful");
                        flag = true;
                    }
                    else
                    {
                        DarkMultiPlayerServer.DarkLog.Normal("SyntaxCodes - Creating object - claiming vessel " + vesselid + " failed");
                    }
                }
                return flag;
            }
            static public bool IsProtected(string playername, string vesselid)
            {
                bool flag = false;
                string owner = "";
               if(ps.CheckVesselIsClaimed(vesselid))
               {
                   flag = true;
               }

                return flag;
            }
            static public bool IsProtected(string vesselid)
            {
                return ps.CheckVesselIsClaimed(vesselid);
            }
            static public bool IsOwner(string playername, string vesselid)
            {
                bool flag = false;
                string ownername = "";
                if(ps.CheckVesselIsClaimed(vesselid,out ownername))
                {
                    if(ownername == playername)
                    {
                        flag = true;
                    }
                }

                return flag;
            }
            static public bool GetAccessibility(string vesselid, out string _vat)
            {
                bool flag = false;
                string vesselAccessType = ps.GetVesselAccessType(vesselid);

                if (vesselAccessType != "") // maybe add :  || vesselAccessType != "private" ????
                {
                    flag = true;
                }

                _vat = vesselAccessType;
                return flag;
            }
            static public bool ChangeVesselAccessibility(string playername, string vesselid, VesselAccessibilityTypes _vat)
            {
                bool flag = false;

                if (ps.ChangeAccessibility(playername, vesselid, _vat))
                {
                    flag = true;
                }

                return flag;
            }

            static public KeyValuePair<string,PlayerVessel> SpectateMode(string vesselid)
            {
                KeyValuePair<string, PlayerVessel> returndata = new KeyValuePair<string, PlayerVessel>();

                if (FindLockedPlayerVessel(vesselid, out returndata))
                {
                    if(returndata.Key != "")
                    {
                        // vessel is found so do nothing but return the found vessel
                    }
                    else
                    {
                        // report the vessel was not found and thus it cannot be spectated.
                        returndata = new KeyValuePair<string, PlayerVessel>("VesselNotFound", new PlayerVessel());
                    }
                }
                return returndata;
            }
            static public bool SpectatingAllowed(string vesselid)
            {
                string accesstype = ps.GetVesselAccessType(vesselid);
                bool flag = false;

                if(accesstype == "spectate")
                {
                    flag = true;
                }
                return flag;
            }

            static public bool FindPlayerVessel(string playername, string vesselid,out KeyValuePair<string,PlayerVessel> playerVesselEntry)
            {
                bool flag = false;
                KeyValuePair<string, PlayerVessel> datafound = ps.ReturnPlayerVessel(playername, vesselid);
                if(datafound.Key != null)
                {
                    flag = true;
                }
                else
                {
                    datafound = new KeyValuePair<string, PlayerVessel>();
                }
                playerVesselEntry = datafound;
                return flag;
            }
            static public bool FindLockedPlayerVessel(string vesselid, out KeyValuePair<string, PlayerVessel> playerVesselEntry)
            {
                bool flag = false;
                KeyValuePair<string, PlayerVessel> datafound = ps.ReturnLockedPlayerVessel(vesselid);
                if (datafound.Key != null)
                {
                    flag = true;
                }
                else
                {
                    datafound = new KeyValuePair<string, PlayerVessel>();
                }
                playerVesselEntry = datafound;
                return flag;
            }
        }

        /// <summary>
        /// Contains all player group access codes
        /// </summary>
        static public class PGroup
        {
            static public bool AddGroup(string _gname, string _gadmin, VesselAccessibilityTypes _gvat)
            {
                return ps.AddPlayerGroup(_gname, _gvat, _gadmin);
            }

            static public bool EditGroupVesselAccessibility(string _gname, string _gadmin, VesselAccessibilityTypes _gvat)
            {
                return ps.EditPlayerGroupVesselAccess(_gname, _gadmin, _gvat);
            }

            static public bool InvitePlayerToGroup(string _gname, string _invitername, string _invitedname)
            {
                return ps.InvitePlayerToGroup(_gname, _invitername, _invitedname);
            }

            static public bool ClaimVesselForGroup(string _playername, string _vesselid, VesselAccessibilityTypes _vat)
            {
                bool flag = false;
                DarkMultiPlayerServer.DarkLog.Debug("Entering claiming for vessel method. SystemAccess Code 1.");
                if (ps.CheckPlayerIsProtected(_playername))
                {
                    string _playergroup;
                    if (ps.CheckPlayerGroup(_playername, out _playergroup))
                    {
                        DarkMultiPlayerServer.DarkLog.Debug("Entering claiming for vessel method. SystemAccess Code 2.");
                        if (ps.ProtectGroupVessel(_playername, _playergroup, _vesselid, _vat))
                        {
                            DarkMultiPlayerServer.DarkLog.Debug("Entering claiming for vessel method. SystemAccess Code 3.");
                            flag = true;
                        }
                    }
                }
                return flag;
            }

            static public bool UnClaimVesselFromGroup(string _playername, string _vesselid)
            {
                bool flag = false;
                if (ps.CheckPlayerIsProtected(_playername))
                {
                    string playergroup = "";
                    if (ps.CheckPlayerGroup(_playername, out playergroup))
                    {
                        if (ps.RemoveVesselFromGroup(_playername, playergroup, _vesselid))
                        {
                            flag = true;
                        }
                    }
                }
                return flag;
            }

            static public bool ChangeVesselAccessibility(string _playername, string _gname, string _vesselid, VesselAccessibilityTypes _gvat)
            {
                bool flag = false;

                if (ps.ChangeAccessibility(_playername, _gname, _vesselid, _gvat))
                {
                    flag = true;
                }


                return flag;
            }
        }

    }
    public enum VesselAccessibilityTypes
    {
        Private, Spectate, Public, Group
    }
}
