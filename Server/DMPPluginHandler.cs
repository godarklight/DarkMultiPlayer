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

        static DMPPluginHandler()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            //This will find and return the assembly requested if it is already loaded
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.FullName == args.Name)
                {
                    DarkLog.Debug("Resolved plugin assembly reference: " + args.Name + " (referenced by " + args.RequestingAssembly.FullName + ")");
                    return assembly;
                }
            }

            DarkLog.Error("Could not resolve assembly " + args.Name + " referenced by " + args.RequestingAssembly.FullName);
            return null;
        }

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
                        //UnsafeLoadFrom will not throw an exception if the dll is marked as unsafe, such as downloaded from internet in Windows
                        //See http://stackoverflow.com/a/15238782
                        Assembly loadedAssembly = Assembly.UnsafeLoadFrom(pluginFile);
                        loadedAssemblies.Add(loadedAssembly);
                        DarkLog.Debug("Loaded " + pluginFile);
                    }
                    catch (NotSupportedException)
                    {
                        //This should only occur if using Assembly.LoadFrom() above instead of Assembly.UnsafeLoadFrom()
                        DarkLog.Debug("Can't load dll, perhaps it is blocked: " + pluginFile);
                    }
                    catch
                    {
                        DarkLog.Debug("Error loading " + pluginFile);
                    }
                }
            }
            //Add all the event types
            pluginEvents.Add(typeof(DMPUpdate), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnServerStart), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnServerStop), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnClientConnect), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnClientAuthenticated), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnClientDisconnect), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnMessageReceived), new List<Delegate>());
            pluginEvents.Add(typeof(DMPOnMessageReceivedRaw), new List<Delegate>());
            //Iterate through the assemblies looking for the DMPPlugin attribute
            foreach (Assembly loadedAssembly in loadedAssemblies)
            {
                Type[] loadedTypes = loadedAssembly.GetExportedTypes();
                foreach (Type loadedType in loadedTypes)
                {
                    if (loadedType.IsDefined(typeof(DMPPluginAttribute), false))
                    {
                        object pluginInstance = ActivatePluginType(loadedType);

                        if (pluginInstance != null)
                        {
                            CreatePluginDelegates(loadedAssembly, loadedType, pluginInstance);
                        }
                    }
                }
            }
            DarkLog.Debug("Done!");
        }

        private static object ActivatePluginType(Type loadedType)
        {
            DarkLog.Debug("Loading " + loadedType.Name);
            
            try
            {
                object pluginInstance = Activator.CreateInstance(loadedType);
                return pluginInstance;
            }
            catch (Exception e)
            {
                DarkLog.Error("Cannot activate plugin " + loadedType.Name + ", Exception: " + e.ToString());
                return null;
            }
        }

        private static void CreatePluginDelegates(Assembly loadedAssembly, Type loadedType, object pluginInstance)
        {
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
        //Fire OnMessageReceived
        public static void FireOnMessageReceivedRaw(ClientObject client, ref ClientMessage message)
        {
            foreach (DMPOnMessageReceivedRaw pluginEvent in pluginEvents[typeof(DMPOnMessageReceivedRaw)])
            {
                try
                {
                    pluginEvent(client, ref message);
                    if (message == null) {
                        return;
                    }
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

