using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class ModWindow
    {
        public bool display;
        private bool safeDisplay;
        private bool initialized;
        private Client parent;
        private Rect windowRect;
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;
        private GUIStyle scrollStyle;
        private GUILayoutOption[] layoutOptions;
        private Vector2 scrollPos;
        //const
        private const float WINDOW_HEIGHT = 400;
        private const float WINDOW_WIDTH = 600;

        public ModWindow(Client parent)
        {
            this.parent = parent;
            if (this.parent != null)
            {
                //Shutup compiler
            }
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(((Screen.width / 2f) - (WINDOW_WIDTH / 2f)), ((Screen.height / 4f) - (WINDOW_HEIGHT / 2f)), WINDOW_WIDTH, WINDOW_HEIGHT);

            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);
            labelStyle = new GUIStyle(GUI.skin.label);
            scrollStyle = new GUIStyle(GUI.skin.scrollView);

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.MinWidth(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.MaxWidth(WINDOW_WIDTH);
            layoutOptions[2] = GUILayout.MinHeight(WINDOW_HEIGHT);
            layoutOptions[3] = GUILayout.MaxHeight(WINDOW_HEIGHT);

            scrollPos = new Vector2();
        }

        public void Update()
        {
            safeDisplay = display;
        }

        public void Draw()
        {
            if (!initialized)
            {
                initialized = true;
                InitGUI();
            }
            if (safeDisplay)
            {
                GUILayout.Window(GUIUtility.GetControlID(6706, FocusType.Passive), windowRect, DrawContent, "DarkMultiPlayer - Mod Control", windowStyle, layoutOptions);
            }
        }

        private void DrawContent(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Failed mod validation", labelStyle);
            scrollPos = GUILayout.BeginScrollView(scrollPos, scrollStyle);
            GUILayout.Label(parent.modWorker.failText, labelStyle);
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close", buttonStyle))
            {
                display = false;
            }
            GUILayout.EndVertical();
        }
    }
}

