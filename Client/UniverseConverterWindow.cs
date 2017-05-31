using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class UniverseConverterWindow
    {
        public bool loadEventHandled = true;
        public bool display;
        private bool safeDisplay;
        private bool initialized;
        string[] saveDirectories = UniverseConverter.GetSavedNames();
        //GUI Layout
        private Rect windowRect;
        private Rect moveRect;
        private Vector2 scrollPos;
        private GUILayoutOption[] layoutOptions;
        //Styles
        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle scrollStyle;
        //const
        private const float WINDOW_HEIGHT = 300;
        private const float WINDOW_WIDTH = 200;
        //Services
        private UniverseConverter universeConverter;

        public UniverseConverterWindow(UniverseConverter universeConverter)
        {
            this.universeConverter = universeConverter;
        }

        private void InitGUI()
        {
            //Setup GUI stuff
            windowRect = new Rect(Screen.width / 4f - WINDOW_WIDTH / 2f, Screen.height / 2f - WINDOW_HEIGHT / 2f, WINDOW_WIDTH, WINDOW_HEIGHT);
            moveRect = new Rect(0, 0, 10000, 20);

            windowStyle = new GUIStyle(GUI.skin.window);
            buttonStyle = new GUIStyle(GUI.skin.button);
            scrollStyle = new GUIStyle(GUI.skin.scrollView);

            layoutOptions = new GUILayoutOption[4];
            layoutOptions[0] = GUILayout.Width(WINDOW_WIDTH);
            layoutOptions[1] = GUILayout.Height(WINDOW_HEIGHT);
            layoutOptions[2] = GUILayout.ExpandWidth(true);
            layoutOptions[3] = GUILayout.ExpandHeight(true);
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
                windowRect = DMPGuiUtil.PreventOffscreenWindow(GUILayout.Window(6712 + Client.WINDOW_OFFSET, windowRect, DrawContent, "Universe Converter", windowStyle, layoutOptions));
            }
        }

        private void DrawContent(int windowID)
        {
            GUI.DragWindow(moveRect);
            GUILayout.BeginVertical();
            scrollPos = GUILayout.BeginScrollView(scrollPos, scrollStyle);
            foreach (string saveFolder in saveDirectories)
            {
                if (GUILayout.Button(saveFolder))
                {
                    universeConverter.GenerateUniverse(saveFolder);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", buttonStyle))
            {
                display = false;
            }
            GUILayout.EndVertical();
        }
    }
}

