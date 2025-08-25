
namespace SharpMSDF.Atlas
{

    /// Type of atlas image contents
    public enum ImageType
    {
        /// Rendered glyphs without anti-aliasing (two colors only)
        HardMask,
        /// Rendered glyphs with anti-aliasing
        SoftMask,
        /// Signed (true) distance field
        SDF,
        /// Signed perpendicular distance field
        PSDF,
        /// Multi-channel signed distance field
        MSDF,
        /// Multi-channel & true signed distance field
        MTSDF
    };

    /// Atlas image encoding
    public enum ImageFormat
    {
        UNSPECIFIED,
        PNG,
        BMP,
        TIFF,
        RGBA,
        FL32,
        TEXT,
        TEXT_FLOAT,
        BINARY,
        BINARY_FLOAT,
        BINARY_FLOAT_BE
    };

    /// Glyph identification
    public enum GlyphIdentifierType
    {
        GlyphIndex,
        UnicodeCodepoint
    };

    /// Direction of the Y-axis
    public enum YDirection
    {
        BottomUp,
        TopDown
    };

    /// The method of computing the _Layout of the atlas
    public enum PackingStyle
    {
        Tight,
        Grid
    };

    /// Constraints for the atlas's dimensions - see size selectors for more info
    public enum DimensionsConstraint
    {
        None,
        Square,
        EvenSquare,
        MultipleOfFourSquare,
        PowerOfTwoRectangle,
        PowerOfTwoSquares
    };

}
