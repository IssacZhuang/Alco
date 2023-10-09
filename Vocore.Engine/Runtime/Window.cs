using System;
using System.Collections.Generic;

namespace Vocore.Engine
{
    public static class Window
    {
        public static int Width => RuntimeGlobal.Window.Width;
        public static int Height => RuntimeGlobal.Window.Height;
        public static float AspectRatio => (float)Width / Height;
    }
}

