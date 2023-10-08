using System;
using Veldrid.Sdl2;

namespace Vocore.Engine
{
    public static class Application
    {
        public static readonly string Path = AppDomain.CurrentDomain.BaseDirectory;

        public static void Quit()
        {
            Global.Window.Close();
        }

    }
}