using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DarkMultiPlayer
{
    class ConfigNodeSerializer
    {
        private static ConfigNodeSerializer singleton = new ConfigNodeSerializer();

        private delegate void WriteNodeDelegate(ConfigNode configNode,StreamWriter writer);

        private delegate List<string[]> PreFormatConfigDelegate(string[] cfgData);

        private delegate ConfigNode RecurseFormatDelegate(List<string[]> cfg);

        private WriteNodeDelegate WriteNodeThunk;
        private PreFormatConfigDelegate PreFormatConfigThunk;
        private RecurseFormatDelegate RecurseFormatThunk;

        public ConfigNodeSerializer()
        {
            CreateDelegates();
        }

        public static ConfigNodeSerializer fetch
        {
            get
            {
                return singleton;
            }
        }

        private void CreateDelegates()
        {
            try
            {
                DarkLog.Debug("ConfigNodeSerializer creating delegates...WriteNode...");
                Type configNodeType = typeof(ConfigNode);
                var writeNodeMethodInfo = configNodeType.GetMethod("WriteNode", BindingFlags.NonPublic | BindingFlags.Instance);
                
                //pass null for instance so we only do the slower reflection part once ever, then provide the instance at runtime
                WriteNodeThunk = (WriteNodeDelegate)Delegate.CreateDelegate(typeof(WriteNodeDelegate), null, writeNodeMethodInfo);

                DarkLog.Debug("ConfigNodeSerializer creating delegates...PreFormatConfig...");
                //these ones really are static and won't have a instance first parameter 
                var preFormatConfigMethodInfo = configNodeType.GetMethod("PreFormatConfig", BindingFlags.NonPublic | BindingFlags.Static);
                PreFormatConfigThunk = (PreFormatConfigDelegate)Delegate.CreateDelegate(typeof(PreFormatConfigDelegate), null, preFormatConfigMethodInfo);

                DarkLog.Debug("ConfigNodeSerializer creating delegates...RecurseFormat...");
                var recurseFormatMethodInfo = configNodeType.GetMethod("RecurseFormat", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(List<string[]>) }, null);
                RecurseFormatThunk = (RecurseFormatDelegate)Delegate.CreateDelegate(typeof(RecurseFormatDelegate), null, recurseFormatMethodInfo);

                DarkLog.Debug("ConfigNodeSerializer delegates ready!");
            }
            catch (Exception ex)
            {
                //maybe older or newer KSP version with different data?
                DarkLog.Debug("Can't create delegates for ConfigNode serialization. Falling back to temp file. Exception: " + ex.ToString());
            }
        }

        public byte[] Serialize(ConfigNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            if (WriteNodeThunk == null)
            {
                //if WriteNodeThunk wasn't set in the constructor, can't reflect into it
                //Use dogey hack fallback
                return ConvertConfigNodeToByteArray(node);
            }

            //otherwise, call the insides of what ConfigNode would have called if we said Save(filename)
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    //we late bind to the instance by passing the instance as the first argument
                    WriteNodeThunk(node, writer);
                    byte[] data = stream.ToArray();
                    return data;
                }
            }
        }

        public ConfigNode Deserialize(byte[] data)
        {
            if (PreFormatConfigThunk == null ||
                RecurseFormatThunk == null)
            {
                //if WriteNodeThunk wasn't set in the constructor, can't reflect into it
                //Use dogey hack fallback
                return ConvertByteArrayToConfigNode(data);
            }

            using (var stream = new MemoryStream(data))
            {
                using (var reader = new StreamReader(stream))
                {
                    List<string> lines = new List<string>();

                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine();
                        lines.Add(line);
                    }

                    string[] cfgData = lines.ToArray();

                    List<string[]> cfg = PreFormatConfigThunk(cfgData);
                    ConfigNode node = RecurseFormatThunk(cfg);

                    return node;
                }
            }
        }
        //Fall back methods in case reflection isn't working:
        //Welcome to the world of beyond-dodgy. KSP: Expose either these methods or the string data please!
        private static ConfigNode ConvertByteArrayToConfigNode(byte[] configData)
        {
            string tempFile = null;
            ConfigNode returnNode = null;
            if (configData != null)
            {
                try
                {
                    tempFile = Path.GetTempFileName();
                    File.WriteAllBytes(tempFile, configData);
                    returnNode = ConfigNode.Load(tempFile);
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Failed to convert byte[] to ConfigNode, Exception " + e);
                    returnNode = null;
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            }
            return returnNode;
        }

        private static byte[] ConvertConfigNodeToByteArray(ConfigNode configData)
        {
            string tempFile = null;
            byte[] returnByteArray = null;
            if (configData != null)
            {
                try
                {
                    tempFile = Path.GetTempFileName();
                    configData.Save(tempFile);
                    returnByteArray = File.ReadAllBytes(tempFile);
                }
                catch (Exception e)
                {
                    DarkLog.Debug("Failed to convert byte[] to ConfigNode, Exception " + e);
                    returnByteArray = null;
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            }
            return returnByteArray;
        }
    }
}
