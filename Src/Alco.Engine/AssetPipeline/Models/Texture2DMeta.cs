
using Alco.Graphics;

namespace Alco.Engine;

public class Texture2DMeta : Meta
{
    public FilterMode FilterMode { get; set; } = FilterMode.Linear;
    public AddressMode AddressMode { get; set; } = AddressMode.ClampToEdge;
}