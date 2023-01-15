using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using RimWorld;
using UnityEngine;

using Vocore.AssetsLib;

namespace MuzzleFlash
{
    [StaticConstructorOnStartup]
    public class MuzzleFlashMod: Mod
    {
        public MuzzleFlashMod(ModContentPack content) : base(content)
        {
            LongEventHandler.QueueLongEvent(() =>
            {
                foreach (AssetBundle asset in content.assetBundles.loadedAssetBundles)
                {
                    AnimatedShaderPool.Default.LoadAssetBundle(asset);
                }

                foreach(var shaderName in AnimatedShaderPool.Default.GetShaderNames())
                {
                    Log.Message("[Muzzle Flash] Custom shader loaded: "+ shaderName);
                }

            }, "MF_LoadingShaders", false, null, true);

        }
    }
}
