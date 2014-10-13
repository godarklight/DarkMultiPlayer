﻿using System;
using System.Collections.Generic;
using System.IO;
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

        public byte[] Serialize(ConfigNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException("node");
            }

            //Call the insides of what ConfigNode would have called if we said Save(filename)
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
            if (data == null)
            {
                return null;
            }

            if (data.Length == 0)
            {
                return null;
            }

            using (var stream = new MemoryStream(data))
            {
				using (var reader = new StreamReader(stream))
                {
                    var lines = new List<string>();

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
