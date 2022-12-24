using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Verse;
using UnityEngine;
using Vocore.AssetsLib;

namespace MuzzleFlash
{
    public class MuzzleFlashDef : Def
    {
        [NoTranslate]
        public string texture;
        public Vector2 defaultSize;

        public Vector2 splits = new Vector2(5, 5);
        public Vector2 drawOffsetMultiplier = Vector2.zero;

        public int duration = 30;
        public float framesPerAnimation = 3;
        public float lightIntensity = 1f;

        private Texture2D _cacheTexture;
        private int _renderID = -1;

        public Texture2D Texture
        {
            get
            {
                if (_cacheTexture == null) _cacheTexture = ContentFinder<Texture2D>.Get(texture);
                return _cacheTexture;
            }
        }

        public int RenderID
        {
            get
            {
                if (_renderID < 0)
                {
                    InitializeMaterial();
                }
                return _renderID;
            }
        }

        public void InitializeMaterial()
        {
            Shader shader = AnimatedShaderPool.Default.AnimatedInstanced;
            Material mat = AnimatedMaterialPool.Default.GetMaterial(shader, Texture, splits, lightIntensity);
            _renderID = AnimatedRenderManager.Default.GetRendererID(MeshPool.plane10, mat);
        }
    }
}
