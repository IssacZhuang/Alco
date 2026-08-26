using System;
using Alco.Audio;
using Alco.Graphics;

namespace Alco.Engine
{
    /// <summary>
    /// The game engine setting
    /// </summary>
    public class GameEngineSetting
    {
        /// <summary>
        /// Initializes engine settings with the default view, graphics, audio, and asset configuration.
        /// </summary>
        public GameEngineSetting()
        {
            View = ViewSetting.Default;
            Graphics = GraphicsSetting.Default;
            Audio = AudioSetting.Default;
            Assets = AssetsSetting.Default;
        }

        /// <summary>
        /// Check if the game engine requires GPU interface
        /// </summary>
        public bool HasGPU
        {
            get => Graphics.Backend != GraphicsBackend.None;
        }

        /// <summary>
        /// Check if the game engine requires audio interface
        /// </summary>
        public bool HasAudio
        {
            get => Audio.Backend != AudioBackend.None;
        }

        /// <summary>
        /// Gets or sets the main-loop frame-rate limit. A value less than or equal to zero disables frame limiting.
        /// </summary>
        public int TargetFrameRate { get; set; }

        /// <summary>
        /// The engine will stop when error catched
        /// </summary>
        public bool StopWhenError;

        /// <summary>
        /// The engine will run once, then stop. Which mean the game will not loop.
        /// </summary>
        public bool RunOnce;

        /// <summary>
        /// Gets or sets whether instrumented methods may be collected by the method profiler.
        /// </summary>
        public bool EnableMethodProfiling { get; set; }

        /// <summary>
        /// The view setting
        /// </summary>
        public ViewSetting View;

        /// <summary>
        /// The graphics setting
        /// </summary>
        public GraphicsSetting Graphics;

        /// <summary>
        /// The audio setting
        /// </summary>
        public AudioSetting Audio;

        /// <summary>
        /// The assets setting
        /// </summary>
        public AssetsSetting Assets;

        /// <summary>
        /// Optional platform implementation. The engine creates an SDL 3 platform when this is null.
        /// </summary>
        public Platform? Platform;

        /// <summary>
        /// Creates an engine configuration without graphics or audio devices.
        /// </summary>
        /// <returns>A configured engine setting.</returns>
        public static GameEngineSetting CreateNoGPU()
        {
            return new GameEngineSetting
            {
                Graphics = GraphicsSetting.NoGPU,
                Audio = AudioSetting.NoAudio,
                Assets = AssetsSetting.Default,
                Platform = new ConsolePlatform()
            };
        }

        /// <summary>
        /// Creates an engine configuration with a graphics device but no view.
        /// </summary>
        /// <returns>A configured engine setting.</returns>
        public static GameEngineSetting CreateGPUWithoutView()
        {
            return new GameEngineSetting
            {
                Graphics = GraphicsSetting.Default,
                Assets = AssetsSetting.Default,
                Platform = new ConsolePlatform()
            };
        }
    }
}
