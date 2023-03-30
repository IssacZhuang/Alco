using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public class TextureInfo
    {
        public static readonly Rect DefaultRect = new Rect(0, 0, 1, 1);
        public Texture2D atlas;
        public Rect rect = DefaultRect;

        public TextureInfo(Texture2D atlas, Rect rect)
        {
            this.atlas = atlas;
            this.rect = rect;
        }

        public TextureInfo(Texture2D atlas)
        {
            this.atlas = atlas;
            this.rect = DefaultRect;
        }
    }
}

