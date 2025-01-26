namespace Alco.Graphics
{
    public struct BlendState
    {
        public BlendState(BlendComponent color, BlendComponent alpha)
        {
            Color = color;
            Alpha = alpha;
        }
        public BlendComponent Color { get; init; }
        public BlendComponent Alpha { get; init; }

        // presets
        public static readonly BlendState Opaque = new BlendState
        {
            Color = new BlendComponent(BlendFactor.One, BlendFactor.Zero, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.One, BlendFactor.Zero, BlendOperation.Add)
        };

        public static readonly BlendState AlphaBlend = new BlendState
        {
            Color = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add)
        };

        public static readonly BlendState Additive = new BlendState
        {
            Color = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.One, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.One, BlendOperation.Add)
        };

        public static readonly BlendState PremultipliedAlpha = new BlendState
        {
            Color = new BlendComponent(BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add)
        };

        // Traditional transparency
        public static readonly BlendState NonPremultipliedAlpha = new BlendState
        {
            Color = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add)
        };

        //operator ==
        public static bool operator ==(BlendState left, BlendState right)
        {
            return left.Color == right.Color && left.Alpha == right.Alpha;
        }

        //operator !=
        public static bool operator !=(BlendState left, BlendState right)
        {
            return left.Color != right.Color || left.Alpha != right.Alpha;
        }

        public override bool Equals(object? obj)
        {
            return obj is BlendState state && this == state;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Color, Alpha);
        }
    }
}