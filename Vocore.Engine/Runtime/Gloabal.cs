using System;
using Veldrid;
using Veldrid.Sdl2;

#pragma warning disable CS8618

namespace Vocore.Engine
{
    internal static class Global
    {
        public static Sdl2Window Window;
        public static InputSnapshot InputSnapshot;
        public static GraphicsDevice GraphicsDevice;
        public static ResourceFactory ResourceFactory;
    }
}