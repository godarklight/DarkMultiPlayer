using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.IO;
using System.Reflection;
using System.CodeDom.Compiler;
using Microsoft.CSharp;

namespace DarkMultiPlayerServer
{
    public class ScriptManager
    {
        private static readonly ArrayList m_compiledassemblies = new ArrayList();
        public static string Assemblies = "System.dll,System.Xml.dll,DMPServer.exe,DarkMultiPlayer-Common.dll";
        public bool Initialize()
        {
            return true;
        }

        public static ArrayList CompiledAssemblies
        {
            get
            {
                return m_compiledassemblies;
            }
        }

        private ArrayList ParseDirectory(DirectoryInfo path, string filter, bool deep)
        {
            ArrayList files = new ArrayList();

            if (!path.Exists)
                return files;

            files.AddRange(path.GetFiles(filter));

            if (deep)
            {
                foreach (DirectoryInfo subdir in path.GetDirectories())
                    files.AddRange(ParseDirectory(subdir, filter, deep));
            }

            return files;
        }

        public bool CompileScripts(string path, string dllName)
        {

            ArrayList files = ParseDirectory(new DirectoryInfo(path), "*.cs", true);
            if (files.Count == 0)
            {
                DarkLog.Normal("No Scripts to compile.");
                return true;
            }

            string[] asm_names = Assemblies.Split(',');
            // On efface l'ancien dll
            if (File.Exists(dllName))
                File.Delete(dllName);
            DarkLog.Normal("Compiling scripts on " + path);
            CompilerResults res = null;
            try
            {
                CodeDomProvider compiler = new CSharpCodeProvider(new Dictionary<string, string>() { { "CompilerVersion", "v4.0" } });
                CompilerParameters param = new CompilerParameters(asm_names);

                param.GenerateExecutable = false;
                param.GenerateInMemory = true;
                param.WarningLevel = 2;
                //param.CompilerOptions = @"/lib:";

                // DEBUG
                param.IncludeDebugInformation = true;
                param.TempFiles.KeepFiles = true; // Conserve les PDB pour le mode Debug

                string[] filepaths = new string[files.Count];
                for (int i = 0; i < files.Count; i++)
                    filepaths[i] = ((FileInfo)files[i]).FullName;
                
                res = compiler.CompileAssemblyFromFile(param, filepaths);

                //After compiling, collect
                GC.Collect();

                if (res.Errors.HasErrors)
                {
                    foreach (CompilerError err in res.Errors)
                    {
                        //if (err.IsWarning) continue;

                        StringBuilder builder = new StringBuilder();
                        builder.Append("   ");
                        builder.Append(err.FileName);
                        builder.Append(" Line:");
                        builder.Append(err.Line);
                        builder.Append(" Col:");
                        builder.Append(err.Column);
                        DarkLog.Error("Script compilation failed because: ");
                        DarkLog.Error(err.ErrorText);
                        DarkLog.Error(builder.ToString());
                    }

                    return false;
                }
                DarkLog.Normal("Scripts successfully compiled !");
                m_compiledassemblies.Clear();
                if (!m_compiledassemblies.Contains(res.CompiledAssembly))
                {
                    DarkLog.Normal("Adding assembly into list");
                    m_compiledassemblies.Add(res.CompiledAssembly);
                }

            }
            catch (Exception e)
            {
                DarkLog.Error("CompileScripts" + e);
                //m_compiledassemblies.Clear();
            }
            return true;
        }


    }
}
