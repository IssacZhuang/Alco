namespace Alco.Graphics;

public struct SamplerDescriptor
{
    public static readonly SamplerDescriptor Default = new(
        FilterMode.Linear,
        FilterMode.Linear,
        FilterMode.Linear,
        AddressMode.ClampToEdge,
        AddressMode.ClampToEdge,
        AddressMode.ClampToEdge,
        0,
        32f,
        CompareFunction.Undefined,
        1,
        "default_sampler"
        );

    public SamplerDescriptor(
        FilterMode minFilter,
        FilterMode magFilter,
        FilterMode mipFilter,
        AddressMode addressModeU,
        AddressMode addressModeV,
        AddressMode addressModeW,
        float lodMinClamp = 0,
        float lodMaxClamp = 32f,
        CompareFunction compare = CompareFunction.Undefined,
        ushort maxAnisotropy = 1,
        string name = "unamed_sampler"
        )
    {
        MinFilter = minFilter;
        MagFilter = magFilter;
        MipFilter = mipFilter;
        AddressModeU = addressModeU;
        AddressModeV = addressModeV;
        AddressModeW = addressModeW;
        LodMinClamp = lodMinClamp;
        LodMaxClamp = lodMaxClamp;
        Compare = compare;
        MaxAnisotropy = maxAnisotropy;
        Name = name;
    }

    public FilterMode MinFilter { get; init; }
    public FilterMode MagFilter { get; init; }
    public FilterMode MipFilter { get; init; }
    public AddressMode AddressModeU { get; init; }
    public AddressMode AddressModeV { get; init; }
    public AddressMode AddressModeW { get; init; }
    public float LodMinClamp { get; init; } = 0;
    public float LodMaxClamp { get; init; } = 32f;
    public CompareFunction Compare { get; init; } = CompareFunction.Undefined;
    public ushort MaxAnisotropy { get; init; } = 1;
    public string Name { get; init; } = "unamed_sampler";
}