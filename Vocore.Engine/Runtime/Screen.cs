using System;
using System.Collections.Generic;

namespace Vocore.Engine
{
    public static class Screen
    {
        public static int Width => Current.Window.Width;
        public static int Height => Current.Window.Height;
        public static float AspectRatio => (float)Width / Height;
    }
}

