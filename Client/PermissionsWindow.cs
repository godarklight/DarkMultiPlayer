using System;
using DarkMultiPlayerCommon;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class PermissionsWindow
    {
        public bool display = false;
        //Services
        private DMPGame dmpGame;
        private Settings dmpSettings;
        private Permissions permissions;
        private Groups groups;
        private LockSystem lockSystem;
        private PlayerStatusWorker playerStatusWorker;
        private bool isWindowLocked = false;
        //private parts
        private bool initialized;
        //GUI Layout
        private Rect windowRect;
        private Rect moveRect;
        private GUILayoutOption[] layoutOptions;
        //Styles
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        //const
        private const float WINDOW_HEIGHT = 300;
        private const float WINDOW_WIDTH = 400;
        private NamedAction drawAction;

        public PermissionsWindow(DMPGame dmpGame, Settings dmpSettings, Groups groups, Permissions permissions, LockSystem lockSystem)
        {
            this.dmpGame = dmpGame;
            this.dmpSettings = dmpSettings;
            this.groups = groups;
            this.permissions = permissions;
            this.lockSystem = lockSystem;
            drawAction = new NamedAction(Draw);
            dmpGame.drawEvent.Add(drawAction);
        }

        public void SetDependencies(PlayerStatusWorker playerStatusWorker)
        {
            this.playerStatusWorker = playerStatusWorker;
        }

        public void Stop()
        {
            dmpGame.drawEvent.Remove(drawAction);
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width * 0.5f - WINDOW_WIDTH, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);

            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.MinWidth(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.MaxWidth(WINDOW_WIDTH);
            layoutOptions[2] = GUILayout.MinHeight(WINDOW_HEIGHT);
            layoutOptions[3] = GUILayout.MaxHeight(WINDOW_HEIGHT);
        }

        private void Draw()
        {
            if (display)
            {
                if (!initialized)
                {
                    initialized = true;
                    InitGUI();
                }
                Vector2 mousePos = Input.mousePosition;
                mousePos.y = Screen.height - mousePos.y;
                bool shouldLock = windowRect.Contains(mousePos);

                if (shouldLock && !isWindowLocked)
                {
                    InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, "DMP_PermissionsWindowLock");
                    isWindowLocked = true;
                }
                if (!shouldLock && isWindowLocked)
                {
                    RemoveWindowLock();
                }
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6714 + Client.WINDOW_OFFSET, windowRect, DrawContent, "DarkMultiPlayer - Permissions", windowStyle, layoutOptions));
            }
        }

        private void RemoveWindowLock()
        {
            if (isWindowLocked)
            {
                isWindowLocked = false;
                InputLockManager.RemoveControlLock("DMP_PermissionsWindowLock");
            }
        }

        private void DrawContent(int windowID)
        {
            GUI.DragWindow(moveRect);
            GUILayout.BeginVertical();
            Guid vesselID = Guid.Empty;
            if (FlightGlobals.fetch != null && FlightGlobals.fetch.activeVessel != null)
            {
                vesselID = FlightGlobals.fetch.activeVessel.id;
            }
            if (FlightGlobals.fetch != null && FlightGlobals.fetch.activeVessel != null && permissions.vesselPermissions.ContainsKey(vesselID) && lockSystem.LockIsOurs("control-" + vesselID))
            {
                VesselPermission vesselPermission = permissions.vesselPermissions[vesselID];
                if (vesselPermission.owner == dmpSettings.playerName)
                {
                    GUILayout.BeginHorizontal();
                    if (vesselPermission.protection != VesselProtectionType.PUBLIC)
                    {
                        if (GUILayout.Button("Set to public"))
                        {
                            permissions.SetVesselProtection(vesselID, VesselProtectionType.PUBLIC);
                        }
                    }
                    if (vesselPermission.protection != VesselProtectionType.GROUP)
                    {
                        if (GUILayout.Button("Set to group"))
                        {
                            permissions.SetVesselProtection(vesselID, VesselProtectionType.GROUP);
                        }
                    }
                    if (vesselPermission.protection != VesselProtectionType.PRIVATE)
                    {
                        if (GUILayout.Button("Set to private"))
                        {
                            permissions.SetVesselProtection(vesselID, VesselProtectionType.PRIVATE);
                        }
                    }
                    GUILayout.EndHorizontal();
                    if (vesselPermission.protection == VesselProtectionType.GROUP)
                    {
                        if (groups.playerGroups.ContainsKey(dmpSettings.playerName))
                        {
                            foreach (string group in groups.playerGroups[dmpSettings.playerName])
                            {
                                if (GUILayout.Button("Set to group: " + group))
                                {
                                    permissions.SetVesselGroup(vesselID, group);
                                }
                            }
                        }
                    }
                    GUILayout.Space(20);
                    foreach (PlayerStatus playerStatus in playerStatusWorker.playerStatusList)
                    {
                        if (playerStatus.playerName != dmpSettings.playerName)
                        {
                            if (GUILayout.Button("Give owner to: " + playerStatus.playerName))
                            {
                                permissions.SetVesselOwner(vesselID, playerStatus.playerName);
                            }
                        }
                    }
                }
                else
                {
                    GUILayout.Label("Not vessel owner, belongs to: " + vesselPermission.owner);
                }
            }
            else
            {
                GUILayout.Label("Not flying vessel");
            }
            GUILayout.EndVertical();
        }
    }
}
