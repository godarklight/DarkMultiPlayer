using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using DarkMultiPlayerCommon;

namespace DarkMultiPlayerServer
{
    internal static class DMPPluginHandler
    {
        private static readonly List<IDMPPlugin> loadedPlugins = new List<IDMPPlugin>();

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

            //Iterate through the assemblies looking for classes that have the IDMPPlugin interface

            Type dmpInterfaceType = typeof(IDMPPlugin);

            foreach (Assembly loadedAssembly in loadedAssemblies)
            {
                Type[] loadedTypes = loadedAssembly.GetExportedTypes();
                foreach (Type loadedType in loadedTypes)
                {
                    Type[] typeInterfaces = loadedType.GetInterfaces();
                    bool containsDMPInterface = false;
                    foreach (Type typeInterface in typeInterfaces)
                    {
                        if (typeInterface == dmpInterfaceType)
                        {
                            containsDMPInterface = true;
                        }
                    }
                    if (containsDMPInterface)
                    {
                        DarkLog.Debug("Loading plugin: " + loadedType.FullName);

                        try
                        {
                            IDMPPlugin pluginInstance = ActivatePluginType(loadedType);

                            if (pluginInstance != null)
                            {
                                DarkLog.Debug("Loaded plugin: " + loadedType.FullName);

                                loadedPlugins.Add(pluginInstance);
                            }
                        }
                        catch (Exception ex)
                        {
                            DarkLog.Error("Error loading plugin " + loadedType.FullName + "(" + loadedType.Assembly.FullName + ") Exception: " + ex.ToString());
                        }
                    }
                }
            }
            DarkLog.Debug("Done!");
        }

        private static IDMPPlugin ActivatePluginType(Type loadedType)
        {
            try
            {
                //"as IDMPPlugin" will cast or return null if the type is not a IDMPPlugin
                IDMPPlugin pluginInstance = Activator.CreateInstance(loadedType) as IDMPPlugin;
                return pluginInstance;
            }
            catch (Exception e)
            {
                DarkLog.Error("Cannot activate plugin " + loadedType.Name + ", Exception: " + e.ToString());
                return null;
            }
        }

        //Fire OnUpdate
        public static void FireOnUpdate()
        {
            foreach (var plugin in loadedPlugins)
            {
                try
                {
                    plugin.OnUpdate();
                }
                catch (Exception e)
                {
                    Type type = plugin.GetType();
                    DarkLog.Debug("Error thrown in OnUpdate event for " + type.FullName + " (" + type.Assembly.FullName + "), Exception: " + e);
                }
            }
        }

        //Fire OnServerStart
        public static void FireOnServerStart()
        {
            foreach (var plugin in loadedPlugins)
            {
                try
                {
                    plugin.OnServerStart();
                }
                catch (Exception e)
                {
                    Type type = plugin.GetType();
                    DarkLog.Debug("Error thrown in OnServerStart event for " + type.FullName + " (" + type.Assembly.FullName + "), Exception: " + e);
                }
            }
        }

        //Fire OnServerStart
        public static void FireOnServerStop()
        {
            foreach (var plugin in loadedPlugins)
            {
                try
                {
                    plugin.OnServerStop();
                }
                catch (Exception e)
                {
                    Type type = plugin.GetType();
                    DarkLog.Debug("Error thrown in OnServerStop event for " + type.FullName + " (" + type.Assembly.FullName + "), Exception: " + e);
                }
            }
        }

        //Fire OnClientConnect
        public static void FireOnClientConnect(ClientObject client)
        {
            foreach (var plugin in loadedPlugins)
            {
                try
                {
                    plugin.OnClientConnect(client);
                }
                catch (Exception e)
                {
                    Type type = plugin.GetType();
                    DarkLog.Debug("Error thrown in OnClientConnect event for " + type.FullName + " (" + type.Assembly.FullName + "), Exception: " + e);
                }
            }
        }

        //Fire OnClientAuthenticated
        public static void FireOnClientAuthenticated(ClientObject client)
        {
            foreach (var plugin in loadedPlugins)
            {
                try
                {
                    plugin.OnClientAuthenticated(client);
                }
                catch (Exception e)
                {
                    Type type = plugin.GetType();
                    DarkLog.Debug("Error thrown in OnClientAuthenticated event for " + type.FullName + " (" + type.Assembly.FullName + "), Exception: " + e);
                }
            }
        }

        //Fire OnClientDisconnect
        public static void FireOnClientDisconnect(ClientObject client)
        {
            foreach (var plugin in loadedPlugins)
            {
                try
                {
                    plugin.OnClientDisconnect(client);
                }
                catch (Exception e)
                {
                    Type type = plugin.GetType();
                    DarkLog.Debug("Error thrown in OnClientDisconnect event for " + type.FullName + " (" + type.Assembly.FullName + "), Exception: " + e);
                }
            }
        }

        //Fire OnMessageReceived
        public static void FireOnMessageReceived(ClientObject client, ClientMessage message)
        {
            bool handledByAny = false;
            foreach (var plugin in loadedPlugins)
            {
                try
                {
                    plugin.OnMessageReceived(client, message);

                    //prevent plugins from unhandling other plugin's handled requests
                    if (message.handled)
                    {
                        handledByAny = true;
                    }
                }
                catch (Exception e)
                {
                    Type type = plugin.GetType();
                    DarkLog.Debug("Error thrown in OnMessageReceived event for " + type.FullName + " (" + type.Assembly.FullName + "), Exception: " + e);
                }
            }
            message.handled = handledByAny;
        }

        //Fire OnMessageReceived
        public static void FireOnMessageSent(ClientObject client, ServerMessage message)
        {
            foreach (var plugin in loadedPlugins)
            {
                try
                {
                    plugin.OnMessageSent(client, message);
                }
                catch (Exception e)
                {
                    Type type = plugin.GetType();
                    DarkLog.Debug("Error thrown in OnMessageSent event for " + type.FullName + " (" + type.Assembly.FullName + "), Exception: " + e);
                }
            }
        }
    }
}

