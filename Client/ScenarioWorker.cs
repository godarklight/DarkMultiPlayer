using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayer
{
    public class ScenarioWorker
    {
        public bool workerEnabled = false;
        private static ScenarioWorker singleton;
        private Queue<ScenarioEntry> scenarioQueue = new Queue<ScenarioEntry>();
        private bool blockScenarioDataSends = false;
        private float lastScenarioSendTime = 0f;
        private const float SEND_SCENARIO_DATA_INTERVAL = 30f;

        public static ScenarioWorker fetch
        {
            get
            {
                return singleton;
            }
        }

        private void Update()
        {
            if (workerEnabled && !blockScenarioDataSends)
            {
                if ((UnityEngine.Time.realtimeSinceStartup - lastScenarioSendTime) > SEND_SCENARIO_DATA_INTERVAL)
                {
                    lastScenarioSendTime = UnityEngine.Time.realtimeSinceStartup;
                    SendScenarioModules();
                }
            }
        }

        private bool IsScenarioModuleAllowed(string scenarioName)
        {
            //Blacklist asteroid module from every game mode
            if (scenarioName == "ScenarioDiscoverableObjects")
            {
                return false;
            }

            //Blacklisted modes for sandbox
            if (Client.fetch.gameMode == GameMode.SANDBOX)
            {
                switch (scenarioName)
                {
                    case "ResearchAndDevelopment":
                    case "VesselRecovery":
                        return false;
                }
            }

            //Blacklisted modules for science/sandbox
            if (Client.fetch.gameMode == GameMode.SANDBOX || Client.fetch.gameMode == GameMode.SCIENCE)
            {
                switch (scenarioName)
                {
                    case "ContractSystem":
                    case "Funding":
                    case "Reputation":
                        return false;
                }
            }

            return true;
        }

        private void SendScenarioModules()
        {
            List<string> scenarioName = new List<string>();
            List<byte[]> scenarioData = new List<byte[]>();
            foreach (ProtoScenarioModule psm in HighLogic.CurrentGame.scenarios)
            {
                //Skip sending science data in sandbox mode (If this can even happen?)
                if (psm != null ? (psm.moduleName != null && psm.moduleRef != null) : false)
                {
                    //Skip sending of blacklisted modules
                    if (!IsScenarioModuleAllowed(psm.moduleName))
                    {
                        DarkLog.Debug("Skipped sending of '" + psm.moduleName + "' in " + Client.fetch.gameMode + " mode");
                        continue;
                    }

                    ConfigNode scenarioNode = new ConfigNode();
                    psm.moduleRef.Save(scenarioNode);
                    byte[] scenarioBytes = ConfigNodeSerializer.fetch.Serialize(scenarioNode);
                    if (scenarioBytes != null)
                    {
                        scenarioName.Add(psm.moduleName);
                        scenarioData.Add(scenarioBytes);
                    }
                }
            }

            if (scenarioName.Count > 0)
            {
                NetworkWorker.fetch.SendScenarioModuleData(scenarioName.ToArray(), scenarioData.ToArray());
            }
        }

        public void LoadScenarioDataIntoGame()
        {
            while (scenarioQueue.Count > 0)
            {
                LoadScenarioData(scenarioQueue.Dequeue());
            }
            CheckScenarioModules();
        }

        public void CheckScenarioModules()
        {
            //Create scenarios that exist for all games
            if (IsScenarioModuleAllowed("ProgressTracking") && !HighLogic.CurrentGame.scenarios.Exists(psm => psm.moduleName == "ProgressTracking"))
            {
                DarkLog.Debug("Creating new ProgressTracking data");
                LoadNewScenarioData(GetBlankProgressTracking());
            }

            if (IsScenarioModuleAllowed("ResearchAndDevelopment") && !HighLogic.CurrentGame.scenarios.Exists(psm => psm.moduleName == "ResearchAndDevelopment"))
            {
                DarkLog.Debug("Creating new ResearchAndDevelopment data");
                LoadNewScenarioData(GetBlankResearchAndDevelopment());
            }

            if (IsScenarioModuleAllowed("VesselRecovery") && !HighLogic.CurrentGame.scenarios.Exists(psm => psm.moduleName == "VesselRecovery"))
            {
                DarkLog.Debug("Creating new VesselRecovery data");
                LoadNewScenarioData(GetBlankVesselRecovery());
            }

            if (IsScenarioModuleAllowed("ContractSystem") && !HighLogic.CurrentGame.scenarios.Exists(psm => psm.moduleName == "ContractSystem"))
            {
                DarkLog.Debug("Creating new ContractSystem data");
                LoadNewScenarioData(GetBlankContractSystem());
            }

            if (IsScenarioModuleAllowed("Funding") && !HighLogic.CurrentGame.scenarios.Exists(psm => psm.moduleName == "Funding"))
            {
                DarkLog.Debug("Creating new Funding data");
                LoadNewScenarioData(GetBlankFunding());
            }

            if (IsScenarioModuleAllowed("Reputation") && !HighLogic.CurrentGame.scenarios.Exists(psm => psm.moduleName == "Reputation"))
            {
                DarkLog.Debug("Creating new Reputation data");
                LoadNewScenarioData(GetBlankReputation());
            }
        }
        //Would be nice if we could ask KSP to do this for us...
        /*
        EDIT 18th July 2014 - Guess what this prints to the log

        ProgressTracking progressTracking = new ProgressTracking();
        if (progressTracking == null)
        {
            DarkLog.Debug("ProgressTracking is null, what the fuck?");
        }
        */
        private ConfigNode GetBlankContractSystem()
        {
            ConfigNode newNode = new ConfigNode();
            newNode.AddValue("name", "ContractSystem");
            newNode.AddValue("scene", "7, 8, 5, 6, 9");
            newNode.AddValue("update", "0.06");
            newNode.AddNode("CONTRACTS");           
            return newNode;
        }

        private ConfigNode GetBlankFunding()
        {
            ConfigNode newNode = new ConfigNode();
            newNode.AddValue("name", "Funding");
            newNode.AddValue("scene", "7, 8, 5, 6, 9");
            newNode.AddValue("funds", "10000");
            return newNode;
        }

        private ConfigNode GetBlankProgressTracking()
        {
            ConfigNode newNode = new ConfigNode();
            newNode.AddValue("name", "ProgressTracking");
            newNode.AddValue("scene", "7, 8, 5");
            newNode.AddNode("Progress");
            return newNode;
        }

        private ConfigNode GetBlankResearchAndDevelopment()
        {
            ConfigNode newNode = new ConfigNode();
            newNode.AddValue("name", "ResearchAndDevelopment");
            newNode.AddValue("scene", "7, 8, 5, 6, 9");
            newNode.AddValue("sci", "0");
            ConfigNode techNode = newNode.AddNode("Tech");
            techNode.AddValue("id", "start");
            techNode.AddValue("state", "Available");
            techNode.AddValue("part", "mk1pod");
            techNode.AddValue("part", "liquidEngine");
            techNode.AddValue("part", "solidBooster");
            techNode.AddValue("part", "fuelTankSmall");
            techNode.AddValue("part", "trussPiece1x");
            techNode.AddValue("part", "longAntenna");
            techNode.AddValue("part", "parachuteSingle");
            return newNode;
        }

        private ConfigNode GetBlankVesselRecovery()
        {
            ConfigNode newNode = new ConfigNode();
            newNode.AddValue("name", "VesselRecovery");
            newNode.AddValue("scene", "5, 7, 8, 6, 9");
            return newNode;
        }

        private ConfigNode GetBlankReputation()
        {
            ConfigNode newNode = new ConfigNode();
            newNode.AddValue("name", "Reputation");
            newNode.AddValue("scene", "5, 7, 8, 6, 9");
            newNode.AddValue("rep", "0");
            return newNode;
        }

        public void LoadScenarioData(ScenarioEntry entry)
        {
            if (!IsScenarioModuleAllowed(entry.scenarioName))
            {
                DarkLog.Debug("Skipped loading of '" + entry.scenarioName + "' in " + Client.fetch.gameMode + " mode");
                return;
            }

            //Load data from DMP
            if (entry.scenarioNode == null)
            {
                DarkLog.Debug(entry.scenarioName + " scenario data failed to create a ConfigNode!");
                blockScenarioDataSends = true;
                return;
            }

            //Load data into game
            bool loaded = false;
            foreach (ProtoScenarioModule psm in HighLogic.CurrentGame.scenarios)
            {
                if (psm.moduleName == entry.scenarioName)
                {
                    DarkLog.Debug("Loading existing " + entry.scenarioName + " scenario module");
                    try
                    {
                        if (psm.moduleRef == null)
                        {
                            DarkLog.Debug("Fixing null scenario module!");
                            psm.moduleRef = new ScenarioModule();
                        }
                        psm.moduleRef.Load(entry.scenarioNode);
                    }
                    catch (Exception e)
                    {
                        DarkLog.Debug("Error loading " + entry.scenarioName + " scenario module, Exception: " + e);
                        blockScenarioDataSends = true;
                    }
                    loaded = true;
                }
            }
            if (!loaded)
            {
                DarkLog.Debug("Loading new " + entry.scenarioName + " scenario module");
                LoadNewScenarioData(entry.scenarioNode);
            }
        }

        public void LoadNewScenarioData(ConfigNode newScenarioData)
        {
            ProtoScenarioModule newModule = new ProtoScenarioModule(newScenarioData);
            try
            {
                HighLogic.CurrentGame.scenarios.Add(newModule);
                newModule.Load(ScenarioRunner.fetch);
            }
            catch
            {
                DarkLog.Debug("Error loading scenario data!");
                blockScenarioDataSends = true;
            }
        }

        public void QueueScenarioData(string scenarioName, ConfigNode scenarioData)
        {
            ScenarioEntry entry = new ScenarioEntry();
            entry.scenarioName = scenarioName;
            entry.scenarioNode = scenarioData;
            scenarioQueue.Enqueue(entry);
        }

        public static void Reset()
        {
            lock (Client.eventLock)
            {
                if (singleton != null)
                {
                    singleton.workerEnabled = false;
                    Client.updateEvent.Remove(singleton.Update);
                }
                singleton = new ScenarioWorker();
                Client.updateEvent.Add(singleton.Update);
            }
        }
    }

    public class ScenarioEntry
    {
        public string scenarioName;
        public ConfigNode scenarioNode;
    }
}

