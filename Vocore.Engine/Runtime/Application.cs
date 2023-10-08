using System;
using Veldrid.Sdl2;

namespace Vocore.Engine
{
    public static class Application
    {
        public static void Quit()
        {
            Global.Window.Close();
        }

    }
}