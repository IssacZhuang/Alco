using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using UnityEngine;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Security.Policy;
using System.Runtime.CompilerServices;

namespace AssemblyHotReload
{
    public class AssemblyHotReload : Mod
    {
        private AppDomain _domain;
        public AppDomain Domain
        {
            get
            {
                if (_domain == null)
                {
                    InitAppDomain();
                }
                return _domain;
            }
        }

        public AssemblyHotReload(ModContentPack content) : base(content)
        {

        }

        public override string SettingsCategory()
        {
            return "Hot Reload - " + Content.Name;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (Widgets.ButtonText(new Rect(10, 50, 200, 30), "Reload Assembly"))
            {
                Log.Message("[AssemblyHotReload] Reloading Assembly");
                ReloadAssembly();
            }
        }

        private void ReloadAssembly()
        {
            var defs = GenDefDatabase.GetAllDefsInDatabaseForDef(typeof(ThingDef)).AsParallel().Where(def => def.modContentPack == Content);
            ResetAppDomain();
            InitAppDomain();

            foreach (FileInfo fileInfo in Enumerable.Select<Tuple<string, FileInfo>, FileInfo>(ModContentPack.GetAllFilesForModPreserveOrder(this.Content, "Assemblies/", (string e) => e.ToLower() == ".dll", null), (Tuple<string, FileInfo> f) => f.Item2))
            {
                Log.Message("Loading " + fileInfo.FullName);
                if (fileInfo.Name == "AssemblyHotReload.dll") continue;
                Assembly assembly = null;
                try
                {
                    byte[] rawAssembly = File.ReadAllBytes(fileInfo.FullName);
                    // var assemblyLoader = (AssemblyLoader)Domain.CreateInstanceAndUnwrap(typeof(AssemblyLoader).Assembly.FullName, typeof(AssemblyLoader).FullName);
                    // assembly = assemblyLoader.LoadFrom(fileInfo.FullName);
                    assembly = Domain.Load(rawAssembly);
                    Log.Message("Loaded " + fileInfo.Name + ", Assembly: " + assembly.FullName);

                }
                catch (Exception ex)
                {
                    Log.Error("Exception loading " + fileInfo.Name + ": " + ex.ToString());
                    break;
                }
            }

            Parallel.ForEach(defs, def => ReplaceType(def as ThingDef));
        }

        private void ResetAppDomain()
        {
            if (_domain == null) return;
            AppDomain.Unload(_domain);
            _domain = null;
        }

        private void InitAppDomain()
        {
            if (_domain != null) return;
            // AppDomainSetup domaininfo = new AppDomainSetup();
            // domaininfo.ApplicationBase = AppDomain.CurrentDomain.BaseDirectory;
            // Evidence adevidence = AppDomain.CurrentDomain.Evidence;
            _domain = AppDomain.CreateDomain("HotReloadDomain");
        }

        private void ReplaceType(ThingDef def)
        {
            if (def == null) return;
            StringBuilder sb = new StringBuilder();
            sb.Append("Reload classes for " + def.defName + " : \n");
            bool changed = false;
            if (GetTypeInDomain(def.thingClass?.FullName, out Type newType))
            {
                bool isSameType = def.thingClass.Assembly == newType.Assembly;
                Log.Message("IsSameType: " + isSameType);
                def.thingClass = newType;
                changed = true;
                sb.Append("ThingClass: " + def.thingClass?.FullName + ", IsSameType: " + isSameType + "\n");
            }

            if (def.comps != null)
            {
                foreach (var comp in def.comps)
                {
                    if (GetTypeInDomain(comp.compClass?.FullName, out Type newType2))
                    {
                        bool isSameType = comp.compClass.Assembly == newType2.Assembly;
                        comp.compClass = newType2;
                        changed = true;
                        sb.Append("CompClass: " + comp.compClass?.FullName + ", IsSameType: " + isSameType + "\n");
                    }

                }
            }

            if (def.Verbs != null)
            {
                foreach (var verb in def.Verbs)
                {
                    if (GetTypeInDomain(verb.verbClass?.FullName, out Type newType3))
                    {
                        bool isSameType = verb.verbClass.Assembly == newType3.Assembly;
                        verb.verbClass = newType3;
                        changed = true;
                        sb.Append("VerbClass: " + verb.verbClass?.FullName + ", IsSameType: " + isSameType + "\n");
                    }
                }
            }
            if (changed)
            {
                Log.Message(sb.ToString());
            }

        }

        private bool GetTypeInDomain(string typeName, out Type newType)
        {
            newType = null;
            if (typeName == null) return false;
            newType = Domain.GetAssemblies().SelectMany(assembly => assembly.GetTypes()).FirstOrDefault(type => type.FullName == typeName);
            return newType != null;
        }
    }
}
