using System;
using UnityEngine;

namespace DarkMultiPlayer
{
    // This disclaimer exists because I was contacted by a moderator pointing me to the addon posting rules.
    public class DisclaimerWindow
    {
        private Settings dmpSettings;

        public DisclaimerWindow(Settings dmpSettings)
        {
            this.dmpSettings = dmpSettings;
        }

        public void SpawnDialog()
        {
            string disclaimerText = "DarkMultiPlayer shares the following possibly personally identifiable information with any server you connect to:\n";
            disclaimerText += "\ta) Your player name you connect with\n";
            disclaimerText += "\tb) Your player token (a randomly generated string to authenticate you)\n";
            disclaimerText += "\tc) Your IP address\n";
            disclaimerText += "\n";
            disclaimerText += "DarkMultiPlayer does not contact any other computer than the server you are connecting to.\n";
            disclaimerText += "In order to use DarkMultiPlayer, you must allow the mod to use this information.\n";
            disclaimerText += "\n";
            disclaimerText += "For more information, read the KSP addon rules on the forums.\n";

            PopupDialog.SpawnPopupDialog(
                new MultiOptionDialog("DisclaimerWindow", disclaimerText,
                    "DarkMultiPlayer - Disclaimer",
                    HighLogic.UISkin,
                    new Rect(.5f, .5f, 425f, 150f),
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIVerticalLayout(
                        new DialogGUIHorizontalLayout(
                            new DialogGUIButton("Accept",
                                delegate
                                {
                                    DarkLog.Debug("User accepted disclaimer, enabling DarkMultiPlayer");
                                    dmpSettings.disclaimerAccepted = 1;
                                    dmpSettings.SaveSettings();
                                    Client.modDisabled = false;
                                }
                            ),
                            new DialogGUIFlexibleSpace(),
                            new DialogGUIButton("Open the KSP Addon rules in browser",
                                delegate
                                {
                                    Application.OpenURL("http://forum.kerbalspaceprogram.com/index.php?/topic/154851-add-on-posting-rules-march-8-2017/");
                                }
                            , false),
                            new DialogGUIFlexibleSpace(),
                            new DialogGUIButton("Decline",
                                delegate
                                {
                                    DarkLog.Debug("User declined disclaimer, disabling DarkMultiPlayer");
                                }
                            )
                        )
                    )
                ),
                true,
                HighLogic.UISkin
            );
        }
    }
}

