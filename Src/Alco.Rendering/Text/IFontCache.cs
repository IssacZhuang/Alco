

using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;


public interface IFontCache
{
    public bool TryGetFont(string fontName, [NotNullWhen(true)] out Font? font);

    public Task<Exception?> AddOrUpdateAsync(string fontName, Font font);
}
