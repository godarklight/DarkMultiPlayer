using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    //This windows is added, to kill the time between forced-KSP and DMP updates.
    public class IgnoreCompatibilityWindow
    {
        public static IgnoreCompatibilityWindow singleton;
        private const int WINDOW_WIDTH = 500;
        private const int WINDOW_HEIGHT = 300;
        private Rect windowRect;
        private Rect moveRect;
        private bool initialized;
        private bool display;
        private GUILayoutOption[] layoutOptions;

        public static IgnoreCompatibilityWindow fetch
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
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6713 + Client.WINDOW_OFFSET, windowRect, DrawContent, "DarkMultiPlayer - Compatibility", layoutOptions));
            }
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(moveRect);
            string compatibilityText = "This Version of DarkMultiPlayer is compatible to\n";
            compatibilityText += "\tKerbal Space Program Version: " + Utilities.CompatibilityChecker.getCompatibleText() + "\n";
            compatibilityText += "\tYour Version: " + Versioning.version_major + "." + Versioning.version_minor + "." + Versioning.Revision + "\n";
            compatibilityText += "\n";
            compatibilityText += "Please try to update to latest Version with 'DMPUpdater.exe'.\n";
            compatibilityText += "\t(or 'DMPUpdater-development.exe')\n";
            compatibilityText += "\n";
            compatibilityText += "Enable DarkMultiPlayer anyway?\n";
            compatibilityText += "\t(Not recommended!)\n";
            GUILayout.Label(compatibilityText);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open the KSP Addon rules in the browser"))
            {
                Application.OpenURL("http://forum.kerbalspaceprogram.com/threads/87841-Add-on-Posting-Rules-July-24th-2014-going-into-effect-August-21st-2014!");
            }
            if (GUILayout.Button("Yes (Force) - Enable DarkMultiPlayer"))
            {
                DarkLog.Debug("User ignored compatibility - Enabling DarkMultiPlayer");
                display = false;
                Client.fetch.modDisabled = false;
            }
            if (GUILayout.Button("No (recommended) - Disable DarkMultiPlayer"))
            {
                DarkLog.Debug("User accepted incompatibility - Disabling DarkMultiPlayer");
                display = false;
            }
            GUILayout.EndVertical();
        }

        public static void Enable()
        {
            singleton = new IgnoreCompatibilityWindow();
            lock (Client.eventLock) {
                Client.drawEvent.Add(singleton.Draw);
            }
            singleton.display = true;
        }
    }
}

