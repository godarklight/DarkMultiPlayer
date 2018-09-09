using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using MessageStream2;

namespace DarkMultiPlayerServer
{
    public class BanSystem
    {
        private static BanSystem instance;
        private static string banlistFile;
        private static string ipBanlistFile;
        private static string publicKeyBanlistFile;
        private List<string> bannedNames = new List<string>();
        private List<IPAddress> bannedIPs = new List<IPAddress>();
        private List<string> bannedPublicKeys = new List<string>();

        public BanSystem()
        {
            LoadBans();
        }

        public static BanSystem fetch
        {
            get
            {
                //Lazy loading
                if (instance == null)
                {
                    banlistFile = Path.Combine(Server.configDirectory, "banned-players.txt");
                    ipBanlistFile = Path.Combine(Server.configDirectory, "banned-ips.txt");
                    publicKeyBanlistFile = Path.Combine(Server.configDirectory, "banned-keys.txt");
                    instance = new BanSystem();
                }
                return instance;
            }
        }

        public void BanPlayer(string commandArgs)
        {
            string playerName = commandArgs;
            string reason = "";

            if (commandArgs.Contains(" "))
            {
                playerName = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" ") + 1);
            }

            if (playerName != "")
            {

                ClientObject player = ClientHandler.GetClientByName(playerName);

                if (reason == "")
                {
                    reason = "no reason specified";
                }

                if (player != null)
                {
                    Messages.ConnectionEnd.SendConnectionEnd(player, "You were banned from the server!");
                }

                DarkLog.Normal("Player '" + playerName + "' was banned from the server");
                bannedNames.Add(playerName);
                SaveBans();
            }

        }

        public void BanIP(string commandArgs)
        {
            string ip = commandArgs;
            string reason = "";

            if (commandArgs.Contains(" "))
            {
                ip = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" ") + 1);
            }

            IPAddress ipAddress;
            if (IPAddress.TryParse(ip, out ipAddress))
            {

                ClientObject player = ClientHandler.GetClientByIP(ipAddress);

                if (player != null)
                {
                    Messages.ConnectionEnd.SendConnectionEnd(player, "You were banned from the server!");
                }
                bannedIPs.Add(ipAddress);
                SaveBans();

                DarkLog.Normal("IP Address '" + ip + "' was banned from the server: " + reason);
            }
            else
            {
                DarkLog.Normal(ip + " is not a valid IP address");
            }

        }

        public void BanPublicKey(string commandArgs)
        {
            string publicKey = commandArgs;
            string reason = "";

            if (commandArgs.Contains(" "))
            {
                publicKey = commandArgs.Substring(0, commandArgs.IndexOf(" "));
                reason = commandArgs.Substring(commandArgs.IndexOf(" ") + 1);
            }

            ClientObject player = ClientHandler.GetClientByPublicKey(publicKey);

            if (reason == "")
            {
                reason = "no reason specified";
            }

            if (player != null)
            {
                Messages.ConnectionEnd.SendConnectionEnd(player, "You were banned from the server!");
            }
            bannedPublicKeys.Add(publicKey);
            SaveBans();

            DarkLog.Normal("Public key '" + publicKey + "' was banned from the server: " + reason);

        }

        public bool IsPlayerNameBanned(string playerName)
        {
            return bannedNames.Contains(playerName);
        }

        public bool IsIPBanned(IPAddress address)
        {
            return bannedIPs.Contains(address);
        }

        public bool IsPublicKeyBanned(string publicKey)
        {
            return bannedPublicKeys.Contains(publicKey);
        }

        public void SaveBans()
        {
            try
            {
                if (File.Exists(banlistFile))
                {
                    File.SetAttributes(banlistFile, FileAttributes.Normal);
                }

                using (StreamWriter sw = new StreamWriter(banlistFile))
                {

                    foreach (string name in bannedNames)
                    {
                        sw.WriteLine("{0}", name);
                    }
                }

                using (StreamWriter sw = new StreamWriter(ipBanlistFile))
                {
                    foreach (IPAddress ip in bannedIPs)
                    {
                        sw.WriteLine("{0}", ip);
                    }
                }

                using (StreamWriter sw = new StreamWriter(publicKeyBanlistFile))
                {
                    foreach (string publicKey in bannedPublicKeys)
                    {
                        sw.WriteLine("{0}", publicKey);
                    }
                }
            }
            catch (Exception e)
            {
                DarkLog.Error("Error saving bans!, Exception: " + e);
            }
        }

        public void LoadBans()
        {
            bannedNames.Clear();
            bannedIPs.Clear();
            bannedPublicKeys.Clear();

            if (File.Exists(banlistFile))
            {
                foreach (string line in File.ReadAllLines(banlistFile))
                {
                    if (!bannedNames.Contains(line))
                    {
                        bannedNames.Add(line);
                    }
                }
            }
            else
            {
                File.Create(banlistFile);
            }

            if (File.Exists(ipBanlistFile))
            {
                foreach (string line in File.ReadAllLines(ipBanlistFile))
                {
                    IPAddress banIPAddr = null;
                    if (IPAddress.TryParse(line, out banIPAddr))
                    {
                        if (!bannedIPs.Contains(banIPAddr))
                        {
                            bannedIPs.Add(banIPAddr);
                        }
                    }
                    else
                    {
                        DarkLog.Error("Error in IP ban list file, " + line + " is not an IP address");
                    }
                }
            }
            else
            {
                File.Create(ipBanlistFile);
            }

            if (File.Exists(publicKeyBanlistFile))
            {
                foreach (string bannedPublicKey in File.ReadAllLines(publicKeyBanlistFile))
                {
                    if (!bannedPublicKeys.Contains(bannedPublicKey))
                    {
                        bannedPublicKeys.Add(bannedPublicKey);
                    }
                }
            }
            else
            {
                File.Create(publicKeyBanlistFile);
            }
        }
    }
}

