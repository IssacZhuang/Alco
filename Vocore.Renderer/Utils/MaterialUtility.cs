using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Vocore.Renderer
{
    public static class MaterialUtility
    {
        public static Material CreateMaterial(Shader shader, bool instanced = false)
        {
            Material material = new Material(shader)
            {
                enableInstancing = instanced
            };
            return material;
        }

        public static Material CreateMaterial(Shader shader, Texture2D mainTexture, bool instanced = false)
        {
            Material material = CreateMaterial(shader, instanced);
            material.mainTexture = mainTexture;
            return material;
        }
    }
}
