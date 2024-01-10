namespace Vocore.Graphics
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
            Color = new BlendComponent(BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add),
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

        public static readonly BlendState NonPremultipliedAlpha = new BlendState
        {
            Color = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add),
            Alpha = new BlendComponent(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOperation.Add)
        };
    }
}