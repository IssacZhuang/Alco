namespace Alco.Graphics
{
    /// <summary>
    /// The blend configuration applied to a render target: separate equations for
    /// color and alpha.
    /// </summary>
    public struct BlendState
    {
        /// <summary>
        /// Initializes the state with separate color and alpha blend equations.
        /// </summary>
        public BlendState(BlendComponent color, BlendComponent alpha)
        {
            Color = color;
            Alpha = alpha;
        }
        /// <summary>
        /// The blend equation for the RGB channels.
        /// </summary>
        public BlendComponent Color { get; init; }
        /// <summary>
        /// The blend equation for the alpha channel.
        /// </summary>
        public BlendComponent Alpha { get; init; }

        /// <summary>
        /// Opaque rendering: the source replaces the destination, no blending.
        /// </summary>
        public static readonly BlendState Opaque = new BlendState
        {
            Color = new BlendComponent(BlendFactor.One, BlendFactor.Zero, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.One, BlendFactor.Zero, BlendOperation.Add)
        };

        /// <summary>
        /// Alpha transparency: the source color is weighted by its alpha over the
        /// destination.
        /// </summary>
        public static readonly BlendState AlphaBlend = new BlendState
        {
            Color = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add)
        };

        /// <summary>
        /// Additive glow: the source scaled by its alpha is added onto the
        /// destination, brightening as geometry overlaps.
        /// </summary>
        public static readonly BlendState Additive = new BlendState
        {
            Color = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.One, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.One, BlendOperation.Add)
        };

        /// <summary>
        /// Blending for colors already premultiplied by their alpha.
        /// </summary>
        public static readonly BlendState PremultipliedAlpha = new BlendState
        {
            Color = new BlendComponent(BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add)
        };

        /// <summary>
        /// Straight (non-premultiplied) transparency: source and destination are
        /// weighted by source alpha in both color and alpha.
        /// </summary>
        public static readonly BlendState NonPremultipliedAlpha = new BlendState
        {
            Color = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add)
        };

        /// <summary>
        /// Multiply: source and destination colors are multiplied together.
        /// </summary>
        public static readonly BlendState Multiply = new BlendState
        {
            Color = new BlendComponent(BlendFactor.Dst, BlendFactor.Zero, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.One, BlendFactor.Zero, BlendOperation.Add)
        };

        /// <summary>
        /// Alpha blending that prevents alpha accumulation on overlapping geometry.
        /// Uses Max operation for alpha channel to avoid transparency stacking.
        /// </summary>
        public static readonly BlendState AlphaBlendNoAccumulation = new BlendState
        {
            Color = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.One, BlendFactor.One, BlendOperation.Max)
        };

        public static bool operator ==(BlendState left, BlendState right)
        {
            return left.Color == right.Color && left.Alpha == right.Alpha;
        }

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