using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;


namespace UnityToolBox
{
    public class AssemblyLoader : MonoBehaviour
    {
        public const string folderName = "CoreAssemblies";
        private static readonly List<Assembly> _assemblies = new List<Assembly>();

        public void Start(){
            LoadAssemblies();
            ExecuteEntry();
        }

        public static void LoadAssemblies()
        {
            string path = "";
            if (Application.isEditor)
            {
                path = Path.Combine(Application.dataPath, folderName);
            }
            else
            {
                //exe path
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);
            }


            Terminal.Log("App path: " + AppDomain.CurrentDomain.BaseDirectory);


            //create directory if not exits
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            //load assembly with pdb in folder, the pdb might not exist
            string[] files = Directory.GetFiles(path, "*.dll");
            foreach (var file in files)
            {
                try
                {
                    Terminal.Log("Trying to load: " + file);
                    string pdb = Path.ChangeExtension(file, ".pdb");
                    if (File.Exists(pdb))
                    {
                        _assemblies.Add(Assembly.Load(File.ReadAllBytes(file), File.ReadAllBytes(pdb)));
                    }
                    else
                    {
                        _assemblies.Add(Assembly.Load(File.ReadAllBytes(file)));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
        }

        public static void ExecuteEntry()
        {
            //find non-static class implement IEntry interface and sort by ExecuteOrder
            List<IEntry> entries = new List<IEntry>();
            foreach (var assembly in _assemblies)
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsClass && !type.IsAbstract && !type.IsSealed && typeof(IEntry).IsAssignableFrom(type))
                    {
                        try
                        {
                            entries.Add((IEntry)Activator.CreateInstance(type));
                        }
                        catch (Exception e)
                        {
                            Debug.LogError(e);
                        }
                    }
                }
            }

            entries.Sort((x, y) => x.ExecuteOder.CompareTo(y.ExecuteOder));
            //execute entry
            foreach (var entry in entries)
            {
                try
                {
                    entry.Entry();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
        }
    }
}

