using System;
using Vocore.Graphics;

namespace Vocore.Engine
{
    /// <summary>
    /// The game engine setting
    /// </summary>
    public struct GameEngineSetting
    {
        /// <summary>
        /// Check if the game engine requires GPU interface
        /// </summary>
        public bool HasGraphics
        {
            get => Graphics.Backend != GraphicsBackend.None;
        }

        /// <summary>
        /// The rate of game logic tick
        /// </summary>
        public int GametTickRate;

        /// <summary>
        /// The engine will stop when error catched
        /// </summary>
        public bool StopWhenError;
        
        /// <summary>
        /// The window setting
        /// </summary>
        public WindowSetting Window;

        /// <summary>
        /// The graphics setting 
        /// </summary>
        public GraphicsSetting Graphics;

        /// <summary>
        /// The default game engine setting
        /// </summary>
        public readonly static GameEngineSetting Default = new GameEngineSetting
        {
            GametTickRate = 60,
            Window = WindowSetting.Default,
            Graphics = GraphicsSetting.Default,
        };


        /// <summary>
        /// The default game engine setting but no GPU interface required
        /// </summary>
        public readonly static GameEngineSetting NoGraphics = new GameEngineSetting
        {
            GametTickRate = 60,
            Window = WindowSetting.Default,
            Graphics = GraphicsSetting.NoGPU,
        };
    }
}