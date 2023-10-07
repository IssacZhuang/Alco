using System;
using System.Runtime.CompilerServices;

using UnityEngine;

using Vocore.Unsafe;

namespace Vocore
{
    public class TileLightMapGPU
    {
        [Serializable]
        public struct Setting
        {
            public int width;
            public int height;
            public int iterationCount;
            public float attenuation;

            public Setting(int width, int height, int iterationCount = 64, float attenuation = 1 / 64f)
            {
                this.width = width;
                this.height = height;
                this.iterationCount = iterationCount;
                this.attenuation = attenuation;
            }
        }
        public static readonly int ShaderId_fixedLight = Shader.PropertyToID("_fixedLight");
        public static readonly int ShaderId_bufferOrigin = Shader.PropertyToID("_bufferOrigin");
        public static readonly int ShaderId_bufferTarget = Shader.PropertyToID("_bufferTarget");
        public static readonly int ShaderId_transparencyMap = Shader.PropertyToID("_transparencyMap");
        public static readonly int ShaderId_attenuation = Shader.PropertyToID("_attenuation");
        public static readonly int ShaderId_width = Shader.PropertyToID("_width");
        public static readonly int ShaderId_height = Shader.PropertyToID("_height");
        public static readonly int ShaderId_renderTexture = Shader.PropertyToID("_output");
        public static readonly string Kernel_FloodFill = "FloodFill";
        public static readonly string Kernel_ResetBuffer = "ResetBuffer";
        public static readonly Vector3 DefaultColor = new Vector3(0, 0, 0);

        private StructuredBuffer<Vector3> _fixedLights;
        private StructuredBuffer<float> _transparencyMap;
        private int _width;
        private int _height;
        private AABBInt _renderBounds;
        private bool _isBufferSwapped;
        private int _iterationCount = 64;
        private float _attenuation = 1 / 64f;

        private ComputeBuffer _GPUBuffer1;
        private ComputeBuffer _GPUBuffer2;
        private ComputeBuffer _GPUFixedLights;
        private ComputeBuffer _GPUTransparencyMap;

        private ComputeShader _computeShader;
        private RenderTexture _renderTexture;
        private int _kernelFloodFill;
        private int3 _kenelThreadGroup_FloodFill;
        private int _kernelResetBuffer;
        private int3 _kenelThreadGroup_ResetBuffer;


        private ComputeBuffer GPUBufferOrigin
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _isBufferSwapped ? _GPUBuffer2 : _GPUBuffer1;
            }
        }

        private ComputeBuffer GPUBufferTarget
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _isBufferSwapped ? _GPUBuffer1 : _GPUBuffer2;
            }
        }

        public int Width => _width;
        public int Height => _height;

        public unsafe TileLightMapGPU(Setting setting, ComputeShader computeShader)
        {
            _computeShader = computeShader;

            UpdateSetting(setting);
        }

        ~TileLightMapGPU()
        {
            _GPUFixedLights.Dispose();
            _GPUTransparencyMap.Dispose();
            _GPUBuffer1.Dispose();
            _GPUBuffer2.Dispose();
        }

        public float GetTransparency(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return 0;
            }
            return _transparencyMap[x + y * _width];
        }

        private unsafe void SwapBuffer()
        {
            _isBufferSwapped = !_isBufferSwapped;
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < _width && y >= 0 && y < _height;
        }



        public bool AddLight(int2 postion, Vector3 color)
        {
            if (_renderBounds.Contains(postion))
            {
                AddLightNoBoundCheck(postion, color);
                return true;
            }
            return false;
        }

        public bool AddLight(int2 postion, Color color)
        {
            if (_renderBounds.Contains(postion))
            {
                AddLightNoBoundCheck(postion, new Vector3(color.r, color.g, color.b));
                return true;
            }
            return false;
        }

        public void AddLightNoBoundCheck(int2 postion, Vector3 color)
        {
            int2 renderPosition = postion - _renderBounds.min;
            _fixedLights[renderPosition.x + renderPosition.y * _width] = color;
        }

        public bool SetTransparency(int2 position, float transparency)
        {
            if (_renderBounds.Contains(position))
            {
                SetTransparencyNoBoundCheck(position, transparency);
                return true;
            }
            return false;
        }

        public void SetTransparencyNoBoundCheck(int2 position, float transparency)
        {
            int2 renderPosition = position - _renderBounds.min;
            _transparencyMap[renderPosition.x + renderPosition.y * _width] = transparency;
        }

        public Vector2 GetRenderPlanePosition()
        {
            //center of aabb
            return new Vector2(_renderBounds.min.x + (float)_renderBounds.Width / 2f, _renderBounds.min.y + (float)_renderBounds.Height / 2f);
        }

        public Vector3 GetRenderPlaneScale()
        {
            return new Vector3(_renderBounds.Width, _renderBounds.Height, 1);
        }

        public void OnFrame()
        {
            FloorFillLight();
            ResetFrame();
        }

        public void UpdateSetting(Setting setting)
        {
            _iterationCount = setting.iterationCount;
            _attenuation = setting.attenuation;
            if (_width != setting.width || _height != setting.height)
            {
                Resize(setting.width, setting.height);
            }
        }

        private void Resize(int width, int height)
        {
            _width = width;
            _height = height;

            int size = _width * _height;

            _renderBounds = new AABBInt(0, 0, _width, _height);
            _fixedLights = new StructuredBuffer<Vector3>(size);
            _transparencyMap = new StructuredBuffer<float>(size);

            _GPUBuffer1 = new ComputeBuffer(size, UtilsMemory.SizeOf<Vector3>());
            _GPUBuffer2 = new ComputeBuffer(size, UtilsMemory.SizeOf<Vector3>());
            _GPUFixedLights = new ComputeBuffer(size, UtilsMemory.SizeOf<Vector3>());

            _GPUTransparencyMap = new ComputeBuffer(size, sizeof(float));


            _kernelFloodFill = _computeShader.FindKernel(Kernel_FloodFill);
            _kernelResetBuffer = _computeShader.FindKernel(Kernel_ResetBuffer);
            uint sizeX, sizeY, sizeZ;
            _computeShader.GetKernelThreadGroupSizes(_kernelFloodFill, out sizeX, out sizeY, out sizeZ);
            _kenelThreadGroup_FloodFill = GetThreadGroup(sizeX, sizeY, sizeZ);
            _computeShader.GetKernelThreadGroupSizes(_kernelResetBuffer, out sizeX, out sizeY, out sizeZ);
            _kenelThreadGroup_ResetBuffer = GetThreadGroup(sizeX, sizeY, sizeZ);

            _renderTexture = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32);
            _renderTexture.enableRandomWrite = true;

            ResetTransparency();
        }

        private unsafe void ResetFrame()
        {
            ResetTransparency();
            ResetLight();
        }

        private int3 GetThreadGroup(uint sizeX, uint sizeY, uint sizeZ)
        {
            return new int3((int)(_width / sizeX) + 1, (int)(_height / sizeY) + 1, (int)sizeZ);
        }

        private unsafe void ResetTransparency()
        {
            for (int i = 0; i < _transparencyMap.Length; i++)
            {
                _transparencyMap.Raw[i] = 1f;
            }
        }

        private unsafe void ResetLight()
        {
            for (int i = 0; i < _fixedLights.Length; i++)
            {
                _fixedLights.Raw[i] = DefaultColor;
            }
        }

        private unsafe void FloorFillLight()
        {
            _GPUFixedLights.SetData(_fixedLights.Raw);
            _GPUTransparencyMap.SetData(_transparencyMap.Raw);

            _computeShader.SetInt(ShaderId_width, _width);
            _computeShader.SetInt(ShaderId_height, _height);
            _computeShader.SetFloat(ShaderId_attenuation, _attenuation);

            _computeShader.SetTexture(_kernelFloodFill, ShaderId_renderTexture, _renderTexture);

            _computeShader.SetBuffer(_kernelFloodFill, ShaderId_fixedLight, _GPUFixedLights);
            _computeShader.SetBuffer(_kernelResetBuffer, ShaderId_bufferOrigin, GPUBufferOrigin);
            _computeShader.SetBuffer(_kernelResetBuffer, ShaderId_bufferTarget, GPUBufferTarget);
            _computeShader.SetBuffer(_kernelFloodFill, ShaderId_transparencyMap, _GPUTransparencyMap);

            _computeShader.Dispatch(_kernelResetBuffer, _kenelThreadGroup_ResetBuffer.x, _kenelThreadGroup_ResetBuffer.y, _kenelThreadGroup_ResetBuffer.z);

            for (int loop = 0; loop < _iterationCount; loop++)
            {
                SwapBuffer();

                _computeShader.SetBuffer(_kernelFloodFill, ShaderId_bufferOrigin, GPUBufferOrigin);
                _computeShader.SetBuffer(_kernelFloodFill, ShaderId_bufferTarget, GPUBufferTarget);

                _computeShader.Dispatch(_kernelFloodFill, _kenelThreadGroup_FloodFill.x, _kenelThreadGroup_FloodFill.y, _kenelThreadGroup_FloodFill.z);
            }

        }

        public RenderTexture GetRenderTexture()
        {
            return _renderTexture;
        }
    }
}