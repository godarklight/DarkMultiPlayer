using System;
using System.IO;
using UnityEngine;
using System.Reflection;

namespace DarkMultiPlayer.Utilities
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class InstallChecker : MonoBehaviour
    {
        private static string currentPath
        {
            get
            {
                return new DirectoryInfo(Assembly.GetExecutingAssembly().Location).FullName;
            }
        }

        private static string correctPath
        {
            get
            {
                string kspPath = new DirectoryInfo(KSPUtil.ApplicationRootPath).FullName;
                return Path.Combine(Path.Combine(Path.Combine(Path.Combine(kspPath, "GameData"), "DarkMultiPlayer"), "Plugins"), "DarkMultiPlayer.dll");
            }
        }

        public static bool IsCorrectlyInstalled()
        {
#if DEBUG
            return true;
#endif
#if !DEBUG
            return (currentPath == correctPath);
#endif
        }

        public void Start()
        {
            Debug.Log(String.Format("[InstallChecker] Running checker from '{0}'", Assembly.GetExecutingAssembly().GetName().Name));

            if (!IsCorrectlyInstalled())
            {

                string displayCurrentPath = currentPath;
                string displayCorrectPath = correctPath;
                int indexOfGameDataCurrent = displayCurrentPath.IndexOf("gamedata", StringComparison.OrdinalIgnoreCase);
                int indexOfGameDataCorrect = displayCorrectPath.IndexOf("gamedata", StringComparison.OrdinalIgnoreCase);
                if (indexOfGameDataCorrect != -1 && indexOfGameDataCorrect == indexOfGameDataCurrent)
                {
                    displayCorrectPath = displayCorrectPath.Substring(indexOfGameDataCorrect);
                    displayCurrentPath = displayCurrentPath.Substring(indexOfGameDataCurrent);
                }
                string message = string.Format("DarkMultiPlayer is not correctly installed.\n\nCurrent location: {0}\n\nCorrect location: {1}\n", displayCurrentPath, displayCorrectPath);
                Debug.Log(String.Format("[InstallChecker] Mod '{0}' is not correctly installed.", Assembly.GetExecutingAssembly().GetName().Name));
                Debug.Log(String.Format("[InstallChecker] DMP is Currently installed at '{0}', should be installed at '{1}'", displayCurrentPath, displayCorrectPath));
                PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "InstallChecker", "Incorrect Install Detected", message, "OK", true, HighLogic.UISkin);
            }
        }
    }
}

