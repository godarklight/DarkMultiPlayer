using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    public class DMPPluginHandler
    {
        private static Dictionary <Delegate, DMPEventInfo> delegateInfo = new Dictionary<Delegate, DMPEventInfo>();
        private static Dictionary <Type, List<Delegate>> pluginEvents = new Dictionary<Type, List<Delegate>>();

        public static void LoadPlugins()
        {
            string pluginDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginDirectory))
            {
                Directory.CreateDirectory(pluginDirectory);
            }
            DarkLog.Debug("Loading plugins!");
            //Load all the assemblies just in case they depend on each other during instantation
            List<Assembly> loadedAssemblies = new List<Assembly>();
            string[] pluginFiles = Directory.GetFiles(pluginDirectory, "*", SearchOption.AllDirectories);
            foreach (string pluginFile in pluginFiles)
            {
                if (Path.GetExtension(pluginFile).ToLower() == ".dll")
                {
                    try
                    {
                        Assembly loadedAssembly = Assembly.LoadFile(pluginFile);
                        loadedAssemblies.Add(loadedAssembly);
                        DarkLog.Debug("Loaded " + pluginFile);
                    }
                    catch
                    {
                        DarkLog.Debug("Error loading " + pluginFile);
                    }
                }
            }
            // We load the script part too
            loadedAssemblies.Add((Assembly)ScriptManager.CompiledAssemblies[0]);
            //Add all the event types
            pluginEvents.Add(typeof(DMPUpdate), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnServerStart), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnServerStop), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnClientConnect), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnClientAuthenticated), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnClientDisconnect), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnMessageReceived), new List<Delegate>());
            //Iterate through the assemblies looking for the DMPPlugin attribute
            foreach (Assembly loadedAssembly in loadedAssemblies)
            {
                Type[] loadedTypes = loadedAssembly.GetExportedTypes();
                foreach (Type loadedType in loadedTypes)
                {
                    if (loadedType.IsDefined(typeof(DMPPluginAttribute), false))
                    {
                        DarkLog.Debug("Loading " + loadedType.Name);
                        object pluginInstance = Activator.CreateInstance(loadedType);
                        MethodInfo[] methodInfos = loadedType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                        foreach (MethodInfo methodInfo in methodInfos)
                        {
                            try
                            {
                                foreach (Type evT in pluginEvents.Keys)
                                {
                                    if (evT.Name.Substring(3) == methodInfo.Name)
                                    {
                                        DarkLog.Debug("Event registered : " + evT.Name);
                                        Delegate deg = Delegate.CreateDelegate(evT, pluginInstance, methodInfo);
                                        DMPEventInfo info = new DMPEventInfo();
                                        info.loadedAssembly = loadedAssembly.FullName;
                                        info.loadedType = loadedType.Name;
                                        delegateInfo.Add(deg, info);
                                        pluginEvents[evT].Add(deg);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                DarkLog.Error("Error loading " + methodInfo.Name + " from " + loadedType.Name + ", Exception: " + e.Message);
                            }
                        }
                    }
                }
            }
            DarkLog.Debug("Done!");
        }
        //Fire Update
        public static void FireUpdate()
        {
            foreach (DMPUpdate pluginEvent in pluginEvents[typeof(DMPUpdate)])
            {
                try
                {
                    pluginEvent();
                }
                catch (Exception e)
                {
                    DMPEventInfo eventInfo = delegateInfo[pluginEvent];
                    DarkLog.Debug("Error thrown in Update event for " + eventInfo.loadedType + " (" + eventInfo.loadedAssembly + "), Exception: " + e);
                }

            }
        }
        //Fire OnServerStart
        public static void FireOnServerStart()
        {
            foreach (DMPOnServerStart pluginEvent in pluginEvents[typeof(DMPOnServerStart)])
            {
                try
                {
                    pluginEvent();
                }
                catch (Exception e)
                {
                    DMPEventInfo eventInfo = delegateInfo[pluginEvent];
                    DarkLog.Debug("Error thrown in OnServerStart event for " + eventInfo.loadedType + " (" + eventInfo.loadedAssembly + "), Exception: " + e);
                }

            }
        }
        //Fire OnServerStart
        public static void FireOnServerStop()
        {
            foreach (DMPOnServerStop pluginEvent in pluginEvents[typeof(DMPOnServerStop)])
            {
                try
                {
                    pluginEvent();
                }
                catch (Exception e)
                {
                    DMPEventInfo eventInfo = delegateInfo[pluginEvent];
                    DarkLog.Debug("Error thrown in OnServerStop event for " + eventInfo.loadedType + " (" + eventInfo.loadedAssembly + "), Exception: " + e);
                }

            }
        }
        //Fire OnClientConnect
        public static void FireOnClientConnect(ClientObject client)
        {
            foreach (DMPOnClientConnect pluginEvent in pluginEvents[typeof(DMPOnClientConnect)])
            {
                try
                {
                    pluginEvent(client);
                }
                catch (Exception e)
                {
                    DMPEventInfo eventInfo = delegateInfo[pluginEvent];
                    DarkLog.Debug("Error thrown in OnClientConnect event for " + eventInfo.loadedType + " (" + eventInfo.loadedAssembly + "), Exception: " + e);
                }

            }
        }
        //Fire OnClientConnect
        public static void FireOnClientAuthenticated(ClientObject client)
        {
            foreach (DMPOnClientAuthenticated pluginEvent in pluginEvents[typeof(DMPOnClientAuthenticated)])
            {
                try
                {
                    pluginEvent(client);
                }
                catch (Exception e)
                {
                    DMPEventInfo eventInfo = delegateInfo[pluginEvent];
                    DarkLog.Debug("Error thrown in OnClientAuthenticated event for " + eventInfo.loadedType + " (" + eventInfo.loadedAssembly + "), Exception: " + e);
                }

            }
        }
        //Fire OnClientDisconnect
        public static void FireOnClientDisconnect(ClientObject client)
        {
            foreach (DMPOnClientDisconnect pluginEvent in pluginEvents[typeof(DMPOnClientDisconnect)])
            {
                try
                {
                    pluginEvent(client);
                }
                catch (Exception e)
                {
                    DMPEventInfo eventInfo = delegateInfo[pluginEvent];
                    DarkLog.Debug("Error thrown in OnClientDisconnect event for " + eventInfo.loadedType + " (" + eventInfo.loadedAssembly + "), Exception: " + e);
                }

            }
        }
        //Fire OnMessageReceived
        public static void FireOnMessageReceived(ClientObject client, ClientMessage message)
        {
            foreach (DMPOnMessageReceived pluginEvent in pluginEvents[typeof(DMPOnMessageReceived)])
            {
                try
                {
                    pluginEvent(client, message);
                }
                catch (Exception e)
                {
                    DMPEventInfo eventInfo = delegateInfo[pluginEvent];
                    DarkLog.Debug("Error thrown in OnMessageReceived event for " + eventInfo.loadedType + " (" + eventInfo.loadedAssembly + "), Exception: " + e);
                }

            }
        }
    }

    public class DMPEventInfo
    {
        public string loadedType;
        public string loadedAssembly;
    }
}

