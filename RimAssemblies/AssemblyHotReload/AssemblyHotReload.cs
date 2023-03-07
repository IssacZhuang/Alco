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
            if (Widgets.ButtonText(new Rect(10, 10, 200, 30), "Reload Assembly"))
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
                Assembly assembly = null;
                try
                {
                    byte[] rawAssembly = File.ReadAllBytes(fileInfo.FullName);
                    FileInfo fileInfo2 = new FileInfo(Path.Combine(fileInfo.DirectoryName, Path.GetFileNameWithoutExtension(fileInfo.FullName)) + ".pdb");
                    if (fileInfo2.Exists)
                    {
                        byte[] rawSymbolStore = File.ReadAllBytes(fileInfo2.FullName);
                        assembly = Domain.Load(rawAssembly, rawSymbolStore);
                    }
                    else
                    {
                        assembly = Domain.Load(rawAssembly);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Exception loading " + fileInfo.Name + ": " + ex.ToString());
                    break;
                }
            }

            Parallel.ForEach(defs, def => ReplaceType(def as ThingDef, Domain));
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
            _domain = AppDomain.CreateDomain("HotReloadDomain");
        }

        private void ReplaceType(ThingDef def, AppDomain domain)
        {
            if(def == null) return;
            StringBuilder sb = new StringBuilder();
            sb.Append("Reload classes for " + def.defName + " : \n");
            def.thingClass = GetTypeInDomain(def.thingClass?.FullName);
            if (def.comps != null)
            {
                foreach (var comp in def.comps)
                {
                    comp.compClass = GetTypeInDomain(comp.compClass?.FullName);
                    sb.Append("CompClass: " + comp.compClass?.FullName + "\n");
                }
            }

            if(def.Verbs != null)
            {
                foreach (var verb in def.Verbs)
                {
                    verb.verbClass = GetTypeInDomain(verb.verbClass?.FullName);
                    sb.Append("VerbClass: " + verb.verbClass?.FullName + "\n");
                }
            }
            Log.Message(sb.ToString());
        }

        private Type GetTypeInDomain(string typeName){
            if(typeName == null) return null;
            return Domain.GetAssemblies().SelectMany(assembly => assembly.GetTypes()).FirstOrDefault(type => type.FullName == typeName);
        }
    }
}
