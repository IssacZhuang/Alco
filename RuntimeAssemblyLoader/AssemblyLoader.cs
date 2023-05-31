using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;


namespace RuntimeAssemblyLoader
{
    public class AssemblyLoader : MonoBehaviour
    {
        public const string folderName = "CoreAssemblies";
        public void Awake()
        {
            string path = "";
            if (Application.isEditor)
            {
                path = Path.Combine(Application.dataPath, folderName);
            }
            else
            {
                //exe path
                path = Path.Combine(Directory.GetCurrentDirectory(), folderName);
            }

            Debug.Log(path);
            List<Assembly> assemblies = new List<Assembly>();
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
                    string pdb = Path.ChangeExtension(file, ".pdb");
                    if (File.Exists(pdb))
                    {
                        assemblies.Add(Assembly.Load(File.ReadAllBytes(file), File.ReadAllBytes(pdb)));
                    }
                    else
                    {
                        assemblies.Add(Assembly.Load(File.ReadAllBytes(file)));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            //find non-static class implement IEntry interface and sort by ExecuteOrder
            List<IEntry> entries = new List<IEntry>();
            foreach (var assembly in assemblies)
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

