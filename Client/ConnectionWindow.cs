using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class ConnectionWindow
    {
        private bool buttonStatus;
        public bool eventHandled = true;

        public void Draw() {
            GUILayout.BeginVertical();
            GUIStyle injectorButtonStyle = new GUIStyle(GUI.skin.button);
            injectorButtonStyle.fontSize = 10;
            bool newButtonStatus = GUILayout.Button("Connect", injectorButtonStyle, null);
            if (!buttonStatus && newButtonStatus)
            {
                eventHandled = false;
            }
            buttonStatus = newButtonStatus;
            GUILayout.EndVertical();
        }
    }
}

