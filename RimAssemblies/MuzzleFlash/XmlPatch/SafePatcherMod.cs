using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Xml;
using UnityEngine;
using Verse;

namespace SafePatcher
{
    public class SafePatcherMod : Mod
    {
        public SafePatcherMod(ModContentPack content) : base(content)
        {
        }

        public override string SettingsCategory()
        {
            return "Muzzle Flash";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            //add button to reload patch
            if (Widgets.ButtonText(new Rect(0, 0, 200, 30), "Reload Patch"))
            {
                Log.Message("[SafePatcher] Reloading patches");
                ReloadPatch();
            }
        }

        [DebugAction("Muzzle Flash", "Reload Safe Patcher", false, false, false, 0, false, allowedGameStates = AllowedGameStates.Playing, displayPriority = 500)]
		private static void TryPlaceNearThing()
		{
			ReloadPatch();
		}

        public static void ReloadPatch(){
            var fieldMatch = typeof(PatchOperationFindMod).GetField("match", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fieldOperations = typeof(PatchOperationSequence).GetField("operations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fieldMods = typeof(PatchOperationFindMod).GetField("mods", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            foreach(var mod in LoadedModManager.RunningMods)
            {
                mod.ClearPatchesCache();
                foreach (var patch in mod.Patches)
                {
                
                    if (patch is PatchOperationReloable patchReloable)
                    {
                        ReloadPatchReloable(patchReloable);
                    }

                    if(patch is PatchOperationFindMod patchFindMod)
                    {
                        //get private field "match"
                        PatchOperation patchMatched = fieldMatch.GetValue(patchFindMod) as PatchOperation;

                        //get private field "mods"
                        List<string> mods = fieldMods.GetValue(patchFindMod) as List<string>;
                        bool found = false;
                        for (int i = 0; i < mods.Count; i++)
                        {
                            if (ModLister.HasActiveModWithName(mods[i]))
                            {
                                found = true;
                                break;
                            }
                        }

                        if(!found)
                        {
                            continue;
                        }

                        if (patchMatched is PatchOperationReloable patchMatchedReloable)
                        {
                            ReloadPatchReloable(patchMatchedReloable);
                        }

                        if(patchMatched is PatchOperationSequence patchMatchedSequence)
                        {
                            List<PatchOperation> operations = fieldOperations.GetValue(patchMatchedSequence) as List<PatchOperation>;
                            if (operations != null)
                            {
                                foreach (var operation in operations)
                                {
                                    if (operation is PatchOperationReloable operationReloable)
                                    {
                                        ReloadPatchReloable(operationReloable);
                                    }
                                }
                            }
                        }
                    }

                    
                }
                mod.ClearPatchesCache();
            }
        }

        private static void ReloadPatchReloable(PatchOperationReloable patch)
        {
            try
            {
                patch.ReloadPatch();
            }
            catch (Exception e)
            {
                Log.Error($"[SafePatcher] Error reloading patch: {e.Message}");
            }
        }
    }
}
