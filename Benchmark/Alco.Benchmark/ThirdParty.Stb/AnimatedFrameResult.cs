namespace StbImageSharp
{
	internal class AnimatedFrameResult : ImageResult
	{
        public AnimatedFrameResult(byte[] data, int width, int height, ColorComponents comp, ColorComponents sourceComp) : base(data, width, height, comp, sourceComp)
        {
        }

        public int DelayInMs { get; set; }
	}
}