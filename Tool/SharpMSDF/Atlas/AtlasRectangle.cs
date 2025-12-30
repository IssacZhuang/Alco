namespace SharpMSDF.Atlas
{
    // Union of `Rectangle` and `OrientedRectangle` from original source
    public struct AtlasRectangle
    {
        public bool Rotated;
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public AtlasRectangle(int x, int y, int w, int h, bool isRotated = false)
        {
            X = x;
            Y = y;
            Width = w;
            Height = h;
            Rotated = isRotated;
        }

        public override string ToString() => Rotated? $"{X},{Y},{Width},{Height} (Rotated)" : $"{X},{Y},{Width},{Height}";
    }
}
