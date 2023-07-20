using System;
using System.Runtime.CompilerServices;

using Unity.Mathematics;
using Vocore.Unsafe;

using System.Threading.Tasks;

namespace Vocore
{
    public class TileLightMap
    {
        public struct MatrixLightWeight
        {
            public float r1;
            public float r2;
            public float r3;
            public float r4;
            public float g1;
            public float g2;
            public float g3;
            public float g4;
            public float b1;
            public float b2;
            public float b3;
            public float b4;
        }

        private static readonly int DefaultLightCapacity = 16;
        private NativeArrayList<TileLight> _lights;
        private NativeBuffer<LightSpread> _buffer1;
        private NativeBuffer<LightSpread> _buffer2;
        private NativeBuffer<float> _transparencyMap;
        private int _width;
        private int _height;
        private AABBInt _renderBounds;
        private bool _isBufferSwapped;
        private int _iterationCount = 32;
        private int _attenuation = 8;
        private float _mixMultiplier = 0.05f;


        private NativeBuffer<LightSpread> BufferOrigin
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _isBufferSwapped ? _buffer2 : _buffer1;
            }
        }

        private NativeBuffer<LightSpread> BufferTarget
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _isBufferSwapped ? _buffer1 : _buffer2;
            }
        }

        public int Width => _width;
        public int Height => _height;

        public unsafe TileLightMap(int width, int height)
        {
            _width = width;
            _height = height;
            _renderBounds = new AABBInt(0, 0, width, height);
            _lights = new NativeArrayList<TileLight>(DefaultLightCapacity);
            _buffer1 = new NativeBuffer<LightSpread>(width * height);
            _buffer2 = new NativeBuffer<LightSpread>(width * height);
            _transparencyMap = new NativeBuffer<float>(width * height);

            UtilsMemory.Memset(_buffer1.Ptr, LightSpread.Default, _buffer1.Size);
            UtilsMemory.Memset(_buffer2.Ptr, LightSpread.Default, _buffer2.Size);
        }

        ~TileLightMap()
        {
            _buffer1.Dispose();
            _buffer2.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LightSpread GetSpread(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return LightSpread.Default;
            }
            return BufferOrigin[x + y * _width];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LightColor GetColor(int x, int y)
        {
            return GetSpread(x, y).GetColor();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void SetTargetSpread(int x, int y, LightSpread spread)
        {
            if (!InBounds(x, y))
            {
                return;
            }
            BufferTarget.Ptr[x + y * _width] = spread;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void SetOriginSpread(int x, int y, LightSpread spread)
        {
            if (!InBounds(x, y))
            {
                return;
            }
            BufferOrigin.Ptr[x + y * _width] = spread;
        }

        private unsafe void SwapBuffer()
        {
            _isBufferSwapped = !_isBufferSwapped;
            //return;
            //clear the target buffer
            UtilsMemory.Memset(BufferTarget.Ptr, LightSpread.Default, BufferTarget.Size);
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
            _buffer1.Resize(width * height);
            _buffer2.Resize(width * height);
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
            Log.Info($"AddLightNoBoundCheck {light.position}");
            _lights.Add(light);
        }

        public unsafe void ResetBuffer()
        {
            _lights.Clear();
            LightSpread* ptr = _buffer1.Ptr;
            UtilsMemory.Memset(ptr, LightSpread.Default, _buffer1.Size);
            ptr = _buffer2.Ptr;
            UtilsMemory.Memset(ptr, LightSpread.Default, _buffer2.Size);
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

        public unsafe void FloorFillLight()
        {
            LightSpread target;

            LightColor fromTop = default;
            LightColor fromLeft = default;
            LightColor fromRight = default;
            LightColor fromBottom = default;

            int maxR = 0;
            int maxG = 0;
            int maxB = 0;

            int sumR = 0;
            int sumG = 0;
            int sumB = 0;

            MatrixLightWeight weight = default;

            for (int loop = 0; loop < _iterationCount; loop++)
            {
                SwapBuffer();
                for (int i = 0; i < _lights.Count; i++)
                {
                    TileLight light = _lights[i];
                    target = GetSpread(light.position.x, light.position.y);
                    target.fixedColor = light.color;
                    SetTargetSpread(light.position.x, light.position.y, target);
                    SetOriginSpread(light.position.x, light.position.y, target);
                }

                Parallel.For(0, _height * _width, (int i) =>
                {
                    int x = i % _width;
                    int y = i / _width;
                });

                for (int i = 0; i < _height; i++)
                {
                    for (int j = 0; j < _width; j++)
                    {
                        target = GetSpread(j, i);

                        fromTop = GetSpread(j, i - 1).GetColor();
                        fromLeft = GetSpread(j - 1, i).GetColor();
                        fromRight = GetSpread(j + 1, i).GetColor();
                        fromBottom = GetSpread(j, i + 1).GetColor();

                        //attenuation
                        fromTop -= _attenuation;
                        fromLeft -= _attenuation;
                        fromRight -= _attenuation;
                        fromBottom -= _attenuation;

                        //clamp
                        fromTop.ClampHDR();
                        fromLeft.ClampHDR();
                        fromRight.ClampHDR();
                        fromBottom.ClampHDR();

                        sumR = fromTop.r + fromLeft.r + fromRight.r + fromBottom.r;
                        sumG = fromTop.g + fromLeft.g + fromRight.g + fromBottom.g;
                        sumB = fromTop.b + fromLeft.b + fromRight.b + fromBottom.b;

                        weight.r1 = (float)fromTop.r / sumR;
                        weight.r2 = (float)fromLeft.r / sumR;
                        weight.r3 = (float)fromRight.r / sumR;
                        weight.r4 = (float)fromBottom.r / sumR;

                        weight.g1 = (float)fromTop.g / sumG;
                        weight.g2 = (float)fromLeft.g / sumG;
                        weight.g3 = (float)fromRight.g / sumG;
                        weight.g4 = (float)fromBottom.g / sumG;

                        weight.b1 = (float)fromTop.b / sumB;
                        weight.b2 = (float)fromLeft.b / sumB;
                        weight.b3 = (float)fromRight.b / sumB;
                        weight.b4 = (float)fromBottom.b / sumB;

                        target.dynamicColor.r = (int)(fromTop.r * weight.r1 + fromLeft.r * weight.r2 + fromRight.r * weight.r3 + fromBottom.r * weight.r4);
                        target.dynamicColor.g = (int)(fromTop.g * weight.g1 + fromLeft.g * weight.g2 + fromRight.g * weight.g3 + fromBottom.g * weight.g4);
                        target.dynamicColor.b = (int)(fromTop.b * weight.b1 + fromLeft.b * weight.b2 + fromRight.b * weight.b3 + fromBottom.b * weight.b4);

                        maxR = math.max(math.max(fromTop.r, fromLeft.r), math.max(fromRight.r, fromBottom.r)) - _attenuation;
                        maxG = math.max(math.max(fromTop.g, fromLeft.g), math.max(fromRight.g, fromBottom.g)) - _attenuation;
                        maxB = math.max(math.max(fromTop.b, fromLeft.b), math.max(fromRight.b, fromBottom.b)) - _attenuation;

                        target.dynamicColor.r = math.min(target.dynamicColor.r, maxR);
                        target.dynamicColor.g = math.min(target.dynamicColor.g, maxG);
                        target.dynamicColor.b = math.min(target.dynamicColor.b, maxB);

                        target.dynamicColor.ClampHDR();
                        target.fixedColor.ClampHDR();

                        SetTargetSpread(j, i, target);
                    }
                }


            }

        }
    }
}