using System;
using System.Collections.Generic;
using DarkMultiPlayerCommon;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class GroupsWindow
    {
        public bool display = false;
        //Services
        private DMPGame dmpGame;
        private Settings dmpSettings;
        private Groups groups;
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
        private const float WINDOW_HEIGHT = 400;
        private const float WINDOW_WIDTH = 300;

        public GroupsWindow(DMPGame dmpGame, Settings dmpSettings, Groups groups)
        {
            this.dmpGame = dmpGame;
            this.dmpSettings = dmpSettings;
            this.groups = groups;
            dmpGame.drawEvent.Add(Draw);
        }

        public void SetDependencies(PlayerStatusWorker playerStatusWorker)
        {
            this.playerStatusWorker = playerStatusWorker;
        }

        public void Stop()
        {
            dmpGame.drawEvent.Remove(Draw);
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width * 0.7f - WINDOW_WIDTH, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
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
            if (!initialized)
            {
                initialized = true;
                InitGUI();
            }
            if (display)
            {
                Vector2 mousePos = Input.mousePosition;
                mousePos.y = Screen.height - mousePos.y;
                bool shouldLock = windowRect.Contains(mousePos);

                if (shouldLock && !isWindowLocked)
                {
                    InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, "DMP_GroupWindowLock");
                    isWindowLocked = true;
                }
                if (!shouldLock && isWindowLocked)
                {
                    RemoveWindowLock();
                }
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6713 + Client.WINDOW_OFFSET, windowRect, DrawContent, "DarkMultiPlayer - Groups", windowStyle, layoutOptions));
            }
        }

        private void RemoveWindowLock()
        {
            if (isWindowLocked)
            {
                isWindowLocked = false;
                InputLockManager.RemoveControlLock("DMP_GroupWindowLock");
            }
        }

        private string tempGroupName = "";
        private string editGroup = "";
        private void DrawContent(int windowID)
        {
            GUI.DragWindow(moveRect);
            GUILayout.BeginVertical();
            GUILayout.Label("Create");
            GUILayout.BeginHorizontal();
            tempGroupName = GUILayout.TextArea(tempGroupName, GUILayout.ExpandWidth(true));
            if (tempGroupName.StartsWith(".", StringComparison.Ordinal))
            {
                tempGroupName = "";
            }
            if (GUILayout.Button("Create group", GUILayout.ExpandWidth(false)))
            {
                groups.AddPlayerToGroup(dmpSettings.playerName, tempGroupName);
                tempGroupName = "";
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(20);
            if (groups.playerGroups.ContainsKey(dmpSettings.playerName))
            {
                GUILayout.Label("Edit");
                foreach (string group in groups.playerGroups[dmpSettings.playerName])
                {
                    if (editGroup == group)
                    {
                        if (!GUILayout.Toggle(true, group, buttonStyle))
                        {
                            editGroup = "";
                        }
                    }
                    else
                    {
                        if (GUILayout.Toggle(false, group, buttonStyle))
                        {
                            editGroup = group;
                        }
                    }
                }
            }
            if (editGroup != "")
            {
                if (GUILayout.Button("Leave group"))
                {
                    groups.RemovePlayerFromGroup(dmpSettings.playerName, editGroup);
                    editGroup = "";
                }
                if (groups.PlayerIsAdmin(dmpSettings.playerName, editGroup))
                {
                    if (GUILayout.Button("Disband group"))
                    {
                        groups.DeleteGroup(editGroup);
                        editGroup = "";
                    }
                }
                if (groups.PlayerIsAdmin(dmpSettings.playerName, editGroup))
                {
                    foreach (PlayerStatus player in playerStatusWorker.playerStatusList)
                    {
                        if (player.playerName != dmpSettings.playerName)
                        {
                            if (!groups.PlayerInGroup(player.playerName, editGroup))
                            {
                                if (GUILayout.Button("Add " + player.playerName))
                                {
                                    groups.AddPlayerToGroup(player.playerName, editGroup);
                                }
                            }
                            else
                            {
                                if (GUILayout.Button("Remove " + player.playerName))
                                {
                                    groups.RemovePlayerFromGroup(player.playerName, editGroup);
                                }
                                if (!groups.PlayerIsAdmin(player.playerName, editGroup))
                                {
                                    if (GUILayout.Button("Make  " + player.playerName + " admin"))
                                    {
                                        groups.AddPlayerAdmin(player.playerName, editGroup);
                                    }
                                }
                                else
                                {
                                    if (GUILayout.Button("Remove admin " + player.playerName))
                                    {
                                        groups.RemovePlayerAdmin(player.playerName, editGroup);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            GUILayout.EndVertical();
        }
    }
}
