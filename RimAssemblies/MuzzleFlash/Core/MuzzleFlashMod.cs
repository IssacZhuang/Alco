using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using RimWorld;
using UnityEngine;

using Vocore.EmbeddedAssets;

namespace MuzzleFlash
{
    [StaticConstructorOnStartup]
    public class MuzzleFlashMod: Mod
    {
        public MuzzleFlashMod(ModContentPack content) : base(content)
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                AssetsManager.Default.Initialize();

                foreach(var shaderName in AssetsManager.Default.GetShaderNames())
                {
                    Log.Message("[Muzzle Flash] Shader loaded from Vocore: "+ shaderName);
                }

            }, "MF_LoadingShaders", false, null, true);

        }
    }
}
