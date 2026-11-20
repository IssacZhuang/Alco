// Minimal Metal value structures, resources, heaps, views, samplers, and state descriptors.

using System.Runtime.InteropServices;

namespace Metal
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MTLOrigin
    {
        public ulong x;
        public ulong y;
        public ulong z;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MTLSize
    {
        public ulong width;
        public ulong height;
        public ulong depth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MTLSizeAndAlign
    {
        public ulong size;
        public ulong align;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MTLResourceID
    {
        public ulong _impl;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MTLViewport
    {
        public double originX;
        public double originY;
        public double width;
        public double height;
        public double znear;
        public double zfar;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MTLScissorRect
    {
        public ulong x;
        public ulong y;
        public ulong width;
        public ulong height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MTLClearColor
    {
        public double red;
        public double green;
        public double blue;
        public double alpha;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MTLDrawPrimitivesIndirectArguments
    {
        public uint vertexCount;
        public uint instanceCount;
        public uint vertexStart;
        public uint baseInstance;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MTLDrawIndexedPrimitivesIndirectArguments
    {
        public uint indexCount;
        public uint instanceCount;
        public uint indexStart;
        public int baseVertex;
        public uint baseInstance;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MTL4TimestampHeapEntry
    {
        public ulong timestamp;
    }

    internal struct MTLEvent : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLEvent obj) => obj.NativePtr;

        public MTLEvent(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTLSharedEvent : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTLEvent(MTLSharedEvent obj) => new(obj.NativePtr);

        public MTLSharedEvent(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong SignaledValue
        {
            get => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_signaledValue);
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSignaledValue, value);
        }

        public bool WaitUntilSignaledValue(ulong value, ulong milliseconds)
        {
            return ObjectiveCRuntime.bool_objc_msgSend(NativePtr, sel_waitUntilSignaledValuetimeoutMS, value, milliseconds);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setSignaledValue = "setSignaledValue:";
        private static readonly Selector sel_signaledValue = "signaledValue";
        private static readonly Selector sel_waitUntilSignaledValuetimeoutMS = "waitUntilSignaledValue:timeoutMS:";
    }

    internal struct MTLAllocation : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLAllocation obj) => obj.NativePtr;

        public MTLAllocation(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTLResource : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTLAllocation(MTLResource obj) => new(obj.NativePtr);

        public MTLResource(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTLHeap : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTLAllocation(MTLHeap obj) => new(obj.NativePtr);

        public MTLHeap(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLBuffer NewBuffer(ulong length, MTLResourceOptions options, ulong offset)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newBufferWithLengthoptionsoffset, length, (ulong)options, offset));
        }

        public MTLTexture NewTexture(MTLTextureDescriptor descriptor, ulong offset)
        {
            return new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_newTextureWithDescriptoroffset, descriptor, offset));
        }

        private static readonly Selector sel_newBufferWithLengthoptionsoffset = "newBufferWithLength:options:offset:";
        private static readonly Selector sel_newTextureWithDescriptoroffset = "newTextureWithDescriptor:offset:";
        private static readonly Selector sel_release = "release";
    }

    internal struct MTLHeapDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLHeapDescriptor obj) => obj.NativePtr;

        public MTLHeapDescriptor()
        {
            var cls = new ObjectiveCClass("MTLHeapDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLHazardTrackingMode HazardTrackingMode
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setHazardTrackingMode, (ulong)value);
        }

        public MTLResourceOptions ResourceOptions
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setResourceOptions, (ulong)value);
        }

        public ulong Size
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSize, value);
        }

        public MTLStorageMode StorageMode
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setStorageMode, (ulong)value);
        }

        public MTLHeapType Type
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setType, (long)value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setHazardTrackingMode = "setHazardTrackingMode:";
        private static readonly Selector sel_setResourceOptions = "setResourceOptions:";
        private static readonly Selector sel_setSize = "setSize:";
        private static readonly Selector sel_setStorageMode = "setStorageMode:";
        private static readonly Selector sel_setType = "setType:";
    }

    internal struct MTLBuffer : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLBuffer obj) => obj.NativePtr;

        public static implicit operator MTLResource(MTLBuffer obj) => new(obj.NativePtr);

        public MTLBuffer(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public IntPtr Contents => new(ObjectiveCRuntime.IntPtr_objc_msgSend(NativePtr, sel_contents));

        public ulong GpuAddress => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_gpuAddress);

        private static readonly Selector sel_contents = "contents";
        private static readonly Selector sel_gpuAddress = "gpuAddress";
        private static readonly Selector sel_release = "release";
    }

    internal struct MTLTexture : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLTexture obj) => obj.NativePtr;

        public static implicit operator MTLResource(MTLTexture obj) => new(obj.NativePtr);

        public MTLTexture(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong Height => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_height);

        public MTLTextureType TextureType => (MTLTextureType)ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_textureType);

        public ulong Width => ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_width);

        private static readonly Selector sel_height = "height";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_textureType = "textureType";
        private static readonly Selector sel_width = "width";
    }

    internal struct MTLTextureDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLTextureDescriptor obj) => obj.NativePtr;

        public MTLTextureDescriptor()
        {
            var cls = new ObjectiveCClass("MTLTextureDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong ArrayLength
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setArrayLength, value);
        }

        public ulong Depth
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDepth, value);
        }

        public MTLHazardTrackingMode HazardTrackingMode
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setHazardTrackingMode, (ulong)value);
        }

        public ulong Height
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setHeight, value);
        }

        public ulong MipmapLevelCount
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setMipmapLevelCount, value);
        }

        public MTLPixelFormat PixelFormat
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setPixelFormat, (ulong)value);
        }

        public ulong SampleCount
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSampleCount, value);
        }

        public MTLStorageMode StorageMode
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setStorageMode, (ulong)value);
        }

        public MTLTextureType TextureType
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setTextureType, (ulong)value);
        }

        public MTLTextureUsage Usage
        {
            get => (MTLTextureUsage)ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_usage);
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setUsage, (ulong)value);
        }

        public ulong Width
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setWidth, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setArrayLength = "setArrayLength:";
        private static readonly Selector sel_setDepth = "setDepth:";
        private static readonly Selector sel_setHazardTrackingMode = "setHazardTrackingMode:";
        private static readonly Selector sel_setHeight = "setHeight:";
        private static readonly Selector sel_setMipmapLevelCount = "setMipmapLevelCount:";
        private static readonly Selector sel_setPixelFormat = "setPixelFormat:";
        private static readonly Selector sel_setSampleCount = "setSampleCount:";
        private static readonly Selector sel_setStorageMode = "setStorageMode:";
        private static readonly Selector sel_setTextureType = "setTextureType:";
        private static readonly Selector sel_setUsage = "setUsage:";
        private static readonly Selector sel_setWidth = "setWidth:";
        private static readonly Selector sel_usage = "usage";
    }

    internal struct MTLTextureViewDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLTextureViewDescriptor obj) => obj.NativePtr;

        public MTLTextureViewDescriptor()
        {
            var cls = new ObjectiveCClass("MTLTextureViewDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public NSRange LevelRange
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setLevelRange, value);
        }

        public MTLPixelFormat PixelFormat
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setPixelFormat, (ulong)value);
        }

        public NSRange SliceRange
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSliceRange, value);
        }

        public MTLTextureType TextureType
        {
            get => (MTLTextureType)ObjectiveCRuntime.ulong_objc_msgSend(NativePtr, sel_textureType);
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setTextureType, (ulong)value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setLevelRange = "setLevelRange:";
        private static readonly Selector sel_setPixelFormat = "setPixelFormat:";
        private static readonly Selector sel_setSliceRange = "setSliceRange:";
        private static readonly Selector sel_setTextureType = "setTextureType:";
        private static readonly Selector sel_textureType = "textureType";
    }

    internal struct MTLResourceViewPool : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLResourceViewPool obj) => obj.NativePtr;

        public MTLResourceViewPool(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTLResourceViewPoolDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLResourceViewPoolDescriptor obj) => obj.NativePtr;

        public MTLResourceViewPoolDescriptor()
        {
            var cls = new ObjectiveCClass("MTLResourceViewPoolDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong ResourceViewCount
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setResourceViewCount, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setResourceViewCount = "setResourceViewCount:";
    }

    internal struct MTLTextureViewPool : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator MTLResourceViewPool(MTLTextureViewPool obj) => new(obj.NativePtr);

        public MTLTextureViewPool(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLResourceID BaseResourceID => ObjectiveCRuntime.MTLResourceID_objc_msgSend(NativePtr, sel_baseResourceID);

        public MTLResourceID CopyResourceViewsFromPool(MTLResourceViewPool sourcePool, NSRange sourceRange, ulong destinationIndex)
        {
            return ObjectiveCRuntime.MTLResourceID_objc_msgSend(NativePtr, sel_copyResourceViewsFromPoolsourceRangedestinationIndex, sourcePool, sourceRange, destinationIndex);
        }

        public MTLResourceID SetTextureView(MTLTexture texture, MTLTextureViewDescriptor descriptor, ulong index)
        {
            return ObjectiveCRuntime.MTLResourceID_objc_msgSend(NativePtr, sel_setTextureViewdescriptoratIndex, texture, descriptor, index);
        }

        private static readonly Selector sel_baseResourceID = "baseResourceID";
        private static readonly Selector sel_copyResourceViewsFromPoolsourceRangedestinationIndex = "copyResourceViewsFromPool:sourceRange:destinationIndex:";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setTextureViewdescriptoratIndex = "setTextureView:descriptor:atIndex:";
    }

    internal struct MTLSamplerDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLSamplerDescriptor obj) => obj.NativePtr;

        public MTLSamplerDescriptor()
        {
            var cls = new ObjectiveCClass("MTLSamplerDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLCompareFunction CompareFunction
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setCompareFunction, (ulong)value);
        }

        public MTLSamplerMinMagFilter MagFilter
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setMagFilter, (ulong)value);
        }

        public ulong MaxAnisotropy
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setMaxAnisotropy, value);
        }

        public MTLSamplerMinMagFilter MinFilter
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setMinFilter, (ulong)value);
        }

        public MTLSamplerMipFilter MipFilter
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setMipFilter, (ulong)value);
        }

        public MTLSamplerAddressMode RAddressMode
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setRAddressMode, (ulong)value);
        }

        public MTLSamplerAddressMode SAddressMode
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSAddressMode, (ulong)value);
        }

        public bool SupportArgumentBuffers
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setSupportArgumentBuffers, value);
        }

        public MTLSamplerAddressMode TAddressMode
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setTAddressMode, (ulong)value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setCompareFunction = "setCompareFunction:";
        private static readonly Selector sel_setMagFilter = "setMagFilter:";
        private static readonly Selector sel_setMaxAnisotropy = "setMaxAnisotropy:";
        private static readonly Selector sel_setMinFilter = "setMinFilter:";
        private static readonly Selector sel_setMipFilter = "setMipFilter:";
        private static readonly Selector sel_setRAddressMode = "setRAddressMode:";
        private static readonly Selector sel_setSAddressMode = "setSAddressMode:";
        private static readonly Selector sel_setSupportArgumentBuffers = "setSupportArgumentBuffers:";
        private static readonly Selector sel_setTAddressMode = "setTAddressMode:";
    }

    internal struct MTLSamplerState : IDisposable
    {
        public IntPtr NativePtr;

        public MTLSamplerState(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLResourceID GpuResourceID => ObjectiveCRuntime.MTLResourceID_objc_msgSend(NativePtr, sel_gpuResourceID);

        private static readonly Selector sel_gpuResourceID = "gpuResourceID";
        private static readonly Selector sel_release = "release";
    }

    internal struct MTLStencilDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLStencilDescriptor obj) => obj.NativePtr;

        public MTLStencilDescriptor()
        {
            var cls = new ObjectiveCClass("MTLStencilDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLStencilOperation DepthFailureOperation
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDepthFailureOperation, (ulong)value);
        }

        public MTLStencilOperation DepthStencilPassOperation
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDepthStencilPassOperation, (ulong)value);
        }

        public uint ReadMask
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setReadMask, value);
        }

        public MTLCompareFunction StencilCompareFunction
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setStencilCompareFunction, (ulong)value);
        }

        public MTLStencilOperation StencilFailureOperation
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setStencilFailureOperation, (ulong)value);
        }

        public uint WriteMask
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setWriteMask, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setDepthFailureOperation = "setDepthFailureOperation:";
        private static readonly Selector sel_setDepthStencilPassOperation = "setDepthStencilPassOperation:";
        private static readonly Selector sel_setReadMask = "setReadMask:";
        private static readonly Selector sel_setStencilCompareFunction = "setStencilCompareFunction:";
        private static readonly Selector sel_setStencilFailureOperation = "setStencilFailureOperation:";
        private static readonly Selector sel_setWriteMask = "setWriteMask:";
    }

    internal struct MTLDepthStencilDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLDepthStencilDescriptor obj) => obj.NativePtr;

        public MTLDepthStencilDescriptor()
        {
            var cls = new ObjectiveCClass("MTLDepthStencilDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public MTLStencilDescriptor BackFaceStencil
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setBackFaceStencil, value);
        }

        public MTLCompareFunction DepthCompareFunction
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDepthCompareFunction, (ulong)value);
        }

        public MTLStencilDescriptor FrontFaceStencil
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setFrontFaceStencil, value);
        }

        public bool IsDepthWriteEnabled
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setDepthWriteEnabled, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setBackFaceStencil = "setBackFaceStencil:";
        private static readonly Selector sel_setDepthCompareFunction = "setDepthCompareFunction:";
        private static readonly Selector sel_setDepthWriteEnabled = "setDepthWriteEnabled:";
        private static readonly Selector sel_setFrontFaceStencil = "setFrontFaceStencil:";
    }

    internal struct MTLDepthStencilState : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLDepthStencilState obj) => obj.NativePtr;

        public MTLDepthStencilState(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        private static readonly Selector sel_release = "release";
    }

    internal struct MTLResidencySet : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLResidencySet obj) => obj.NativePtr;

        public MTLResidencySet(IntPtr ptr) => NativePtr = ptr;

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public void AddAllocation(MTLAllocation allocation)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_addAllocation, allocation);
        }

        public void Commit()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_commit);
        }

        public void RemoveAllocation(MTLAllocation allocation)
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_removeAllocation, allocation);
        }

        private static readonly Selector sel_addAllocation = "addAllocation:";
        private static readonly Selector sel_commit = "commit";
        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_removeAllocation = "removeAllocation:";
    }

    internal struct MTLResidencySetDescriptor : IDisposable
    {
        public IntPtr NativePtr;

        public static implicit operator IntPtr(MTLResidencySetDescriptor obj) => obj.NativePtr;

        public MTLResidencySetDescriptor()
        {
            var cls = new ObjectiveCClass("MTLResidencySetDescriptor");
            NativePtr = cls.AllocInit();
        }

        public void Dispose()
        {
            ObjectiveCRuntime.objc_msgSend(NativePtr, sel_release);
        }

        public ulong InitialCapacity
        {
            set => ObjectiveCRuntime.objc_msgSend(NativePtr, sel_setInitialCapacity, value);
        }

        private static readonly Selector sel_release = "release";
        private static readonly Selector sel_setInitialCapacity = "setInitialCapacity:";
    }
}
