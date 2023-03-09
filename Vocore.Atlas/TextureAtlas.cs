using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Vocore.Atlas
{
    public class AtlasTile
    {
        public Texture2D atlas;
        public Rect rect;
    }

    public class TextureAtlas
    {
        public static int MaxPixelsPerAtlas
        {
            get
            {
                return MaxAtlasSize / 2 * (MaxAtlasSize / 2);
            }
        }
        public static int MaxAtlasSize
        {
            get
            {
                return SystemInfo.maxTextureSize;
            }
        }

        private Texture2D _atlas;

        private string _name;

        private List<Texture2D> _textures = new List<Texture2D>();
        private List<string> _fileName = new List<string>();
        private Dictionary<string, AtlasTile> _tiles = new Dictionary<string, AtlasTile>();

        public Texture2D Atlas
        {
            get
            {
                return _atlas;
            }
        }

        public IEnumerable<string> fileList
        {
            get
            {
                return _fileName;
            }
        }

        public TextureAtlas()
        {
            _name = "Unknown Atlas";
        }

        public TextureAtlas(string name)
        {
            _name = name;
        }

        public void Add(Texture2D texture, string fileName)
        {
            _textures.Add(texture);
            _fileName.Add(fileName);
        }

        public void Build(bool clearSource = false)
        {
            Texture2D[] arrayTextures = _textures.ToArray();
            _atlas = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            Rect[] rects = _atlas.PackTextures(arrayTextures, 8, MaxPixelsPerAtlas, false);
            //calc the tile info
            for (int i = 0; i < rects.Length; i++)
            {
                AtlasTile tile = new AtlasTile();
                tile.atlas = _atlas;
                tile.rect = rects[i];
                _tiles.Add(_fileName[i], tile);
            }

            if (clearSource)
            {
                ClearSource();
            }
        }

        public AtlasTile GetTile(string fileName)
        {
            if (_tiles.ContainsKey(fileName))
            {
                return _tiles[fileName];
            }
            return null;
        }

        private void ClearSource(){
            _textures.Clear();
            _fileName.Clear();
        }

        private void Reset()
        {
            _atlas = null;
            _textures.Clear();
            _fileName.Clear();
            _tiles.Clear();
        }
    }
}
