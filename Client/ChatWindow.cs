using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class ChatWindow
    {
        public bool display;
        private bool sendEventHandled;
        private bool initialized;
        private bool chatLocked;
        private List<string> chatEntries;
        private Queue<string> newChatEntries;
        private string sendText;
        private Client parent;
        //GUI Layout
        private Rect windowRect;
        private Rect moveRect;
        private GUILayoutOption[] layoutOptions;
        private GUILayoutOption[] textAreaOptions;
        private GUIStyle windowStyle;
        private GUIStyle labelStyle;
        private GUIStyle scrollStyle;
        private Vector2 scrollPos;
        //const
        private const float WINDOW_HEIGHT = 200;
        private const float WINDOW_WIDTH = 300;
        private const string DMP_CHAT_LOCK = "DMP_ChatLock";
        public const ControlTypes BLOCK_ALL_CONTROLS = ControlTypes.ALL_SHIP_CONTROLS | ControlTypes.ACTIONS_ALL | ControlTypes.EVA_INPUT | ControlTypes.TIMEWARP | ControlTypes.MISC | ControlTypes.GROUPS_ALL | ControlTypes.CUSTOM_ACTION_GROUPS;

        public ChatWindow(Client parent)
        {
            //Main setup
            display = false;
            this.parent = parent;
            Reset();
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width / 10, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.MinWidth(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.MaxWidth(WINDOW_WIDTH);
            layoutOptions[2] = GUILayout.MinHeight(WINDOW_HEIGHT);
            layoutOptions[3] = GUILayout.MaxHeight(WINDOW_HEIGHT);

            windowStyle = new GUIStyle(GUI.skin.window);
            scrollStyle = new GUIStyle(GUI.skin.scrollView);

            textAreaOptions = new GUILayoutOption[1];
            textAreaOptions[0] = GUILayout.ExpandWidth(true);

            scrollPos = new Vector2(0, 0);
            labelStyle = new GUIStyle(GUI.skin.label);

            sendText = "";
        }

        public void Draw()
        {
            if (display)
            {
                if (!initialized)
                {
                    initialized = true;
                    InitGUI();
                }
                windowRect = GUILayout.Window(GUIUtility.GetControlID(6704, FocusType.Passive), windowRect, DrawContent, "DarkMultiPlayer Chat", windowStyle, layoutOptions);
            }
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            scrollPos = GUILayout.BeginScrollView(scrollPos, scrollStyle);
            foreach (string chatEntry in chatEntries)
            {
                GUILayout.Label(chatEntry, labelStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("SendTextButton");
            sendText = GUILayout.TextArea(sendText, textAreaOptions);
            GUI.enabled = (sendText != "");
            if (GUILayout.Button("Send"))
            {
                sendEventHandled = false;
                GUIUtility.keyboardControl = 0;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            if ((GUI.GetNameOfFocusedControl() == "SendTextButton") && !chatLocked)
            {
                chatLocked = true;
                InputLockManager.SetControlLock(BLOCK_ALL_CONTROLS, DMP_CHAT_LOCK);
            }
            if ((GUI.GetNameOfFocusedControl() != "SendTextButton") && chatLocked)
            {
                chatLocked = false;
                InputLockManager.RemoveControlLock(DMP_CHAT_LOCK);
            }
        }

        public void QueueChatEntry(string playerName, string chatText)
        {
            newChatEntries.Enqueue(playerName + ": " + chatText);
        }

        public void Update()
        {
            while (newChatEntries.Count > 0)
            {
                chatEntries.Add(newChatEntries.Dequeue());
            }
            if (!sendEventHandled)
            {
                parent.networkWorker.SendChatMessage(sendText);
                sendText = "";
                sendEventHandled = true;
            }
        }

        public void Reset()
        {
            sendEventHandled = true;
            chatEntries = new List<string>();
            newChatEntries = new Queue<string>();
        }
    }
}

