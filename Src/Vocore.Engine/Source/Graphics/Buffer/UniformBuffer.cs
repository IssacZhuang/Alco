using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using Veldrid;

namespace Vocore.Engine
{
    public class UniformBuffer<T> : GpuBuffer<T>, IGpuResource where T : unmanaged
    {
        private readonly ResourceSet _resourceSet;
        public UniformBuffer(GraphicsDevice device) : base(device, BufferUsage.UniformBuffer)
        {
            ResourceLayout layout = device.ResourceFactory.CreateResourceLayout(BufferLayout.Uniform);
            _resourceSet = device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(layout, Buffer));
        }

        public ResourceSet ResourceSet
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _resourceSet;
            }
        }

        public override void UpdateToGPU(CommandList commandList)
        {
            base.UpdateToGPU(commandList);
        }
    }
}