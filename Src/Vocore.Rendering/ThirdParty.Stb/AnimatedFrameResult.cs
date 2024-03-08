namespace StbImageSharp
{
#if !STBSHARP_INTERNAL
	public
#else
	internal
#endif
	class AnimatedFrameResult : ImageResult
	{
        public AnimatedFrameResult(byte[] data, int width, int height, ColorComponents comp, ColorComponents sourceComp) : base(data, width, height, comp, sourceComp)
        {
        }

        public int DelayInMs { get; set; }
	}
}