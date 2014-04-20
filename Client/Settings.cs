using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace DarkMultiPlayer
{
    public class Settings
    {
        //Settings
        public string playerName;
        public Guid playerGuid;
        public List<ServerEntry> servers;
        private const string DEFAULT_PLAYER_NAME = "Player";
        private const string SETTINGS_FILE = "servers.xml";
        private const string TOKEN_FILE = "token.txt";
        private string dataLocation;
        private string settingsFile;
        private string tokenFile;

        public Settings()
        {
            dataLocation = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Data");
            settingsFile = Path.Combine(dataLocation, SETTINGS_FILE);
            tokenFile = Path.Combine(dataLocation, TOKEN_FILE);
            LoadSettings();
        }

        public void LoadSettings()
        {

            //Read XML settings
            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                if (!File.Exists(settingsFile))
                {
                    xmlDoc.LoadXml(newXMLString());
                    xmlDoc.Save(settingsFile);
                }
                xmlDoc.Load(settingsFile);
                playerName = xmlDoc.SelectSingleNode("/settings/global/@username").Value;
                XmlNodeList serverNodeList = xmlDoc.GetElementsByTagName("server");
                servers = new List<ServerEntry>();
                foreach (XmlNode xmlNode in serverNodeList)
                {
                    ServerEntry newServer = new ServerEntry();
                    newServer.name = xmlNode.Attributes["name"].Value;
                    newServer.address = xmlNode.Attributes["address"].Value;
                    Int32.TryParse(xmlNode.Attributes["port"].Value, out newServer.port);
                    servers.Add(newServer);
                }
            }
            catch (Exception e)
            {
                DarkLog.Debug("XML Exception: " + e);
            }

            //Read player token
            try
            {
                if (File.Exists(tokenFile))
                {
                    using (StreamReader sr = new StreamReader(tokenFile))
                    {
                        playerGuid = new Guid(sr.ReadLine());
                    }
                }
                else
                {
                    DarkLog.Debug("Creating new token file.");
                    using (StreamWriter sw = new StreamWriter(tokenFile))
                    {
                        playerGuid = Guid.NewGuid();
                        sw.WriteLine(playerGuid.ToString());
                    }
                }
            }
            catch
            {
                DarkLog.Debug("Error processing token, creating new token file.");
                playerGuid = Guid.NewGuid();
                if (File.Exists(tokenFile))
                {
                    File.Move(tokenFile, tokenFile + ".bak");
                }
                using (StreamWriter sw = new StreamWriter(tokenFile))
                {
                    sw.WriteLine(playerGuid.ToString());
                }
            }
        }

        public void SaveSettings()
        {
            XmlDocument xmlDoc = new XmlDocument();
            if (File.Exists(settingsFile))
            {
                xmlDoc.Load(settingsFile);
            }
            else
            {
                xmlDoc.LoadXml(newXMLString());
            }
            xmlDoc.SelectSingleNode("/settings/global/@username").Value = playerName;
            XmlNode serverNodeList = xmlDoc.SelectSingleNode("/settings/servers");
            serverNodeList.RemoveAll();
            foreach (ServerEntry server in servers)
            {
                XmlElement serverElement = xmlDoc.CreateElement("server");
                serverElement.SetAttribute("name", server.name);
                serverElement.SetAttribute("address", server.address);
                serverElement.SetAttribute("port", server.port.ToString());
                serverNodeList.AppendChild(serverElement);
            }
            xmlDoc.Save(settingsFile);
        }

        private string newXMLString()
        {
            return String.Format("<?xml version=\"1.0\"?><settings><global name=\"{0}\"/><servers></servers></settings>", DEFAULT_PLAYER_NAME);
        }
    }

    public class ServerEntry
    {
        public string name;
        public string address;
        public int port;
    }
}

