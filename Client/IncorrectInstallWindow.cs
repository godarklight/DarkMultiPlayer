using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class IncorrectInstallWindow
    {
        public static IncorrectInstallWindow singleton;

        private const int WINDOW_WIDTH = 600;
        private const int WINDOW_HEIGHT = 200;
        private Rect windowRect;
        private Rect moveRect;
        private bool initialized;
        private bool display;
        private GUILayoutOption[] layoutOptions;


        public static IncorrectInstallWindow fetch
        {
            get
            {
                return singleton;
            }
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect((Screen.width / 2f) - (WINDOW_WIDTH / 2), (Screen.height / 2f) - (WINDOW_HEIGHT / 2f), WINDOW_WIDTH, WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);

            layoutOptions = new GUILayoutOption[2];
            layoutOptions[0] = GUILayout.ExpandWidth(true);
            layoutOptions[1] = GUILayout.ExpandHeight(true);
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
                windowRect = GUILayout.Window(GUIUtility.GetControlID(6705, FocusType.Passive), windowRect, DrawContent, "DarkMultiPlayer", layoutOptions);
            }
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            GUILayout.Label("DMP is not correctly installed");
            GUILayout.Label("Current location: " + Client.fetch.assemblyPath);
            GUILayout.Label("Correct location: " + Client.fetch.assemblyShouldBeInstalledAt);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close"))
            {
                display = false;
            }
            GUILayout.EndVertical();
        }

        public static void Enable()
        {
            singleton = new IncorrectInstallWindow();
            lock (Client.eventLock) {
                Client.drawEvent.Add(singleton.Draw);
            }
            singleton.display = true;
        }
    }
}

