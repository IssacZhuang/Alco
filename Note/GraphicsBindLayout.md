
## SPIRV Reflect resource binding

``` csharp
public struct DescriptorBinding
{
	public uint SpirvId;
	public unsafe byte* Name;
	public uint Binding;
	public uint InputAttachmentIndex;
	public uint Set;
	public DescriptorType DescriptorType;
	public ResourceType ResourceType;
	public ImageTraits Image;
	public BlockVariable Block;
	public BindingArrayTraits Array;
	public uint Count;
	public uint Accessed;
	public uint UavCounterId;
	public unsafe DescriptorBinding* UavCounterBinding;
	public unsafe TypeDescription* TypeDescription;
	public DescriptorBindingWordOffset WordOffset;
	public uint DecorationFlags;
}

public enum DescriptorType
{
	Sampler = 0,
	CombinedImageSampler = 1,
	SampledImage = 2,
	StorageImage = 3,
	UniformTexelBuffer = 4,
	StorageTexelBuffer = 5,
	UniformBuffer = 6,
	StorageBuffer = 7,
	UniformBufferDynamic = 8,
	StorageBufferDynamic = 9,
	InputAttachment = 10,
}

public enum ResourceType
{
	Undefined = 0,
	Sampler = 1,
	Cbv = 2,
	Srv = 4,
	Uav = 8
}

public struct ImageTraits
{
	public Dim Dim;
	public uint Depth;
	public uint Arrayed;
	public uint Ms;
	public uint Sampled;
	public ImageFormat ImageFormat;
}

public enum Dim
{
	Dim1D = 0,
	Dim2D = 1,
	Dim3D = 2,
	DimCube = 3,
	DimRect = 4,
	DimBuffer = 5,
	DimSubpassData = 6,
}


```

## WebGPU shader resource binding

``` csharp
public struct WGPUPipelineLayoutDescriptor
{
    public WGPUChainedStruct* nextInChain;
    public sbyte* label;
    public nuint bindGroupLayoutCount;
    public WGPUBindGroupLayout* bindGroupLayouts;
}
``` 

the WGPUBindGroupLayout is create by

``` csharp
public static WGPUBindGroupLayout wgpuDeviceCreateBindGroupLayout(
    WGPUDevice device, 
    WGPUBindGroupLayoutDescriptor* descriptor
    );
```

and the WGPUBindGroupLayoutDescriptor is

``` csharp
public struct WGPUBindGroupLayoutDescriptor
{
    public WGPUChainedStruct* nextInChain;
    public sbyte* label;
    public nuint entryCount;
    public WGPUBindGroupLayoutEntry* entries;
}

public struct WGPUBindGroupLayoutEntry
{
    public WGPUChainedStruct* nextInChain;
    public uint binding;
    public WGPUShaderStage visibility;
    public WGPUBufferBindingLayout buffer;
    public WGPUSamplerBindingLayout sampler;
    public WGPUTextureBindingLayout texture;
    public WGPUStorageTextureBindingLayout storageTexture;
}

public enum WGPUShaderStage
{
    None = 0,
    Vertex = 1,
    Fragment = 2,
    Compute = 4
}

// buffer
public struct WGPUBufferBindingLayout
{
    public WGPUChainedStruct* nextInChain;
    public WGPUBufferBindingType type;
    public WGPUBool hasDynamicOffset;
    public ulong minBindingSize;
}

public enum WGPUBufferBindingType
{
    Undefined = 0,
    Uniform = 1,
    Storage = 2,
    ReadOnlyStorage = 3
}

//sampler
public struct WGPUSamplerBindingLayout
{
    public WGPUChainedStruct* nextInChain;
    public WGPUSamplerBindingType type;
}

public enum WGPUSamplerBindingType
{
    Undefined = 0,
    Filtering = 1,
    NonFiltering = 2,
    Comparison = 3
}

// texture
public struct WGPUTextureBindingLayout
{
    public WGPUChainedStruct* nextInChain;
    public WGPUTextureSampleType sampleType;
    public WGPUTextureViewDimension viewDimension;
    public WGPUBool multisampled;
}

public enum WGPUTextureSampleType
{
    Undefined = 0,
    Float = 1,
    UnfilterableFloat = 2,
    Depth = 3,
    Sint = 4,
    Uint = 5
}

public enum WGPUTextureViewDimension
{
    Undefined = 0,
    _1D = 1,
    _2D = 2,
    _2DArray = 3,
    Cube = 4,
    CubeArray = 5,
    _3D = 6
}

// storage texture
public struct WGPUStorageTextureBindingLayout
{
    public WGPUChainedStruct* nextInChain;
    public WGPUStorageTextureAccess access;
    public WGPUTextureFormat format;
    public WGPUTextureViewDimension viewDimension;
}

public enum WGPUStorageTextureAccess
{
    Undefined = 0,
    WriteOnly = 1,
    ReadOnly = 2,
    ReadWrite = 3
}
```