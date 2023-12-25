using System;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    public class OffscreenBuffer : IDisposable, IGpuResource
    {
        private Framebuffer _framebuffer;
        private GraphicsDevice _device;
        private ResourceFactory _factory;
        private OutputDescription _outDesc;
        private ResourceSet _resourceSet;
        private uint _width;
        private uint _height;
        private SamplerMode _samplerMode;
        private PixelFormat[] _colors;
        private PixelFormat? _depth;
        private bool _disposed;
        public Framebuffer Framebuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _framebuffer;
        }

        public OutputDescription OutputDescription
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _outDesc;
        }

        public ResourceSet ResourceSet
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _resourceSet;
        }

        public OffscreenBuffer(GraphicsDevice device, uint width, uint height, PixelFormat[] colors, PixelFormat? depth = null, SamplerMode samplerMode = SamplerMode.Linear)
        {
            _device = device;
            _factory = device.ResourceFactory;

            _width = width;
            _height = height;
            _colors = colors;
            _depth = depth;

            _samplerMode = samplerMode;

            _framebuffer = CreateFramebuffer();
            _outDesc = new OutputDescription
            {
                ColorAttachments = new OutputAttachmentDescription[_colors.Length]
            };
            for (int i = 0; i < _colors.Length; i++)
            {
                _outDesc.ColorAttachments[i] = new OutputAttachmentDescription(_colors[i]);
            }

            if (_depth.HasValue)
            {
                _outDesc.DepthAttachment = new OutputAttachmentDescription(_depth.Value);
            }

            _resourceSet = CreateResourceSet();

            _disposed = false;
        }

        public static OffscreenBuffer CreateBySwapchainFramebuffer(GraphicsDevice device)
        {
            Framebuffer swapChainBuffer = device.SwapchainFramebuffer;
            Texture defaultColorTexture = swapChainBuffer.ColorTargets[0].Target;
            uint width = defaultColorTexture.Width;
            uint height = defaultColorTexture.Height;
            PixelFormat[] colors = new PixelFormat[swapChainBuffer.ColorTargets.Count];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = swapChainBuffer.ColorTargets[i].Target.Format;
            }

            PixelFormat? depth = null;
            if (swapChainBuffer.DepthTarget.HasValue)
            {
                depth = swapChainBuffer.DepthTarget.Value.Target.Format;
            }

            return new OffscreenBuffer(device, width, height, colors, depth);
        }

        ~OffscreenBuffer()
        {
            Dispose();
        }

        private Framebuffer CreateFramebuffer()
        {
            FramebufferDescription desc = new FramebufferDescription();

            if (_depth.HasValue)
            {
                Texture depthTexture = _factory.CreateTexture(TextureDescription.Texture2D(_width, _height, 1, 1, _depth.Value, TextureUsage.DepthStencil));
                desc.DepthTarget = new FramebufferAttachmentDescription(depthTexture, 0);
            }

            FramebufferAttachmentDescription[] colorAttachments = new FramebufferAttachmentDescription[_colors.Length];
            for (int i = 0; i < _colors.Length; i++)
            {
                Texture colorTexture = _factory.CreateTexture(TextureDescription.Texture2D(_width, _height, 1, 1, _colors[i], TextureUsage.RenderTarget | TextureUsage.Sampled));
                colorAttachments[i] = new FramebufferAttachmentDescription(colorTexture, 0);
            }
            desc.ColorTargets = colorAttachments;

            return _factory.CreateFramebuffer(desc);
        }

        private ResourceSet CreateResourceSet()
        {

            // texture and sampler
            int elementCount = (_colors.Length + (_depth.HasValue ? 1 : 0)) * 2;
            ResourceLayoutElementDescription[] elements = new ResourceLayoutElementDescription[elementCount];
            for (int i = 0; i < _colors.Length; i++)
            {
                elements[i * 2] = new ResourceLayoutElementDescription($"ColorTexture{i}", ResourceKind.TextureReadOnly, ShaderStages.Fragment);
                elements[i * 2 + 1] = new ResourceLayoutElementDescription($"ColorSampler{i}", ResourceKind.Sampler, ShaderStages.Fragment);
            }

            if (_framebuffer.DepthTarget.HasValue)
            {
                elements[_colors.Length * 2] = new ResourceLayoutElementDescription($"DepthTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment);
                elements[_colors.Length * 2 + 1] = new ResourceLayoutElementDescription($"DepthSampler", ResourceKind.Sampler, ShaderStages.Fragment);
            }

            ResourceLayout layout = _factory.CreateResourceLayout(new ResourceLayoutDescription(elements));

            ResourceSetDescription desc = new ResourceSetDescription(layout);
            desc.BoundResources = new BindableResource[elementCount];
            for (int i = 0; i < _colors.Length; i++)
            {
                desc.BoundResources[i * 2] = _framebuffer.ColorTargets[i].Target;
                desc.BoundResources[i * 2 + 1] = GetSampler();
            }

            if (_framebuffer.DepthTarget.HasValue)
            {
                desc.BoundResources[_colors.Length * 2] = _framebuffer.DepthTarget.Value.Target;
                desc.BoundResources[_colors.Length * 2 + 1] = GetSampler();
            }

            return _factory.CreateResourceSet(desc);
        }

        public void Resize(uint width, uint height)
        {
            _width = width;
            _height = height;

            _framebuffer.Dispose();
            _resourceSet.Dispose();

            _framebuffer = CreateFramebuffer();
            _resourceSet = CreateResourceSet();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _framebuffer.Dispose();
            GC.SuppressFinalize(this);
        }

        public void CopyBuffer(CommandList commandList, Framebuffer target)
        {
            int minColorsCount = math.min(_colors.Length, target.ColorTargets.Count);
            for (int i = 0; i < minColorsCount; i++)
            {
                commandList.CopyTexture(_framebuffer.ColorTargets[i].Target, target.ColorTargets[i].Target);
            }

            if (_framebuffer.DepthTarget.HasValue && target.DepthTarget.HasValue)
            {
                commandList.CopyTexture(_framebuffer.DepthTarget.Value.Target, target.DepthTarget.Value.Target);
            }
        }

        private Sampler GetSampler()
        {
            switch (_samplerMode)
            {
                case SamplerMode.Linear:
                    return _device.LinearSampler;
                case SamplerMode.Point:
                    return _device.PointSampler;
                case SamplerMode.Aniso4x:
                    return _device.Aniso4xSampler;
                default:
                    throw new Exception($"Invalid sampler mode.{_samplerMode}");
            }
        }
    }
}