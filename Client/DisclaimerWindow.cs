using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    //This disclaimer exists because I was contacted by a moderator pointing me to the addon posting rules.
    public class DisclaimerWindow
    {
        public static DisclaimerWindow singleton;
        private const int WINDOW_WIDTH = 500;
        private const int WINDOW_HEIGHT = 300;
        private Rect windowRect;
        private Rect moveRect;
        private bool initialized;
        private bool display;
        private GUILayoutOption[] layoutOptions;

        public static DisclaimerWindow fetch
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
                windowRect = GUILayout.Window(GUIUtility.GetControlID(6713 + Client.WINDOW_OFFSET, FocusType.Passive), windowRect, DrawContent, "DarkMultiPlayer - Disclaimer", layoutOptions);
            }
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            string disclaimerText = "DarkMultiPlayer shares the following possibly personally identifiable information with any server you connect to.\n";
            disclaimerText += "a) Your player name you connect with.\n";
            disclaimerText += "b) Your player token (A randomly generated string to authenticate you).\n";
            disclaimerText += "c) Your IP address is logged on the server console.\n";
            disclaimerText += "\n";
            disclaimerText += "DMP does not contact any other computer than the server you are connecting to.\n";
            disclaimerText += "In order to use DarkMultiPlayer, you must allow DMP to use this info\n";
            disclaimerText += "\n";
            disclaimerText += "For more information - see the KSP addon rules\n";
            GUILayout.Label(disclaimerText);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open the KSP Addon rules in the browser"))
            {
                Application.OpenURL("http://forum.kerbalspaceprogram.com/threads/87841-Add-on-Posting-Rules-July-24th-2014-going-into-effect-August-21st-2014!");
            }
            if (GUILayout.Button("I accept - Enable DarkMultiPlayer"))
            {
                DarkLog.Debug("User accepted disclaimer - Enabling DarkMultiPlayer");
                display = false;
                Settings.fetch.disclaimerAccepted = 1;
                Client.fetch.modDisabled = false;
                Settings.fetch.SaveSettings();
            }
            if (GUILayout.Button("I decline - Disable DarkMultiPlayer"))
            {
                DarkLog.Debug("User declined disclaimer - Disabling DarkMultiPlayer");
                display = false;
            }
            GUILayout.EndVertical();
        }

        public static void Enable()
        {
            singleton = new DisclaimerWindow();
            lock (Client.eventLock) {
                Client.drawEvent.Add(singleton.Draw);
            }
            singleton.display = true;
        }
    }
}

