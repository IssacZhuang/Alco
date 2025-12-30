namespace SharpMSDF.Atlas
{
    public struct Remap
    {
        public int Index;
        public (int X, int Y) Source, Target;
        public int Width, Height;
    };
}
