using System;
using KSP.UI.Screens;
using UnityEngine;

namespace DarkMultiPlayer
{
    public class ToolbarSupport
    {
        //State
        private bool registered;
        private bool stockDelayRegister;
        private bool blizzyRegistered;
        private bool stockRegistered;
        private Texture2D buttonTexture;
        private ApplicationLauncherButton stockDmpButton;
        private IButton blizzyButton;
        //Services
        private Settings dmpSettings;

        public ToolbarSupport(Settings dmpSettings)
        {
            this.dmpSettings = dmpSettings;
        }

        public void DetectSettingsChange()
        {
            if (registered)
            {
                DisableToolbar();
                EnableToolbar();
            }
        }

        public void EnableToolbar()
        {
            buttonTexture = GameDatabase.Instance.GetTexture("DarkMultiPlayer/Button/DMPButton", false);
            if (registered)
            {
                DarkLog.Debug("Cannot re-register toolbar");
                return;
            }
            registered = true;
            if (dmpSettings.toolbarType == DMPToolbarType.DISABLED)
            {
                //Nothing!
            }
            if (dmpSettings.toolbarType == DMPToolbarType.FORCE_STOCK)
            {
                EnableStockToolbar();
            }
            if (dmpSettings.toolbarType == DMPToolbarType.BLIZZY_IF_INSTALLED)
            {
                if (ToolbarManager.ToolbarAvailable)
                {
                    EnableBlizzyToolbar();
                }
                else
                {
                    EnableStockToolbar();
                }
            }
            if (dmpSettings.toolbarType == DMPToolbarType.BOTH_IF_INSTALLED)
            {
                if (ToolbarManager.ToolbarAvailable)
                {
                    EnableBlizzyToolbar();
                }
                EnableStockToolbar();
            }
        }

        public void DisableToolbar()
        {
            registered = false;
            if (blizzyRegistered)
            {
                DisableBlizzyToolbar();
            }
            if (stockRegistered)
            {
                DisableStockToolbar();
            }
        }

        private void EnableBlizzyToolbar()
        {
            blizzyRegistered = true;
            blizzyButton = ToolbarManager.Instance.add("DarkMultiPlayer", "GUIButton");
            blizzyButton.OnClick += OnBlizzyClick;
            blizzyButton.ToolTip = "Toggle DMP windows";
            blizzyButton.TexturePath = "DarkMultiPlayer/Button/DMPButtonLow";
            blizzyButton.Visibility = new GameScenesVisibility(GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION);
            DarkLog.Debug("Registered blizzy toolbar");
        }

        private void DisableBlizzyToolbar()
        {
            blizzyRegistered = false;
            if (blizzyButton != null)
            {
                blizzyButton.Destroy();
            }
            DarkLog.Debug("Unregistered blizzy toolbar");
        }

        private void EnableStockToolbar()
        {
            stockRegistered = true;
            if (ApplicationLauncher.Ready)
            {
                EnableStockForRealsies();
            }
            else
            {
                stockDelayRegister = true;
                GameEvents.onGUIApplicationLauncherReady.Add(EnableStockForRealsies);
            }
            DarkLog.Debug("Registered stock toolbar");
        }

        private void EnableStockForRealsies()
        {
            if (stockDelayRegister)
            {
                stockDelayRegister = false;
                GameEvents.onGUIApplicationLauncherReady.Remove(EnableStockForRealsies);
            }
            stockDmpButton = ApplicationLauncher.Instance.AddModApplication(HandleButtonClick, HandleButtonClick, DoNothing, DoNothing, DoNothing, DoNothing, ApplicationLauncher.AppScenes.ALWAYS, buttonTexture);
        }

        private void DisableStockToolbar()
        {
            stockRegistered = false;
            if (stockDelayRegister)
            {
                stockDelayRegister = false;
                GameEvents.onGUIApplicationLauncherReady.Remove(EnableStockForRealsies);
            }
            if (stockDmpButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(stockDmpButton);
            }
            DarkLog.Debug("Unregistered stock toolbar");
        }

        private void OnBlizzyClick(ClickEvent clickArgs)
        {
            HandleButtonClick();
        }

        private void HandleButtonClick()
        {
            Client.toolbarShowGUI = !Client.toolbarShowGUI;
        }

        private void DoNothing()
        {
        }

        public  void Stop()
        {
            DisableToolbar();
        }
    }

    public enum DMPToolbarType
    {
        DISABLED,
        FORCE_STOCK,
        BLIZZY_IF_INSTALLED,
        BOTH_IF_INSTALLED
    }
}

