using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Veldrid;

namespace Vocore.Engine
{
    public class Texture2D : IGraphicsResource
    {
        private readonly TextureView _textureView;
        private readonly Sampler _sampler;
        private readonly ResourceLayout _resourceLayout;
        private readonly ResourceSet _resourceSet;
        public ResourceSet ResourceSet
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _resourceSet;
        }
        public Texture2D(GraphicsDevice device, Stream stream, SamplerMode samplerMode = SamplerMode.Linear)
        {
            Texture texture = device.LoadTexture(stream);
            _textureView = device.ResourceFactory.CreateTextureView(texture);
            switch (samplerMode)
            {
                case SamplerMode.Linear:
                    _sampler = device.LinearSampler;
                    break;
                case SamplerMode.Point:
                    _sampler = device.PointSampler;
                    break;
                case SamplerMode.Aniso4x:
                    _sampler = device.Aniso4xSampler;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(samplerMode), samplerMode, null);
            }
            _resourceLayout = device.CreateTextureLayout();
            _resourceSet = device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_resourceLayout, _textureView, _sampler));
        }

        public static Texture2D FromStream(GraphicsDevice device, Stream stream, SamplerMode samplerMode = SamplerMode.Linear)
        {
            return new Texture2D(device, stream, samplerMode);
        }

    }
}

