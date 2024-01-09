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
    }
}