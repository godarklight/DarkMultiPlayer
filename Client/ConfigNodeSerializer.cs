using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DarkMultiPlayerCommon;
using DarkNetworkUDP;

namespace DarkMultiPlayer
{
    public class ConfigNodeSerializer
    {
        private delegate void WriteNodeDelegate(ConfigNode configNode, StreamWriter writer);

        
        private delegate List<string[]> PreFormatConfigDelegate(string[] cfgData);

        private delegate ConfigNode RecurseFormatDelegate(List<string[]> cfg);

        private WriteNodeDelegate WriteNodeThunk;
        private PreFormatConfigDelegate PreFormatConfigThunk;
        private RecurseFormatDelegate RecurseFormatThunk;
        private byte[] configNodeBuffer = new byte[Common.MAX_MESSAGE_SIZE];

        public ConfigNodeSerializer()
        {
            CreateDelegates();
        }

        private void CreateDelegates()
        {
            Type configNodeType = typeof(ConfigNode);
            MethodInfo writeNodeMethodInfo = configNodeType.GetMethod("WriteNode", BindingFlags.NonPublic | BindingFlags.Instance);

            //pass null for instance so we only do the slower reflection part once ever, then provide the instance at runtime
            WriteNodeThunk = (WriteNodeDelegate)Delegate.CreateDelegate(typeof(WriteNodeDelegate), null, writeNodeMethodInfo);

            //these ones really are static and won't have a instance first parameter 
            MethodInfo preFormatConfigMethodInfo = configNodeType.GetMethod("PreFormatConfig", BindingFlags.NonPublic | BindingFlags.Static);
            PreFormatConfigThunk = (PreFormatConfigDelegate)Delegate.CreateDelegate(typeof(PreFormatConfigDelegate), null, preFormatConfigMethodInfo);

            MethodInfo recurseFormatMethodInfo = configNodeType.GetMethod("RecurseFormat", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(List<string[]>) }, null);
            RecurseFormatThunk = (RecurseFormatDelegate)Delegate.CreateDelegate(typeof(RecurseFormatDelegate), null, recurseFormatMethodInfo);
        }

        public ByteArray Serialize(ConfigNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            ByteArray retVal;
            int retValSize = 0;

            //Call the insides of what ConfigNode would have called if we said Save(filename)
            using (MemoryStream stream = new MemoryStream(configNodeBuffer))
            {
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    //we late bind to the instance by passing the instance as the first argument
                    WriteNodeThunk(node, writer);
                    retValSize = (int)stream.Position;
                }
            }
            retVal = ByteRecycler.GetObject(retValSize);
            Array.Copy(configNodeBuffer, 0, retVal.data, 0, retValSize);
            return retVal;
        }

        public ConfigNode Deserialize(byte[] data)
        {
            if (data == null)
            {
                return null;
            }

            if (data.Length == 0)
            {
                return null;
            }

            using (MemoryStream stream = new MemoryStream(data))
            {
                using (StreamReader reader = new StreamReader(stream))
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

        public ConfigNode Deserialize(ByteArray data)
        {
            if (data == null)
            {
                return null;
            }

            if (data.Length == 0)
            {
                return null;
            }

            using (MemoryStream stream = new MemoryStream(data.data, 0, data.Length))
            {
                using (StreamReader reader = new StreamReader(stream))
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
    }
}
