using System;
using System.IO;
using UnityEngine;
using System.Reflection;

namespace DarkMultiPlayer.Utilities
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class InstallChecker : MonoBehaviour
    {
        private static string currentPath = "";
        private static string correctPath = "";

        public static bool IsCorrectlyInstalled()
        {
            string assemblyInstalledAt = new DirectoryInfo(Assembly.GetExecutingAssembly().Location).FullName;
            string kspPath = new DirectoryInfo(KSPUtil.ApplicationRootPath).FullName;
            string shouldBeInstalledAt = Path.Combine(Path.Combine(Path.Combine(Path.Combine(kspPath, "GameData"), "DarkMultiPlayer"), "Plugins"), "DarkMultiPlayer.dll");

            currentPath = assemblyInstalledAt;
            correctPath = shouldBeInstalledAt;

            if (File.Exists(shouldBeInstalledAt))
            {
                return true;
            }
            return (assemblyInstalledAt == shouldBeInstalledAt);
        }

        public void Awake()
        {
            Debug.Log(String.Format("[InstallChecker] Running checker from '{0}'", Assembly.GetExecutingAssembly().GetName().Name));

            if (!IsCorrectlyInstalled())
            {
                Debug.Log(String.Format("[InstallChecker] Mod '{0}' is not correctly installed.", Assembly.GetExecutingAssembly().GetName().Name));
                Debug.Log(String.Format("[InstallChecker] DMP is Currently installed on '{0}', should be installed at '{1}'", currentPath, correctPath));
                PopupDialog.SpawnPopupDialog(new Vector2(0,0), new Vector2(0, 0), "Incorrect Install Detected", String.Format("DarkMultiPlayer is not correctly installed.\n\nCurrent location: {0}\n\nCorrect location: {1}\n", currentPath, correctPath), "OK", false, HighLogic.UISkin);
            }
        }
    }
}

