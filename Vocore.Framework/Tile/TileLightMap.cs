using System;
using System.Runtime.CompilerServices;

using Unity.Mathematics;
using Vocore.Unsafe;

namespace Vocore
{
    public class TileLightMap
    {
        private static readonly int DefaultLightCapacity = 16;
        private static readonly ColorInt DefaultColor = new ColorInt(0, 0, 0, 255);
        private NativeArrayList<TileLight> _lights;
        private NativeBuffer<ColorInt> _colorsMap;
        private NativeBuffer<float> _transparencyMap;
        private int _width;
        private int _height;
        //the tile light map is rendered close to the center of the screen but snapped to the nearest grid
        private AABBInt _renderBounds;

        public TileLightMap(int width, int height)
        {
            _width = width;
            _height = height;
            _lights = new NativeArrayList<TileLight>(DefaultLightCapacity);
            _colorsMap = new NativeBuffer<ColorInt>(width * height);
            _transparencyMap = new NativeBuffer<float>(width * height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ColorInt GetColor(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return DefaultColor;
            }
            return _colorsMap[x + y * _width];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetColor(int x, int y, ColorInt color)
        {
            if (!InBounds(x, y))
            {
                return;
            }
            _colorsMap[x + y * _width] = color;
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < _width && y >= 0 && y < _height;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetTransparency(int x, int y)
        {
            return _transparencyMap[x + y * _width];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetTransparency(int x, int y, float transparency)
        {
            _transparencyMap[x + y * _width] = transparency;
        }

        public void Resize(int width, int height)
        {
            _width = width;
            _height = height;
            _colorsMap.Resize(width * height);
            _transparencyMap.Resize(width * height);
        }

        public bool AddLight(TileLight light)
        {
            if (_renderBounds.Contains(light.position))
            {
                AddLightNoBoundCheck(light);
                return true;
            }
            return false;
        }

        public void AddLightNoBoundCheck(TileLight light)
        {
            int2 renderPosition = light.position - _renderBounds.min;
            light.position = renderPosition;
            _lights.Add(light);
        }

        public unsafe void ResetBuffer()
        {
            _lights.Clear();
            ColorInt* ptr = _colorsMap.Ptr;
            UtilsMemory.Memset(ptr, DefaultColor, _colorsMap.Size);
        }

        public void SetCameraPosition(float3 center, int width, int height)
        {
            int halfWidth = width / 2;
            int halfHeight = height / 2;
            int x = (int)math.round(center.x) - halfWidth;
            int y = (int)math.round(center.y) - halfHeight;
            _renderBounds = new AABBInt(x, y, width, height);
            if (_width != width || _height != height)
            {
                Resize(width, height);
            }
        }

        private void FloorFillLight()
        {
            //parallel fllod fill

            int iterations = TileLight.MaxRadius;

            for (int i = 0; i < _lights.Count; i++)
            {
                SetColor(_lights[i].position.x, _lights[i].position.y, _lights[i].color);
            }

            for (int i = 0; i < _height; i++)
            {
                for (int j = 0; j < _width; j++)
                {
                    //search top, bottom, left, right
                    //if any of them is not transparent, then set the current tile to the average of the 4

                    ColorInt top = GetColor(j, i - 1);
                    ColorInt bottom = GetColor(j, i + 1);
                    ColorInt left = GetColor(j - 1, i);
                    ColorInt right = GetColor(j + 1, i);

                    ColorInt atteruation = TileLight.Attenuation;

                    ColorInt color = (top + bottom + left + right) - atteruation;
                    color *= GetTransparency(j, i);
                    SetColor(j, i, color);
                }
            }
        }

    }
}