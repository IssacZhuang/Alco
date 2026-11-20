// Minimal Metal enumerations required by Alco.Graphics.

namespace Metal
{
    internal enum MTL4BlendState : long
    {
        Disabled = 0,
        Enabled = 1,
        Unspecialized = 2,
    }

    internal enum MTL4CounterHeapType : long
    {
        Invalid,
        Timestamp,
    }

    internal enum MTL4PipelineDataSetSerializerConfiguration : ulong
    {
        CaptureDescriptors = 1,
        CaptureBinaries = 1 << 1,
    }

    internal enum MTL4RenderEncoderOptions : ulong
    {
        None = 0,
        Suspending = 1,
        Resuming = 1 << 1,
    }

    internal enum MTL4TimestampGranularity : long
    {
        Relaxed = 0,
        Precise = 1,
    }

    internal enum MTL4VisibilityOptions : ulong
    {
        None = 0,
        Device = 1,
        ResourceAlias = 1 << 1,
    }

    internal enum MTLBlendFactor : ulong
    {
        Zero = 0,
        One = 1,
        SourceColor = 2,
        OneMinusSourceColor = 3,
        SourceAlpha = 4,
        OneMinusSourceAlpha = 5,
        DestinationColor = 6,
        OneMinusDestinationColor = 7,
        DestinationAlpha = 8,
        OneMinusDestinationAlpha = 9,
        SourceAlphaSaturated = 10,
        BlendColor = 11,
        OneMinusBlendColor = 12,
        BlendAlpha = 13,
        OneMinusBlendAlpha = 14,
        Source1Color = 15,
        OneMinusSource1Color = 16,
        Source1Alpha = 17,
        OneMinusSource1Alpha = 18,
        Unspecialized = 19,
    }

    internal enum MTLBlendOperation : ulong
    {
        Add = 0,
        Subtract = 1,
        ReverseSubtract = 2,
        Min = 3,
        Max = 4,
        Unspecialized = 5,
    }

    [Flags]
    internal enum MTLColorWriteMask : ulong
    {
        None = 0,
        Red = 1 << 3,
        Green = 1 << 2,
        Blue = 1 << 1,
        Alpha = 1,
        All = 15,
        Unspecialized = 1 << 4,
    }

    internal enum MTLCompareFunction : ulong
    {
        Never = 0,
        Less = 1,
        Equal = 2,
        LessEqual = 3,
        Greater = 4,
        NotEqual = 5,
        GreaterEqual = 6,
        Always = 7,
    }

    internal enum MTLCullMode : ulong
    {
        None = 0,
        Front = 1,
        Back = 2,
    }

    internal enum MTLGPUFamily : long
    {
        Apple1 = 1001,
        Apple2 = 1002,
        Apple3 = 1003,
        Apple4 = 1004,
        Apple5 = 1005,
        Apple6 = 1006,
        Apple7 = 1007,
        Apple8 = 1008,
        Apple9 = 1009,
        Apple10 = 1010,
        Mac1 = 2001,
        Mac2 = 2002,
        Common1 = 3001,
        Common2 = 3002,
        Common3 = 3003,
        MacCatalyst1 = 4001,
        MacCatalyst2 = 4002,
        Metal3 = 5001,
        Metal4 = 5002,
    }

    internal enum MTLHazardTrackingMode : ulong
    {
        Default = 0,
        Untracked = 1,
        Tracked = 2,
    }

    internal enum MTLHeapType : long
    {
        Automatic = 0,
        Placement = 1,
        Sparse = 2,
    }

    internal enum MTLIndexType : ulong
    {
        UInt16 = 0,
        UInt32 = 1,
    }

    internal enum MTLLanguageVersion : ulong
    {
        Version10 = 65536,
        Version11 = 65537,
        Version12 = 65538,
        Version20 = 131072,
        Version21 = 131073,
        Version22 = 131074,
        Version23 = 131075,
        Version24 = 131076,
        Version30 = 196608,
        Version31 = 196609,
        Version32 = 196610,
        Version40 = 262144,
    }

    internal enum MTLLoadAction : ulong
    {
        DontCare = 0,
        Load = 1,
        Clear = 2,
    }

    internal enum MTLPixelFormat : ulong
    {
        Invalid = 0,
        A8Unorm = 1,
        R8Unorm = 10,
        R8UnormsRGB = 11,
        R8Snorm = 12,
        R8Uint = 13,
        R8Sint = 14,
        R16Unorm = 20,
        R16Snorm = 22,
        R16Uint = 23,
        R16Sint = 24,
        R16Float = 25,
        RG8Unorm = 30,
        RG8UnormsRGB = 31,
        RG8Snorm = 32,
        RG8Uint = 33,
        RG8Sint = 34,
        B5G6R5Unorm = 40,
        A1BGR5Unorm = 41,
        ABGR4Unorm = 42,
        BGR5A1Unorm = 43,
        R32Uint = 53,
        R32Sint = 54,
        R32Float = 55,
        RG16Unorm = 60,
        RG16Snorm = 62,
        RG16Uint = 63,
        RG16Sint = 64,
        RG16Float = 65,
        RGBA8Unorm = 70,
        RGBA8UnormsRGB = 71,
        RGBA8Snorm = 72,
        RGBA8Uint = 73,
        RGBA8Sint = 74,
        BGRA8Unorm = 80,
        BGRA8UnormsRGB = 81,
        RGB10A2Unorm = 90,
        RGB10A2Uint = 91,
        RG11B10Float = 92,
        RGB9E5Float = 93,
        BGR10A2Unorm = 94,
        BGR10XR = 554,
        BGR10XRsRGB = 555,
        RG32Uint = 103,
        RG32Sint = 104,
        RG32Float = 105,
        RGBA16Unorm = 110,
        RGBA16Snorm = 112,
        RGBA16Uint = 113,
        RGBA16Sint = 114,
        RGBA16Float = 115,
        BGRA10XR = 552,
        BGRA10XRsRGB = 553,
        RGBA32Uint = 123,
        RGBA32Sint = 124,
        RGBA32Float = 125,
        BC1RGBA = 130,
        BC1RGBAsRGB = 131,
        BC2RGBA = 132,
        BC2RGBAsRGB = 133,
        BC3RGBA = 134,
        BC3RGBAsRGB = 135,
        BC4RUnorm = 140,
        BC4RSnorm = 141,
        BC5RGUnorm = 142,
        BC5RGSnorm = 143,
        BC6HRGBFloat = 150,
        BC6HRGBUfloat = 151,
        BC7RGBAUnorm = 152,
        BC7RGBAUnormsRGB = 153,
        PVRTCRGB2BPP = 160,
        PVRTCRGB2BPPsRGB = 161,
        PVRTCRGB4BPP = 162,
        PVRTCRGB4BPPsRGB = 163,
        PVRTCRGBA2BPP = 164,
        PVRTCRGBA2BPPsRGB = 165,
        PVRTCRGBA4BPP = 166,
        PVRTCRGBA4BPPsRGB = 167,
        EACR11Unorm = 170,
        EACR11Snorm = 172,
        EACRG11Unorm = 174,
        EACRG11Snorm = 176,
        EACRGBA8 = 178,
        EACRGBA8sRGB = 179,
        ETC2RGB8 = 180,
        ETC2RGB8sRGB = 181,
        ETC2RGB8A1 = 182,
        ETC2RGB8A1sRGB = 183,
        ASTC4x4sRGB = 186,
        ASTC5x4sRGB = 187,
        ASTC5x5sRGB = 188,
        ASTC6x5sRGB = 189,
        ASTC6x6sRGB = 190,
        ASTC8x5sRGB = 192,
        ASTC8x6sRGB = 193,
        ASTC8x8sRGB = 194,
        ASTC10x5sRGB = 195,
        ASTC10x6sRGB = 196,
        ASTC10x8sRGB = 197,
        ASTC10x10sRGB = 198,
        ASTC12x10sRGB = 199,
        ASTC12x12sRGB = 200,
        ASTC4x4LDR = 204,
        ASTC5x4LDR = 205,
        ASTC5x5LDR = 206,
        ASTC6x5LDR = 207,
        ASTC6x6LDR = 208,
        ASTC8x5LDR = 210,
        ASTC8x6LDR = 211,
        ASTC8x8LDR = 212,
        ASTC10x5LDR = 213,
        ASTC10x6LDR = 214,
        ASTC10x8LDR = 215,
        ASTC10x10LDR = 216,
        ASTC12x10LDR = 217,
        ASTC12x12LDR = 218,
        ASTC4x4HDR = 222,
        ASTC5x4HDR = 223,
        ASTC5x5HDR = 224,
        ASTC6x5HDR = 225,
        ASTC6x6HDR = 226,
        ASTC8x5HDR = 228,
        ASTC8x6HDR = 229,
        ASTC8x8HDR = 230,
        ASTC10x5HDR = 231,
        ASTC10x6HDR = 232,
        ASTC10x8HDR = 233,
        ASTC10x10HDR = 234,
        ASTC12x10HDR = 235,
        ASTC12x12HDR = 236,
        GBGR422 = 240,
        BGRG422 = 241,
        Depth16Unorm = 250,
        Depth32Float = 252,
        Stencil8 = 253,
        Depth24UnormStencil8 = 255,
        Depth32FloatStencil8 = 260,
        X32Stencil8 = 261,
        X24Stencil8 = 262,
        Unspecialized = 263,
    }

    internal enum MTLPrimitiveTopologyClass : ulong
    {
        Unspecified = 0,
        Point = 1,
        Line = 2,
        Triangle = 3,
    }

    internal enum MTLPrimitiveType : ulong
    {
        Point = 0,
        Line = 1,
        LineStrip = 2,
        Triangle = 3,
        TriangleStrip = 4,
    }

    [Flags]
    internal enum MTLRenderStages : ulong
    {
        RenderStageVertex = 1,
        RenderStageFragment = 1 << 1,
        RenderStageTile = 1 << 2,
        RenderStageObject = 1 << 3,
        RenderStageMesh = 1 << 4,
    }

    [Flags]
    internal enum MTLResourceOptions : ulong
    {
        ResourceCPUCacheModeDefaultCache = 0,
        ResourceCPUCacheModeWriteCombined = 1,
        ResourceStorageModeShared = 0,
        ResourceStorageModeManaged = 1 << 4,
        ResourceStorageModePrivate = 1 << 5,
        ResourceStorageModeMemoryless = 1 << 5,
        ResourceHazardTrackingModeDefault = 0,
        ResourceHazardTrackingModeUntracked = 1 << 8,
        ResourceHazardTrackingModeTracked = 1 << 9,
        CPUCacheModeDefault = 0,
        CPUCacheModeWriteCombined = 1,
    }

    internal enum MTLSamplerAddressMode : ulong
    {
        ClampToEdge = 0,
        MirrorClampToEdge = 1,
        Repeat = 2,
        MirrorRepeat = 3,
        ClampToZero = 4,
        ClampToBorderColor = 5,
    }

    internal enum MTLSamplerMinMagFilter : ulong
    {
        Nearest = 0,
        Linear = 1,
    }

    internal enum MTLSamplerMipFilter : ulong
    {
        NotMipmapped = 0,
        Nearest = 1,
        Linear = 2,
    }

    [Flags]
    internal enum MTLStages : ulong
    {
        StageVertex = 1,
        StageFragment = 1 << 1,
        StageTile = 1 << 2,
        StageObject = 1 << 3,
        StageMesh = 1 << 4,
        StageResourceState = 1 << 26,
        StageDispatch = 1 << 27,
        StageBlit = 1 << 28,
        StageAccelerationStructure = 1 << 29,
        StageMachineLearning = 1 << 30,
        StageAll = 9223372036854775807,
    }

    internal enum MTLStencilOperation : ulong
    {
        Keep = 0,
        Zero = 1,
        Replace = 2,
        IncrementClamp = 3,
        DecrementClamp = 4,
        Invert = 5,
        IncrementWrap = 6,
        DecrementWrap = 7,
    }

    internal enum MTLStorageMode : ulong
    {
        Shared = 0,
        Managed = 1,
        Private = 2,
        Memoryless = 3,
    }

    internal enum MTLStoreAction : ulong
    {
        DontCare = 0,
        Store = 1,
        MultisampleResolve = 2,
        StoreAndMultisampleResolve = 3,
        Unknown = 4,
        CustomSampleDepthStore = 5,
    }

    internal enum MTLTextureType : ulong
    {
        Type1D = 0,
        Type1DArray = 1,
        Type2D = 2,
        Type2DArray = 3,
        Type2DMultisample = 4,
        Cube = 5,
        CubeArray = 6,
        Type3D = 7,
        Type2DMultisampleArray = 8,
        TextureBuffer = 9,
    }

    [Flags]
    internal enum MTLTextureUsage : ulong
    {
        Unknown = 0,
        ShaderRead = 1,
        ShaderWrite = 1 << 1,
        RenderTarget = 1 << 2,
        PixelFormatView = 1 << 4,
        ShaderAtomic = 1 << 5,
    }

    internal enum MTLTriangleFillMode : ulong
    {
        Fill = 0,
        Lines = 1,
    }

    internal enum MTLWinding : ulong
    {
        Clockwise = 0,
        CounterClockwise = 1,
    }
}
