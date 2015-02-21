using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DarkMultiPlayerServer;

namespace SyntaxMPProtection
{
    public partial class SyntaxCode
    {
        // Access codes for the codes behind the anti hijack and cheat system.
        public static class SyntaxAntiCheatSystem
        {
            static private SyntaxPermissionSystemHandler SPSHandler = new SyntaxPermissionSystemHandler();
            static internal LockSystem ls = new LockSystem();
            static private Dictionary<string, string> LockList = LockSystem.fetch.GetLockList();

            //static internal LockSystem LS
            //{
            //    get { return ls; }
            //}

            static public bool init()
            {
                bool flag = false;
                
                



                return flag;
            }


            /// <summary>
            /// Syntax Anti Hijack System Check(SAHS): Checks wether the requested vessel belongs to the client requesting it.
            /// If it belongs to the client, it will allow sending it. If not, it will only send the vessel if the AccessType is either 'public'
            /// or 'spectate'. In case of 'spectate' it will keep the vessel locked to prevent taking over the vessel.
            /// </summary>
            /// <param name="client">The client requesting the vessel</param>
            /// <param name="requestedVesselGuid">The requested vessel</param>
            /// <returns>True if allowed to request, false if not</returns>
            public static bool SAHSCheck(ClientObject client, string requestedVesselGuid)
            {
                bool flag = false;
                bool spectatingIsAllowed = false;
                bool entryAlreadyExists = false;

                if (SPSHandler.SAHSCheck(client, requestedVesselGuid, out spectatingIsAllowed))
                {
                    flag = true;
                }
                else
                {
                    if(spectatingIsAllowed)
                    {
                        // Allow spectating of the vessel but lock the spectator out of the controls.
                        KeyValuePair<string, string> newLockEntry = new KeyValuePair<string, string>();
                        KeyValuePair<string, PlayerVessel> foundVesselEntry = new KeyValuePair<string, PlayerVessel>();
                        if(SyntaxPlayerVessel.FindLockedPlayerVessel(requestedVesselGuid, out foundVesselEntry))
                        {
                            newLockEntry = new KeyValuePair<string, string>(foundVesselEntry.Key, foundVesselEntry.Value.VesselID);
                            foreach(KeyValuePair<string,string> lockentry in LockList)
                            {
                                if((lockentry.Key == newLockEntry.Key) && (lockentry.Value == newLockEntry.Value))
                                {
                                    entryAlreadyExists = true;
                                    break;
                                }
                            }
                            if(!entryAlreadyExists)
                            {
                                LockList.Add(newLockEntry.Key, newLockEntry.Value);
                            }
                        }
                    }
                    else
                    {
                        // deny access and spectating because of permissions set on the vessel
                        if(entryAlreadyExists)
                        {
                            // Report attempt to overwrite a lock has been diagnosed
                        }
                    }
                }
                return flag;
            }
        }

        // Codes behind the Syntax Anti cheat/hijack system
        // handles the requests to and from the syntax permission system to avoid abuse on clientside
        private class SyntaxPermissionSystemHandler
        {
            //private LockSystem ls;
            //protected SyntaxPermissionSystemHandler()
            //{
            //    ls = new LockSystem();
            //}

            public bool SAHSCheck(ClientObject client, string requestedVesselid, out bool SpectatingAllowed)
            {
                bool flag = false;
                bool spectateIsAllowed = false;

                if(AntiHijackVessel(client,requestedVesselid,out spectateIsAllowed))
                {
                    if(!spectateIsAllowed)
                    {
                        // Allow request because requesting client is the confirmed owner or requested vesselid has permissions set to public
                        KeyValuePair<string,PlayerVessel> pvEntry;
                        if(SyntaxPlayerVessel.FindPlayerVessel(client.playerName,requestedVesselid,out pvEntry))
                        {
                            // allow locking of the player vessel entry for the owner or public usage
                            // lock the vessel here or allow DMP to lock it as active vessel ?? ..
                            
                        }
                        flag = true;
                    }
                    else
                    {
                        // Allow request because requested vesselid has permissions set to spectate, but lock the vessel to block interaction.
                        // todo: lock vessel
                        //lock(SyntaxCode.SyntaxPlayerVessel.SpectateMode(requestedVesselid))
                        //{

                        //}

                        flag = true;
                    }


                }
                else
                {
                    // block request because requesting client is not allowed to control or see the requested vesselid.
                }
                SpectatingAllowed = spectateIsAllowed;
                return flag;
            }


            // Checks wether a client is the actual owner of the vesselid, and if not wether the vesselid has an accesstype of public or spectate
            private bool AntiHijackVessel(ClientObject client, string requestedvesselid, out bool spectateAllowed)
            {
                bool flag = false;
                bool spectate = false;
                if(SyntaxPlayerVessel.IsProtected(client.playerName,requestedvesselid))
                {
                    if(SyntaxPlayerVessel.IsOwner(client.playerName,requestedvesselid))
                    {
                        // allow vessel request
                        flag = true;
                        
                    }
                    else
                    {
                        string accesstype;
                        if(SyntaxPlayerVessel.GetAccessibility(requestedvesselid,out accesstype))
                        {
                            if(accesstype.ToString() == "public")
                            {
                                // allow vessel request
                                flag = true;
                            }
                            else if(accesstype.ToString() == "spectate")
                            {
                                // allow vessel spectate, but lock vessel
                                spectate = true;
                            }
                            else
                            {
                                // deny vessel request
                            }
                        }
                    }
                }
                spectateAllowed = spectate;
                return flag;
            }

            // Checks wether people are hijacking vessels or not. If so, it either throws them back to kerbal space command or disconnects them.
            private void AntiHijackCheck()
            {
                ClientObject[] clients = ClientHandler.GetClients(); // get the dmp server clients

                foreach(ClientObject client in clients)
                {
                    if(SyntaxPlayerVessel.IsProtected(client.playerName,client.activeVessel))
                    {
                        if(SyntaxPlayerVessel.IsOwner(client.playerName,client.activeVessel))
                        {
                            // give control over the vessel
                        }
                        else
                        {
                            string accesstype;
                            if(SyntaxPlayerVessel.GetAccessibility(client.activeVessel,out accesstype))
                            {
                                if(accesstype.ToString() == "public")
                                {
                                    // give control over the vessel
                                }
                                else if(accesstype.ToString() == "spectate")
                                {
                                    // give spectate control only
                                }
                                else
                                {
                                    // Block control and spectate
                                }
                            }
                        }
                    }
                }

            }
        }
    }
}
