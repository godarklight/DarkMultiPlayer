using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    // This disclaimer exists because I was contacted by a moderator pointing me to the addon posting rules.
    public class ServerListDisclaimerWindow
    {
        private Settings dmpSettings;

        public ServerListDisclaimerWindow(Settings dmpSettings)
        {
            this.dmpSettings = dmpSettings;
        }

        public void SpawnDialog()
        {
            string disclaimerText = "To use the in-game server list, you must allow DarkMultiPlayer to connect to the server list network\n\n";
            disclaimerText += "No data is sent to the server network\n\n";
            disclaimerText += "You may change this setting at any time by clicking Reset Serverlist Disclaimer in Options -> Advanced\n";
            disclaimerText += "\n";

            PopupDialog.SpawnPopupDialog(
                new MultiOptionDialog("ServerListDisclaimerWindow", disclaimerText,
                    "DarkMultiPlayer Server List Disclaimer",
                    HighLogic.UISkin,
                    new Rect(.5f, .5f, 425f, 150f),
                    new DialogGUIVerticalLayout(
                        new DialogGUIFlexibleSpace(),
                            new DialogGUIButton("Prevent serverlist connection and hide the button",
                                delegate
                                {
                                    DarkLog.Debug("User rejected servers disclaimer, disabling button");
                                    dmpSettings.serverlistMode = -1;
                                    dmpSettings.SaveSettings();
                                }
                            , 400f, 30f, true),
                            new DialogGUIButton("Connect when opening the serverlist",
                                delegate
                                {
                                    DarkLog.Debug("User accepted servers disclaimer, connect when clicked");
                                    dmpSettings.serverlistMode = 1;
                                    dmpSettings.SaveSettings();
                                }
                            , 400f, 30f, true),
                            new DialogGUIButton("Connect when the main menu shows",
                                delegate
                                {
                                    DarkLog.Debug("User accepted servers disclaimer, connect on startup");
                                    dmpSettings.serverlistMode = 2;
                                    dmpSettings.SaveSettings();
                                }
                            , 400f, 30f, true)
                       )
                ),
                true,
                HighLogic.UISkin
            );
        }
    }
}


