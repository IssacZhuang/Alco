using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Vocore.AssetsLib
{
    public class AnimatedMaterialPool
    {
        private static AnimatedMaterialPool _instance;
        public static AnimatedMaterialPool Default
        {
            get
            {
                if (_instance == null) _instance = new AnimatedMaterialPool();
                return _instance;
            }
        }

        private readonly List<Material> _materials = new List<Material>();

        public Material GetMaterial(Shader shader, Texture2D texture, Vector4 splits, float lightIntensity=1f)
        {
            if (shader == null) throw ExceptionRendering.ShaderNotFound(" Null shader");

            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i].shader != shader) continue;
                if (_materials[i].mainTexture != texture) continue;
                if (_materials[i].GetVector(ShaderPropertyID.splits) != splits) continue;
                if (_materials[i].GetFloat(ShaderPropertyID.lightIntensity) != lightIntensity) continue;
                return _materials[i];
            }

            Material result = MaterialUtility.CreateMaterial(shader, true);
            result.mainTexture = texture;
            result.SetVector(ShaderPropertyID.splits, splits);
            result.SetFloat(ShaderPropertyID.lightIntensity,lightIntensity);
            return result;
        }
    }
}
