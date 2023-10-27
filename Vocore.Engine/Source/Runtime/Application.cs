using System;
using Veldrid.Sdl2;

namespace Vocore.Engine
{
    public static class Application
    {
        public static readonly string Path = AppDomain.CurrentDomain.BaseDirectory;
        public static int MainThread { get; internal set; }
        public static void Quit()
        {
            Current.Engine?.Stop();
        }

    }
}