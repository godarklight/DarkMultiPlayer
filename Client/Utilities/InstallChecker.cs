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

            if (File.Exists(correctPath))
            {
                return true;
            }
            return (currentPath == correctPath);
        }

        public void Start()
        {
            Debug.Log(String.Format("[InstallChecker] Running checker from '{0}'", Assembly.GetExecutingAssembly().GetName().Name));

            if (!IsCorrectlyInstalled())
            {
                Debug.Log(String.Format("[InstallChecker] Mod '{0}' is not correctly installed.", Assembly.GetExecutingAssembly().GetName().Name));
                Debug.Log(String.Format("[InstallChecker] DMP is Currently installed on '{0}', should be installed at '{1}'", currentPath, correctPath));
                PopupDialog.SpawnPopupDialog(new MultiOptionDialog(String.Format("DarkMultiPlayer is not correctly installed.\n\nCurrent location: {0}\n\nCorrect location: {1}\n", currentPath, correctPath), "Incorrect Install Detected", HighLogic.UISkin), true, HighLogic.UISkin);
            }
        }
    }
}

