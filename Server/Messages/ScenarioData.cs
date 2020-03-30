using System;
using System.IO;
using MessageStream2;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;

namespace DarkMultiPlayerServer.Messages
{
    public class ScenarioData
    {
        public static string GetScenarioPath(ClientObject client)
        {
            string clientPath = Path.Combine(Server.universeDirectory, "Scenarios", client.playerName);
            if (Settings.settingsStore.sharedScience)
            {
                clientPath = Path.Combine(Server.universeDirectory, "Scenarios", "Shared");
            }
            if (!Directory.Exists(clientPath))
            {
                Directory.CreateDirectory(clientPath);
                if (!Settings.settingsStore.sharedScience)
                {
                    foreach (string file in Directory.GetFiles(Path.Combine(Server.universeDirectory, "Scenarios", "Initial")))
                    {
                        File.Copy(file, Path.Combine(clientPath, Path.GetFileName(file)));
                    }
                }
            }
            return clientPath;
        }

        public static void SendScenarioModules(ClientObject client)
        {
            string clientPath = GetScenarioPath(client);
            int numberOfScenarioModules = Directory.GetFiles(clientPath).Length;
            int currentScenarioModule = 0;
            string[] scenarioNames = new string[numberOfScenarioModules];
            byte[][] scenarioDataArray = new byte[numberOfScenarioModules][];
            foreach (string file in Directory.GetFiles(clientPath))
            {
                //Remove the .txt part for the name
                scenarioNames[currentScenarioModule] = Path.GetFileNameWithoutExtension(file);
                scenarioDataArray[currentScenarioModule] = File.ReadAllBytes(file);
                currentScenarioModule++;
            }
            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.SCENARIO_DATA, 5 * 1024 * 1024);
            newMessage.reliable = true;
            using (MessageWriter mw = new MessageWriter(newMessage.data.data))
            {
                mw.Write<string[]>(scenarioNames);
                foreach (byte[] scenarioData in scenarioDataArray)
                {
                    mw.Write<byte[]>(Compression.CompressIfNeeded(scenarioData));
                }
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            ClientHandler.SendToClient(client, newMessage, true);
        }

        // Send a specific scenario module to all clients - only called in shared science mode.
        public static void SendScenarioModuleToClients(string scenarioName, byte[] scenarioData)
        {
            string scenarioFile = Path.Combine(Server.universeDirectory, "Scenarios", "Shared", scenarioName + ".txt");

            string[] scenarioNames = new string[1];
            byte[][] scenarioDataArray = new byte[1][];

            scenarioNames[0] = scenarioName;
            scenarioDataArray[0] = File.ReadAllBytes(scenarioFile);

            NetworkMessage newMessage = NetworkMessage.Create((int)ServerMessageType.SCENARIO_DATA, 512 * 1024);
            newMessage.reliable = true;
            using (MessageWriter mw = new MessageWriter())
            {
                mw.Write<string[]>(scenarioNames);
                foreach (byte[] thisScenarioData in scenarioDataArray)
                {
                    mw.Write<byte[]>(Compression.CompressIfNeeded(thisScenarioData));
                }
                newMessage.data.size = (int)mw.GetMessageLength();
            }
            DarkLog.Debug("Sent " + scenarioName + " to all players.");
        }

        public static void HandleScenarioModuleData(ByteArray messageData, Connection<ClientObject> connection)
        {
            ClientObject client = connection.state;
            string clientPath = GetScenarioPath(client);
            using (MessageReader mr = new MessageReader(messageData.data))
            {
                //Don't care about subspace / send time.
                string[] scenarioName = mr.Read<string[]>();
                DarkLog.Debug("Saving " + scenarioName.Length + " scenario modules from " + client.playerName);

                for (int i = 0; i < scenarioName.Length; i++)
                {
                    byte[] scenarioData = Compression.DecompressIfNeeded(mr.Read<byte[]>());
                    lock (Server.universeSizeLock)
                    {
                        File.WriteAllBytes(Path.Combine(clientPath, scenarioName[i] + ".txt"), scenarioData);
                    }
                    if (Settings.settingsStore.sharedScience)
                    {
                        SendScenarioModuleToClients(scenarioName[i], scenarioData);
                    }
                }
            }
        }
    }
}

