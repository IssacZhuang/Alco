namespace Alco.Graphics;

public struct TextureBindingInfo
{
    public TextureBindingInfo(TextureViewDimension viewDimension, TextureSampleType sampleType = TextureSampleType.Float)
    {
        ViewDimension = viewDimension;
        SampleType = sampleType;
    }

    public TextureViewDimension ViewDimension { get; init; }
    public TextureSampleType SampleType { get; init; }
        
    public static readonly TextureBindingInfo Default2D = new(TextureViewDimension.Texture2D, TextureSampleType.Float);
    
    public static readonly TextureBindingInfo Depth2D = new(TextureViewDimension.Texture2D, TextureSampleType.Depth);

    public static readonly TextureBindingInfo None = new()
    {
        ViewDimension = TextureViewDimension.Undefined
    };
}