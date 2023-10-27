using System;
using Veldrid;
using Veldrid.Sdl2;

#pragma warning disable CS8618

namespace Vocore.Engine
{
    public static class Current
    {
        internal static Sdl2Window Window { get; set; }
        public static InputSnapshot InputSnapshot { get; internal set; }
        public static GraphicsDevice GraphicsDevice { get; internal set; }
        public static ResourceFactory ResourceFactory { get; internal set; }
        public static Engine Engine { get; internal set; }
        public static ICamera? Camera { get; set; }
    }
}