namespace Vocore.Graphics;

public struct SamplerDescriptor
{
    public SamplerDescriptor(
        FilterMode minFilter,
        FilterMode magFilter,
        FilterMode mipFilter,
        AddressMode addressModeU,
        AddressMode addressModeV,
        AddressMode addressModeW,
        string name = "unamed_sampler"
        )
    {
        MinFilter = minFilter;
        MagFilter = magFilter;
        MipFilter = mipFilter;
        AddressModeU = addressModeU;
        AddressModeV = addressModeV;
        AddressModeW = addressModeW;
        Name = name;
    }

    public FilterMode MinFilter { get; init; }
    public FilterMode MagFilter { get; init; }
    public FilterMode MipFilter { get; init; }
    public AddressMode AddressModeU { get; init; }
    public AddressMode AddressModeV { get; init; }
    public AddressMode AddressModeW { get; init; }
    public string Name { get; init; } = "unamed_sampler";
}