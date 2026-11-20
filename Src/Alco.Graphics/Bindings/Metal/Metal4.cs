// Minimal Metal 4 compiler, pipeline, command queue/buffer/encoder, pass, and counter bindings.

namespace Metal
{
    internal struct MTL4FunctionDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4FunctionDescriptor obj) => obj.NativePtr;

        public MTL4FunctionDescriptor(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTL4LibraryFunctionDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTL4FunctionDescriptor(MTL4LibraryFunctionDescriptor obj) => new(obj.NativePtr);

        public MTL4LibraryFunctionDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4LibraryFunctionDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLLibrary Library
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setLibrary, value);
        }

        public NSString Name
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setName, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setLibrary = "setLibrary:";
        private static readonly Selector sel_setName = "setName:";
    }

    internal struct MTL4LibraryDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4LibraryDescriptor obj) => obj.NativePtr;

        public MTL4LibraryDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4LibraryDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLCompileOptions Options
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setOptions, value);
        }

        public NSString Source
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSource, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setOptions = "setOptions:";
        private static readonly Selector sel_setSource = "setSource:";
    }

    internal struct MTL4PipelineDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4PipelineDescriptor obj) => obj.NativePtr;

        public MTL4PipelineDescriptor(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTL4RenderPipelineColorAttachmentDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public MTL4RenderPipelineColorAttachmentDescriptor(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLBlendOperation AlphaBlendOperation
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setAlphaBlendOperation, (ulong)value);
        }

        public MTL4BlendState BlendingState
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setBlendingState, (long)value);
        }

        public MTLBlendFactor DestinationAlphaBlendFactor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDestinationAlphaBlendFactor, (ulong)value);
        }

        public MTLBlendFactor DestinationRGBBlendFactor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDestinationRGBBlendFactor, (ulong)value);
        }

        public MTLPixelFormat PixelFormat
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setPixelFormat, (ulong)value);
        }

        public MTLBlendOperation RgbBlendOperation
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRgbBlendOperation, (ulong)value);
        }

        public MTLBlendFactor SourceAlphaBlendFactor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSourceAlphaBlendFactor, (ulong)value);
        }

        public MTLBlendFactor SourceRGBBlendFactor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSourceRGBBlendFactor, (ulong)value);
        }

        public MTLColorWriteMask WriteMask
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setWriteMask, (ulong)value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setAlphaBlendOperation = "setAlphaBlendOperation:";
        private static readonly Selector sel_setBlendingState = "setBlendingState:";
        private static readonly Selector sel_setDestinationAlphaBlendFactor = "setDestinationAlphaBlendFactor:";
        private static readonly Selector sel_setDestinationRGBBlendFactor = "setDestinationRGBBlendFactor:";
        private static readonly Selector sel_setPixelFormat = "setPixelFormat:";
        private static readonly Selector sel_setRgbBlendOperation = "setRgbBlendOperation:";
        private static readonly Selector sel_setSourceAlphaBlendFactor = "setSourceAlphaBlendFactor:";
        private static readonly Selector sel_setSourceRGBBlendFactor = "setSourceRGBBlendFactor:";
        private static readonly Selector sel_setWriteMask = "setWriteMask:";
    }

    internal struct MTL4RenderPipelineColorAttachmentDescriptorArray : IDisposable
    {
        public IntPtr NativePtr;

        public MTL4RenderPipelineColorAttachmentDescriptorArray(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTL4RenderPipelineColorAttachmentDescriptor Object(ulong attachmentIndex)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_objectAtIndexedSubscript, attachmentIndex));
        }

        private static readonly Selector sel_objectAtIndexedSubscript = "objectAtIndexedSubscript:";
        private static readonly Selector sel_release = "release";
    }

    internal struct MTL4RenderPipelineDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTL4PipelineDescriptor(MTL4RenderPipelineDescriptor obj) => new(obj.NativePtr);

        public MTL4RenderPipelineDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4RenderPipelineDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTL4RenderPipelineColorAttachmentDescriptorArray ColorAttachments => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_colorAttachments));

        public MTL4FunctionDescriptor FragmentFunctionDescriptor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setFragmentFunctionDescriptor, value);
        }

        public MTLPrimitiveTopologyClass InputPrimitiveTopology
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setInputPrimitiveTopology, (ulong)value);
        }

        public ulong RasterSampleCount
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRasterSampleCount, value);
        }

        public MTL4FunctionDescriptor VertexFunctionDescriptor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setVertexFunctionDescriptor, value);
        }

        private static readonly Selector sel_colorAttachments = "colorAttachments";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setFragmentFunctionDescriptor = "setFragmentFunctionDescriptor:";
        private static readonly Selector sel_setInputPrimitiveTopology = "setInputPrimitiveTopology:";
        private static readonly Selector sel_setRasterSampleCount = "setRasterSampleCount:";
        private static readonly Selector sel_setVertexFunctionDescriptor = "setVertexFunctionDescriptor:";
    }

    internal struct MTL4MeshRenderPipelineDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTL4PipelineDescriptor(MTL4MeshRenderPipelineDescriptor obj) => new(obj.NativePtr);

        public MTL4MeshRenderPipelineDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4MeshRenderPipelineDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTL4RenderPipelineColorAttachmentDescriptorArray ColorAttachments => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_colorAttachments));

        public MTL4FunctionDescriptor FragmentFunctionDescriptor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setFragmentFunctionDescriptor, value);
        }

        public MTL4FunctionDescriptor MeshFunctionDescriptor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setMeshFunctionDescriptor, value);
        }

        public MTL4FunctionDescriptor ObjectFunctionDescriptor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setObjectFunctionDescriptor, value);
        }

        public ulong RasterSampleCount
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRasterSampleCount, value);
        }

        public MTLSize RequiredThreadsPerMeshThreadgroup
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRequiredThreadsPerMeshThreadgroup, value);
        }

        public MTLSize RequiredThreadsPerObjectThreadgroup
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRequiredThreadsPerObjectThreadgroup, value);
        }

        private static readonly Selector sel_colorAttachments = "colorAttachments";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setFragmentFunctionDescriptor = "setFragmentFunctionDescriptor:";
        private static readonly Selector sel_setMeshFunctionDescriptor = "setMeshFunctionDescriptor:";
        private static readonly Selector sel_setObjectFunctionDescriptor = "setObjectFunctionDescriptor:";
        private static readonly Selector sel_setRasterSampleCount = "setRasterSampleCount:";
        private static readonly Selector sel_setRequiredThreadsPerMeshThreadgroup = "setRequiredThreadsPerMeshThreadgroup:";
        private static readonly Selector sel_setRequiredThreadsPerObjectThreadgroup = "setRequiredThreadsPerObjectThreadgroup:";
    }

    internal struct MTL4ComputePipelineDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4ComputePipelineDescriptor obj) => obj.NativePtr;

        public MTL4ComputePipelineDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4ComputePipelineDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTL4FunctionDescriptor ComputeFunctionDescriptor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setComputeFunctionDescriptor, value);
        }

        public MTLSize RequiredThreadsPerThreadgroup
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRequiredThreadsPerThreadgroup, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setComputeFunctionDescriptor = "setComputeFunctionDescriptor:";
        private static readonly Selector sel_setRequiredThreadsPerThreadgroup = "setRequiredThreadsPerThreadgroup:";
    }

    internal struct MTLRenderPipelineState : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLRenderPipelineState obj) => obj.NativePtr;

        public static implicit operator MTLAllocation(MTLRenderPipelineState obj) => new(obj.NativePtr);

        public MTLRenderPipelineState(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTLComputePipelineState : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLComputePipelineState obj) => obj.NativePtr;

        public static implicit operator MTLAllocation(MTLComputePipelineState obj) => new(obj.NativePtr);

        public MTLComputePipelineState(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTL4CompilerDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4CompilerDescriptor obj) => obj.NativePtr;

        public MTL4CompilerDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4CompilerDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTL4PipelineDataSetSerializer PipelineDataSetSerializer
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setPipelineDataSetSerializer, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setPipelineDataSetSerializer = "setPipelineDataSetSerializer:";
    }

    internal struct MTL4CompilerTaskOptions : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4CompilerTaskOptions obj) => obj.NativePtr;

        public MTL4CompilerTaskOptions()
        {
            var cls = new ObjectiveCClass("MTL4CompilerTaskOptions");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public NSArray LookupArchives
        {
            get => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_lookupArchives));
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setLookupArchives, value);
        }

        private static readonly Selector sel_lookupArchives = "lookupArchives";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setLookupArchives = "setLookupArchives:";
    }

    internal struct MTL4Compiler : IDisposable
    {
        public IntPtr NativePtr;

        public MTL4Compiler(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTL4PipelineDataSetSerializer PipelineDataSetSerializer => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_pipelineDataSetSerializer));

        public MTLComputePipelineState NewComputePipelineState(MTL4ComputePipelineDescriptor descriptor, MTL4CompilerTaskOptions compilerTaskOptions, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newComputePipelineStateWithDescriptorcompilerTaskOptionserror, descriptor, compilerTaskOptions, ref error.NativePtr));
        }

        public MTLLibrary NewLibrary(MTL4LibraryDescriptor descriptor, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newLibraryWithDescriptorerror, descriptor, ref error.NativePtr));
        }

        public MTLRenderPipelineState NewRenderPipelineState(MTL4PipelineDescriptor descriptor, MTL4CompilerTaskOptions compilerTaskOptions, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newRenderPipelineStateWithDescriptorcompilerTaskOptionserror, descriptor, compilerTaskOptions, ref error.NativePtr));
        }

        private static readonly Selector sel_newComputePipelineStateWithDescriptorcompilerTaskOptionserror = "newComputePipelineStateWithDescriptor:compilerTaskOptions:error:";
        private static readonly Selector sel_newLibraryWithDescriptorerror = "newLibraryWithDescriptor:error:";
        private static readonly Selector sel_newRenderPipelineStateWithDescriptorcompilerTaskOptionserror = "newRenderPipelineStateWithDescriptor:compilerTaskOptions:error:";
        private static readonly Selector sel_pipelineDataSetSerializer = "pipelineDataSetSerializer";
        private static readonly Selector sel_release = "release";
    }

    internal struct MTL4Archive : IDisposable
    {
        public IntPtr NativePtr;

        public MTL4Archive(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLComputePipelineState NewComputePipelineState(MTL4ComputePipelineDescriptor descriptor, ref NSError error)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newComputePipelineStateWithDescriptorerror, descriptor, ref error.NativePtr));
        }

        private static readonly Selector sel_newComputePipelineStateWithDescriptorerror = "newComputePipelineStateWithDescriptor:error:";
        private static readonly Selector sel_release = "release";
    }

    internal struct MTL4PipelineDataSetSerializerDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4PipelineDataSetSerializerDescriptor obj) => obj.NativePtr;

        public MTL4PipelineDataSetSerializerDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4PipelineDataSetSerializerDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTL4PipelineDataSetSerializerConfiguration Configuration
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setConfiguration, (ulong)value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setConfiguration = "setConfiguration:";
    }

    internal struct MTL4PipelineDataSetSerializer : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4PipelineDataSetSerializer obj) => obj.NativePtr;

        public MTL4PipelineDataSetSerializer(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public bool SerializeAsArchiveAndFlushToURL(NSURL url, ref NSError error)
        {
            return ObjectiveCRuntime.bool_objc_msgSend(NativePtr, sel_serializeAsArchiveAndFlushToURLerror, url, ref error.NativePtr);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_serializeAsArchiveAndFlushToURLerror = "serializeAsArchiveAndFlushToURL:error:";
    }

    internal struct MTL4CounterHeapDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4CounterHeapDescriptor obj) => obj.NativePtr;

        public MTL4CounterHeapDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4CounterHeapDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong Count
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setCount, value);
        }

        public MTL4CounterHeapType Type
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setType, (long)value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setCount = "setCount:";
        private static readonly Selector sel_setType = "setType:";
    }

    internal struct MTL4CounterHeap : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4CounterHeap obj) => obj.NativePtr;

        public MTL4CounterHeap(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public NSData ResolveCounterRange(NSRange range)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_resolveCounterRange, range));
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_resolveCounterRange = "resolveCounterRange:";
    }

    internal struct MTL4ArgumentTableDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4ArgumentTableDescriptor obj) => obj.NativePtr;

        public MTL4ArgumentTableDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4ArgumentTableDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public bool InitializeBindings
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setInitializeBindings, value);
        }

        public ulong MaxBufferBindCount
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setMaxBufferBindCount, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setInitializeBindings = "setInitializeBindings:";
        private static readonly Selector sel_setMaxBufferBindCount = "setMaxBufferBindCount:";
    }

    internal struct MTL4ArgumentTable : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4ArgumentTable obj) => obj.NativePtr;

        public MTL4ArgumentTable(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public void SetAddress(ulong gpuAddress, ulong bindingIndex)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setAddressatIndex, gpuAddress, bindingIndex);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setAddressatIndex = "setAddress:atIndex:";
    }

    internal struct MTL4CommandQueueDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4CommandQueueDescriptor obj) => obj.NativePtr;

        public MTL4CommandQueueDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4CommandQueueDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public IntPtr FeedbackQueue
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setFeedbackQueue, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setFeedbackQueue = "setFeedbackQueue:";
    }

    internal unsafe struct MTL4CommandQueue : IDisposable
    {
        public IntPtr NativePtr;

        public MTL4CommandQueue(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public void AddResidencySet(MTLResidencySet residencySet)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_addResidencySet, residencySet);
        }

        public void Commit(MTL4CommandBuffer[] commandBuffers, ulong count)
        {
            unsafe
            {
                fixed (MTL4CommandBuffer* commandBuffersPtr = commandBuffers)
                {
                    ObjectiveCRuntime.objc_msgSend(NativePtr, sel_commitcount, commandBuffersPtr, count);
                }
            }
        }

        public void RemoveResidencySet(MTLResidencySet residencySet)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_removeResidencySet, residencySet);
        }

        public void SignalDrawable(MTLDrawable drawable)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_signalDrawable, drawable);
        }

        public void SignalEvent(MTLEvent mtlEvent, ulong value)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_signalEventvalue, mtlEvent, value);
        }

        public void Wait(MTLEvent mtlEvent, ulong value)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_waitForEventvalue, mtlEvent, value);
        }

        public void Wait(MTLDrawable drawable)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_waitForDrawable, drawable);
        }

        private static readonly Selector sel_addResidencySet = "addResidencySet:";
        private static readonly Selector sel_commitcount = "commit:count:";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_removeResidencySet = "removeResidencySet:";
        private static readonly Selector sel_signalDrawable = "signalDrawable:";
        private static readonly Selector sel_signalEventvalue = "signalEvent:value:";
        private static readonly Selector sel_waitForDrawable = "waitForDrawable:";
        private static readonly Selector sel_waitForEventvalue = "waitForEvent:value:";
    }

    internal struct MTL4CommandAllocator : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4CommandAllocator obj) => obj.NativePtr;

        public MTL4CommandAllocator(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public void Reset()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_reset);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_reset = "reset";
    }

    internal struct MTL4CommandBuffer : IDisposable
    {
        public IntPtr NativePtr;

        public MTL4CommandBuffer(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTL4ComputeCommandEncoder ComputeCommandEncoder => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_computeCommandEncoder));

        public void BeginCommandBuffer(MTL4CommandAllocator allocator)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_beginCommandBufferWithAllocator, allocator);
        }

        public void EndCommandBuffer()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_endCommandBuffer);
        }

        public void PopDebugGroup()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_popDebugGroup);
        }

        public void PushDebugGroup(NSString nsString)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_pushDebugGroup, nsString);
        }

        public MTL4RenderCommandEncoder RenderCommandEncoder(MTL4RenderPassDescriptor descriptor, MTL4RenderEncoderOptions options)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_renderCommandEncoderWithDescriptoroptions, descriptor, (ulong)options));
        }

        public void WriteTimestampIntoHeap(MTL4CounterHeap counterHeap, ulong index)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_writeTimestampIntoHeapatIndex, counterHeap, index);
        }

        private static readonly Selector sel_beginCommandBufferWithAllocator = "beginCommandBufferWithAllocator:";
        private static readonly Selector sel_computeCommandEncoder = "computeCommandEncoder";
        private static readonly Selector sel_endCommandBuffer = "endCommandBuffer";
        private static readonly Selector sel_popDebugGroup = "popDebugGroup";
        private static readonly Selector sel_pushDebugGroup = "pushDebugGroup:";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_renderCommandEncoderWithDescriptoroptions = "renderCommandEncoderWithDescriptor:options:";
        private static readonly Selector sel_writeTimestampIntoHeapatIndex = "writeTimestampIntoHeap:atIndex:";
    }

    internal struct MTLRenderPassAttachmentDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public MTLRenderPassAttachmentDescriptor(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong DepthPlane
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDepthPlane, value);
        }

        public ulong Level
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setLevel, value);
        }

        public MTLLoadAction LoadAction
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setLoadAction, (ulong)value);
        }

        public ulong Slice
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSlice, value);
        }

        public MTLStoreAction StoreAction
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setStoreAction, (ulong)value);
        }

        public MTLTexture Texture
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setTexture, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setDepthPlane = "setDepthPlane:";
        private static readonly Selector sel_setLevel = "setLevel:";
        private static readonly Selector sel_setLoadAction = "setLoadAction:";
        private static readonly Selector sel_setSlice = "setSlice:";
        private static readonly Selector sel_setStoreAction = "setStoreAction:";
        private static readonly Selector sel_setTexture = "setTexture:";
    }

    internal struct MTLRenderPassColorAttachmentDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTLRenderPassAttachmentDescriptor(MTLRenderPassColorAttachmentDescriptor obj) => new(obj.NativePtr);

        public MTLRenderPassColorAttachmentDescriptor(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLClearColor ClearColor
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setClearColor, value);
        }

        public ulong ResolveLevel
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setResolveLevel, value);
        }

        public ulong ResolveSlice
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setResolveSlice, value);
        }

        public MTLTexture ResolveTexture
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setResolveTexture, value);
        }

        public MTLStoreAction StoreAction
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setStoreAction, (ulong)value);
        }

        public MTLTexture Texture
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setTexture, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setClearColor = "setClearColor:";
        private static readonly Selector sel_setResolveLevel = "setResolveLevel:";
        private static readonly Selector sel_setResolveSlice = "setResolveSlice:";
        private static readonly Selector sel_setResolveTexture = "setResolveTexture:";
        private static readonly Selector sel_setStoreAction = "setStoreAction:";
        private static readonly Selector sel_setTexture = "setTexture:";
    }

    internal struct MTLRenderPassColorAttachmentDescriptorArray : IDisposable
    {
        public IntPtr NativePtr;

        public MTLRenderPassColorAttachmentDescriptorArray(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLRenderPassColorAttachmentDescriptor Object(ulong attachmentIndex)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_objectAtIndexedSubscript, attachmentIndex));
        }

        private static readonly Selector sel_objectAtIndexedSubscript = "objectAtIndexedSubscript:";
        private static readonly Selector sel_release = "release";
    }

    internal struct MTLRenderPassDepthAttachmentDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTLRenderPassAttachmentDescriptor(MTLRenderPassDepthAttachmentDescriptor obj) => new(obj.NativePtr);

        public MTLRenderPassDepthAttachmentDescriptor(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public double ClearDepth
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setClearDepth, value);
        }

        public MTLTexture Texture
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setTexture, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setClearDepth = "setClearDepth:";
        private static readonly Selector sel_setTexture = "setTexture:";
    }

    internal struct MTLRenderPassStencilAttachmentDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTLRenderPassAttachmentDescriptor(MTLRenderPassStencilAttachmentDescriptor obj) => new(obj.NativePtr);

        public MTLRenderPassStencilAttachmentDescriptor(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public uint ClearStencil
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setClearStencil, value);
        }

        public MTLTexture Texture
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setTexture, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setClearStencil = "setClearStencil:";
        private static readonly Selector sel_setTexture = "setTexture:";
    }

    internal struct MTL4RenderPassDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTL4RenderPassDescriptor obj) => obj.NativePtr;

        public MTL4RenderPassDescriptor()
        {
            var cls = new ObjectiveCClass("MTL4RenderPassDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLRenderPassColorAttachmentDescriptorArray ColorAttachments => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_colorAttachments));

        public MTLRenderPassDepthAttachmentDescriptor DepthAttachment
        {
            get => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_depthAttachment));
        }

        public ulong RenderTargetHeight
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRenderTargetHeight, value);
        }

        public ulong RenderTargetWidth
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRenderTargetWidth, value);
        }

        public MTLRenderPassStencilAttachmentDescriptor StencilAttachment
        {
            get => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_stencilAttachment));
        }

        private static readonly Selector sel_colorAttachments = "colorAttachments";
        private static readonly Selector sel_depthAttachment = "depthAttachment";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setRenderTargetHeight = "setRenderTargetHeight:";
        private static readonly Selector sel_setRenderTargetWidth = "setRenderTargetWidth:";
        private static readonly Selector sel_stencilAttachment = "stencilAttachment";
    }

    internal struct MTL4ComputeCommandEncoder : IDisposable
    {
        public IntPtr NativePtr;

        public MTL4ComputeCommandEncoder(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public void BarrierAfterStages(MTLStages afterStages, MTLStages beforeQueueStages, MTL4VisibilityOptions visibilityOptions)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_barrierAfterStagesbeforeQueueStagesvisibilityOptions, (ulong)afterStages, (ulong)beforeQueueStages, (ulong)visibilityOptions);
        }

        public void CopyFromBuffer(MTLBuffer sourceBuffer, ulong sourceOffset, ulong sourceBytesPerRow, ulong sourceBytesPerImage, MTLSize sourceSize, MTLTexture destinationTexture, ulong destinationSlice, ulong destinationLevel, MTLOrigin destinationOrigin)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_copyFromBuffersourceOffsetsourceBytesPerRowsourceBytesPerImagesourceSizetoTexturedestinationSlicedestinationLeveldestinationOrigin, sourceBuffer, sourceOffset, sourceBytesPerRow, sourceBytesPerImage, sourceSize, destinationTexture, destinationSlice, destinationLevel, destinationOrigin);
        }

        public void CopyFromBuffer(MTLBuffer sourceBuffer, ulong sourceOffset, MTLBuffer destinationBuffer, ulong destinationOffset, ulong size)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_copyFromBuffersourceOffsettoBufferdestinationOffsetsize, sourceBuffer, sourceOffset, destinationBuffer, destinationOffset, size);
        }

        public void CopyFromTexture(MTLTexture sourceTexture, ulong sourceSlice, ulong sourceLevel, MTLOrigin sourceOrigin, MTLSize sourceSize, MTLTexture destinationTexture, ulong destinationSlice, ulong destinationLevel, MTLOrigin destinationOrigin)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_copyFromTexturesourceSlicesourceLevelsourceOriginsourceSizetoTexturedestinationSlicedestinationLeveldestinationOrigin, sourceTexture, sourceSlice, sourceLevel, sourceOrigin, sourceSize, destinationTexture, destinationSlice, destinationLevel, destinationOrigin);
        }

        public void CopyFromTexture(MTLTexture sourceTexture, ulong sourceSlice, ulong sourceLevel, MTLOrigin sourceOrigin, MTLSize sourceSize, MTLBuffer destinationBuffer, ulong destinationOffset, ulong destinationBytesPerRow, ulong destinationBytesPerImage)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_copyFromTexturesourceSlicesourceLevelsourceOriginsourceSizetoBufferdestinationOffsetdestinationBytesPerRowdestinationBytesPerImage, sourceTexture, sourceSlice, sourceLevel, sourceOrigin, sourceSize, destinationBuffer, destinationOffset, destinationBytesPerRow, destinationBytesPerImage);
        }

        public void DispatchThreadgroups(MTLSize threadgroupsPerGrid, MTLSize threadsPerThreadgroup)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_dispatchThreadgroupsthreadsPerThreadgroup, threadgroupsPerGrid, threadsPerThreadgroup);
        }

        public void DispatchThreadgroups(ulong indirectBuffer, MTLSize threadsPerThreadgroup)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_dispatchThreadgroupsWithIndirectBufferthreadsPerThreadgroup, indirectBuffer, threadsPerThreadgroup);
        }

        public void EndEncoding()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_endEncoding);
        }

        public void SetArgumentTable(MTL4ArgumentTable argumentTable)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setArgumentTable, argumentTable);
        }

        public void SetComputePipelineState(MTLComputePipelineState state)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setComputePipelineState, state);
        }

        private static readonly Selector sel_barrierAfterStagesbeforeQueueStagesvisibilityOptions = "barrierAfterStages:beforeQueueStages:visibilityOptions:";
        private static readonly Selector sel_copyFromBuffersourceOffsetsourceBytesPerRowsourceBytesPerImagesourceSizetoTexturedestinationSlicedestinationLeveldestinationOrigin = "copyFromBuffer:sourceOffset:sourceBytesPerRow:sourceBytesPerImage:sourceSize:toTexture:destinationSlice:destinationLevel:destinationOrigin:";
        private static readonly Selector sel_copyFromBuffersourceOffsettoBufferdestinationOffsetsize = "copyFromBuffer:sourceOffset:toBuffer:destinationOffset:size:";
        private static readonly Selector sel_copyFromTexturesourceSlicesourceLevelsourceOriginsourceSizetoBufferdestinationOffsetdestinationBytesPerRowdestinationBytesPerImage = "copyFromTexture:sourceSlice:sourceLevel:sourceOrigin:sourceSize:toBuffer:destinationOffset:destinationBytesPerRow:destinationBytesPerImage:";
        private static readonly Selector sel_copyFromTexturesourceSlicesourceLevelsourceOriginsourceSizetoTexturedestinationSlicedestinationLeveldestinationOrigin = "copyFromTexture:sourceSlice:sourceLevel:sourceOrigin:sourceSize:toTexture:destinationSlice:destinationLevel:destinationOrigin:";
        private static readonly Selector sel_dispatchThreadgroupsWithIndirectBufferthreadsPerThreadgroup = "dispatchThreadgroupsWithIndirectBuffer:threadsPerThreadgroup:";
        private static readonly Selector sel_dispatchThreadgroupsthreadsPerThreadgroup = "dispatchThreadgroups:threadsPerThreadgroup:";
        private static readonly Selector sel_endEncoding = "endEncoding";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setArgumentTable = "setArgumentTable:";
        private static readonly Selector sel_setComputePipelineState = "setComputePipelineState:";
    }

    internal struct MTL4RenderCommandEncoder : IDisposable
    {
        public IntPtr NativePtr;

        public MTL4RenderCommandEncoder(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public void DrawIndexedPrimitives(MTLPrimitiveType primitiveType, MTLIndexType indexType, ulong indexBuffer, ulong indexBufferLength, ulong indirectBuffer)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_drawIndexedPrimitivesindexTypeindexBufferindexBufferLengthindirectBuffer, (ulong)primitiveType, (ulong)indexType, indexBuffer, indexBufferLength, indirectBuffer);
        }

        public void DrawIndexedPrimitives(MTLPrimitiveType primitiveType, ulong indexCount, MTLIndexType indexType, ulong indexBuffer, ulong indexBufferLength, ulong instanceCount, long baseVertex, ulong baseInstance)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_drawIndexedPrimitivesindexCountindexTypeindexBufferindexBufferLengthinstanceCountbaseVertexbaseInstance, (ulong)primitiveType, indexCount, (ulong)indexType, indexBuffer, indexBufferLength, instanceCount, baseVertex, baseInstance);
        }

        public void DrawMeshThreadgroups(MTLSize threadgroupsPerGrid, MTLSize threadsPerObjectThreadgroup, MTLSize threadsPerMeshThreadgroup)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_drawMeshThreadgroupsthreadsPerObjectThreadgroupthreadsPerMeshThreadgroup, threadgroupsPerGrid, threadsPerObjectThreadgroup, threadsPerMeshThreadgroup);
        }

        public void DrawMeshThreadgroups(ulong indirectBuffer, MTLSize threadsPerObjectThreadgroup, MTLSize threadsPerMeshThreadgroup)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_drawMeshThreadgroupsWithIndirectBufferthreadsPerObjectThreadgroupthreadsPerMeshThreadgroup, indirectBuffer, threadsPerObjectThreadgroup, threadsPerMeshThreadgroup);
        }

        public void DrawPrimitives(MTLPrimitiveType primitiveType, ulong vertexStart, ulong vertexCount, ulong instanceCount, ulong baseInstance)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_drawPrimitivesvertexStartvertexCountinstanceCountbaseInstance, (ulong)primitiveType, vertexStart, vertexCount, instanceCount, baseInstance);
        }

        public void DrawPrimitives(MTLPrimitiveType primitiveType, ulong indirectBuffer)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_drawPrimitivesindirectBuffer, (ulong)primitiveType, indirectBuffer);
        }

        public void EndEncoding()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_endEncoding);
        }

        public void SetArgumentTable(MTL4ArgumentTable argumentTable, MTLRenderStages stages)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setArgumentTableatStages, argumentTable, (ulong)stages);
        }

        public void SetCullMode(MTLCullMode cullMode)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setCullMode, (ulong)cullMode);
        }

        public void SetDepthBias(float depthBias, float slopeScale, float clamp)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDepthBiasslopeScaleclamp, depthBias, slopeScale, clamp);
        }

        public void SetDepthStencilState(MTLDepthStencilState depthStencilState)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDepthStencilState, depthStencilState);
        }

        public void SetFrontFacingWinding(MTLWinding frontFacingWinding)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setFrontFacingWinding, (ulong)frontFacingWinding);
        }

        public void SetRenderPipelineState(MTLRenderPipelineState pipelineState)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRenderPipelineState, pipelineState);
        }

        public void SetScissorRect(MTLScissorRect rect)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setScissorRect, rect);
        }

        public void SetStencilReferenceValues(uint frontReferenceValue, uint backReferenceValue)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setStencilFrontReferenceValuebackReferenceValue, frontReferenceValue, backReferenceValue);
        }

        public void SetTriangleFillMode(MTLTriangleFillMode fillMode)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setTriangleFillMode, (ulong)fillMode);
        }

        public void SetViewport(MTLViewport viewport)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setViewport, viewport);
        }

        public void WriteTimestamp(MTL4TimestampGranularity granularity, MTLRenderStages stage, MTL4CounterHeap counterHeap, ulong index)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_writeTimestampWithGranularityafterStageintoHeapatIndex, (long)granularity, (ulong)stage, counterHeap, index);
        }

        private static readonly Selector sel_drawIndexedPrimitivesindexCountindexTypeindexBufferindexBufferLengthinstanceCountbaseVertexbaseInstance = "drawIndexedPrimitives:indexCount:indexType:indexBuffer:indexBufferLength:instanceCount:baseVertex:baseInstance:";
        private static readonly Selector sel_drawIndexedPrimitivesindexTypeindexBufferindexBufferLengthindirectBuffer = "drawIndexedPrimitives:indexType:indexBuffer:indexBufferLength:indirectBuffer:";
        private static readonly Selector sel_drawMeshThreadgroupsWithIndirectBufferthreadsPerObjectThreadgroupthreadsPerMeshThreadgroup = "drawMeshThreadgroupsWithIndirectBuffer:threadsPerObjectThreadgroup:threadsPerMeshThreadgroup:";
        private static readonly Selector sel_drawMeshThreadgroupsthreadsPerObjectThreadgroupthreadsPerMeshThreadgroup = "drawMeshThreadgroups:threadsPerObjectThreadgroup:threadsPerMeshThreadgroup:";
        private static readonly Selector sel_drawPrimitivesindirectBuffer = "drawPrimitives:indirectBuffer:";
        private static readonly Selector sel_drawPrimitivesvertexStartvertexCountinstanceCountbaseInstance = "drawPrimitives:vertexStart:vertexCount:instanceCount:baseInstance:";
        private static readonly Selector sel_endEncoding = "endEncoding";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setArgumentTableatStages = "setArgumentTable:atStages:";
        private static readonly Selector sel_setCullMode = "setCullMode:";
        private static readonly Selector sel_setDepthBiasslopeScaleclamp = "setDepthBias:slopeScale:clamp:";
        private static readonly Selector sel_setDepthStencilState = "setDepthStencilState:";
        private static readonly Selector sel_setFrontFacingWinding = "setFrontFacingWinding:";
        private static readonly Selector sel_setRenderPipelineState = "setRenderPipelineState:";
        private static readonly Selector sel_setScissorRect = "setScissorRect:";
        private static readonly Selector sel_setStencilFrontReferenceValuebackReferenceValue = "setStencilFrontReferenceValue:backReferenceValue:";
        private static readonly Selector sel_setTriangleFillMode = "setTriangleFillMode:";
        private static readonly Selector sel_setViewport = "setViewport:";
        private static readonly Selector sel_writeTimestampWithGranularityafterStageintoHeapatIndex = "writeTimestampWithGranularity:afterStage:intoHeap:atIndex:";
    }
}
