namespace Vocore.Graphics;

public struct TextureBindingInfo
{
    public TextureBindingInfo(TextureViewDimension viewDimension)
    {
        ViewDimension = viewDimension;
    }

    public TextureViewDimension ViewDimension { get; init; }

    public static readonly TextureBindingInfo Default2D = new()
    {
        ViewDimension = TextureViewDimension.Texture2D
    };

    public static readonly TextureBindingInfo None = new()
    {
        ViewDimension = TextureViewDimension.Undefined
    };
}