// Minimal Metal device, shader library, and command queue/buffer types.

using System.Runtime.InteropServices;

namespace Metal
{
    internal partial struct MTLDevice : IDisposable
    {
        public IntPtr NativePtr;

        [LibraryImport(ObjectiveC.MetalFramework)]
        private static partial IntPtr MTLCreateSystemDefaultDevice();

        public static MTLDevice CreateSystemDefaultDevice()
        {
            return new(MTLCreateSystemDefaultDevice());
        }

        public static implicit operator IntPtr(MTLDevice obj) => obj.NativePtr;

        public MTLDevice(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong MaxArgumentBufferSamplerCount => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_maxArgumentBufferSamplerCount);

        public NSString Name => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_name));

        public MTL4CommandBuffer NewCommandBuffer => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newCommandBuffer));

        public ulong RegistryID => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_registryID);

        public bool SupportsBCTextureCompression => ObjectiveCRuntime.bool_objc_msgSend(NativePtr, sel_supportsBCTextureCompression);

        public MTLSizeAndAlign HeapBufferSizeAndAlign(ulong length, MTLResourceOptions options)
        {
            return ObjectiveCRuntime.MTLSizeAndAlign_objc_msgSend(NativePtr, sel_heapBufferSizeAndAlignWithLengthoptions, length, (ulong)options);
        }

        public MTLSizeAndAlign HeapTextureSizeAndAlign(MTLTextureDescriptor desc)
        {
            return ObjectiveCRuntime.MTLSizeAndAlign_objc_msgSend(NativePtr, sel_heapTextureSizeAndAlignWithDescriptor, desc);
        }

        public MTL4Archive NewArchive(NSURL url, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newArchiveWithURLerror, url, ref error.NativePtr));
        }

        public MTL4ArgumentTable NewArgumentTable(MTL4ArgumentTableDescriptor descriptor, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newArgumentTableWithDescriptorerror, descriptor, ref error.NativePtr));
        }

        public MTLBuffer NewBuffer(IntPtr pointer, ulong length, MTLResourceOptions options)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newBufferWithByteslengthoptions, pointer, length, (ulong)options));
        }

        public MTLBuffer NewBuffer(ulong length, MTLResourceOptions options)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newBufferWithLengthoptions, length, (ulong)options));
        }

        public MTL4CommandAllocator NewCommandAllocator()
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newCommandAllocator));
        }

        public MTLCommandQueue NewCommandQueue()
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newCommandQueue));
        }

        public MTL4Compiler NewCompiler(MTL4CompilerDescriptor descriptor, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newCompilerWithDescriptorerror, descriptor, ref error.NativePtr));
        }

        public MTL4CounterHeap NewCounterHeap(MTL4CounterHeapDescriptor descriptor, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newCounterHeapWithDescriptorerror, descriptor, ref error.NativePtr));
        }

        public MTLDepthStencilState NewDepthStencilState(MTLDepthStencilDescriptor descriptor)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newDepthStencilStateWithDescriptor, descriptor));
        }

        public MTLHeap NewHeap(MTLHeapDescriptor descriptor)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newHeapWithDescriptor, descriptor));
        }

        public MTLLibrary NewLibrary(IntPtr data, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newLibraryWithDataerror, data, ref error.NativePtr));
        }

        public MTL4CommandQueue NewMTL4CommandQueue(MTL4CommandQueueDescriptor descriptor, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newMTL4CommandQueueWithDescriptorerror, descriptor, ref error.NativePtr));
        }

        public MTL4PipelineDataSetSerializer NewPipelineDataSetSerializer(MTL4PipelineDataSetSerializerDescriptor descriptor)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newPipelineDataSetSerializerWithDescriptor, descriptor));
        }

        public MTLResidencySet NewResidencySet(MTLResidencySetDescriptor desc, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newResidencySetWithDescriptorerror, desc, ref error.NativePtr));
        }

        public MTLSamplerState NewSamplerState(MTLSamplerDescriptor descriptor)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newSamplerStateWithDescriptor, descriptor));
        }

        public MTLSharedEvent NewSharedEvent()
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newSharedEvent));
        }

        public MTLTextureViewPool NewTextureViewPool(MTLResourceViewPoolDescriptor descriptor, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newTextureViewPoolWithDescriptorerror, descriptor, ref error.NativePtr));
        }

        public bool SupportsFamily(MTLGPUFamily gpuFamily)
        {
            return ObjectiveCRuntime.bool_objc_msgSend(NativePtr, sel_supportsFamily, (long)gpuFamily);
        }

        private static readonly Selector sel_heapBufferSizeAndAlignWithLengthoptions = "heapBufferSizeAndAlignWithLength:options:";
        private static readonly Selector sel_heapTextureSizeAndAlignWithDescriptor = "heapTextureSizeAndAlignWithDescriptor:";
        private static readonly Selector sel_maxArgumentBufferSamplerCount = "maxArgumentBufferSamplerCount";
        private static readonly Selector sel_name = "name";
        private static readonly Selector sel_newArchiveWithURLerror = "newArchiveWithURL:error:";
        private static readonly Selector sel_newArgumentTableWithDescriptorerror = "newArgumentTableWithDescriptor:error:";
        private static readonly Selector sel_newBufferWithByteslengthoptions = "newBufferWithBytes:length:options:";
        private static readonly Selector sel_newBufferWithLengthoptions = "newBufferWithLength:options:";
        private static readonly Selector sel_newCommandAllocator = "newCommandAllocator";
        private static readonly Selector sel_newCommandBuffer = "newCommandBuffer";
        private static readonly Selector sel_newCommandQueue = "newCommandQueue";
        private static readonly Selector sel_newCompilerWithDescriptorerror = "newCompilerWithDescriptor:error:";
        private static readonly Selector sel_newCounterHeapWithDescriptorerror = "newCounterHeapWithDescriptor:error:";
        private static readonly Selector sel_newDepthStencilStateWithDescriptor = "newDepthStencilStateWithDescriptor:";
        private static readonly Selector sel_newHeapWithDescriptor = "newHeapWithDescriptor:";
        private static readonly Selector sel_newLibraryWithDataerror = "newLibraryWithData:error:";
        private static readonly Selector sel_newMTL4CommandQueueWithDescriptorerror = "newMTL4CommandQueueWithDescriptor:error:";
        private static readonly Selector sel_newPipelineDataSetSerializerWithDescriptor = "newPipelineDataSetSerializerWithDescriptor:";
        private static readonly Selector sel_newResidencySetWithDescriptorerror = "newResidencySetWithDescriptor:error:";
        private static readonly Selector sel_newSamplerStateWithDescriptor = "newSamplerStateWithDescriptor:";
        private static readonly Selector sel_newSharedEvent = "newSharedEvent";
        private static readonly Selector sel_newTextureViewPoolWithDescriptorerror = "newTextureViewPoolWithDescriptor:error:";
        private static readonly Selector sel_registryID = "registryID";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_supportsBCTextureCompression = "supportsBCTextureCompression";
        private static readonly Selector sel_supportsFamily = "supportsFamily:";
    }

    internal struct MTLLibrary : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLLibrary obj) => obj.NativePtr;

        public MTLLibrary(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTLCompileOptions : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLCompileOptions obj) => obj.NativePtr;

        public MTLCompileOptions()
        {
            var cls = new ObjectiveCClass("MTLCompileOptions");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLLanguageVersion LanguageVersion
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setLanguageVersion, (ulong)value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setLanguageVersion = "setLanguageVersion:";
    }

    internal struct MTLCommandQueue : IDisposable
    {
        public IntPtr NativePtr;

        public MTLCommandQueue(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLCommandBuffer CommandBuffer()
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_commandBuffer));
        }

        private static readonly Selector sel_commandBuffer = "commandBuffer";
        private static readonly Selector sel_release = "release";
    }

    internal struct MTLCommandBuffer : IDisposable
    {
        public IntPtr NativePtr;

        public MTLCommandBuffer(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public void Commit()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_commit);
        }

        public void EncodeSignalEvent(MTLEvent mtlEvent, ulong value)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_encodeSignalEventvalue, mtlEvent, value);
        }

        public void EncodeWait(MTLEvent mtlEvent, ulong value)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_encodeWaitForEventvalue, mtlEvent, value);
        }

        public void PresentDrawable(MTLDrawable drawable)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_presentDrawable, drawable);
        }

        private static readonly Selector sel_commit = "commit";
        private static readonly Selector sel_encodeSignalEventvalue = "encodeSignalEvent:value:";
        private static readonly Selector sel_encodeWaitForEventvalue = "encodeWaitForEvent:value:";
        private static readonly Selector sel_presentDrawable = "presentDrawable:";
        private static readonly Selector sel_release = "release";
    }
}
