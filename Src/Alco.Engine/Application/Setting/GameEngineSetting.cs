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
        private readonly PriorityList<IEnginePlugin> _plugins = new PriorityList<IEnginePlugin>((x, y) => x.Order.CompareTo(y.Order));

        /// <summary>
        /// Gets the engine plugins ordered by initialization priority.
        /// </summary>
        public IReadOnlyList<IEnginePlugin> Plugins => _plugins;

        /// <summary>
        /// Initializes engine settings with the default view, graphics, audio, and asset configuration.
        /// </summary>
        public GameEngineSetting()
        {
            GametTickRate = 60;
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
        /// The rate of game logic tick
        /// </summary>
        public int GametTickRate;

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
        /// Whether the engine runs in headless mode (no window, GPU rendering to an offscreen target).
        /// When true, the game skips window-only initialization and the HTTP API server starts
        /// independently of any LLM agent.
        /// </summary>
        public bool IsHeadless;

        /// <summary>
        /// Whether to start the HTTP game API server on engine start. Defaults to <c>true</c> for
        /// production and headless modes; tests set this to <c>false</c> to avoid binding a real port.
        /// </summary>
        public bool EnableGameApi = true;

        /// <summary>
        /// Creates the default standard-dynamic-range engine configuration.
        /// </summary>
        /// <returns>A configured engine setting.</returns>
        public static GameEngineSetting CreateDefaultSDR()
        {
            GameEngineSetting setting = new GameEngineSetting();
            setting.With<PluginDefaultAssets>().
            With<PluginHDR>();
            return setting;
        }

        /// <summary>
        /// Creates the default high-dynamic-range engine configuration.
        /// </summary>
        /// <returns>A configured engine setting.</returns>
        public static GameEngineSetting CreateDefaultHDR()
        {
            GameEngineSetting setting = new GameEngineSetting();
            setting.With<PluginDefaultAssets>().
            With<PluginHDR>();
            return setting;
        }

        /// <summary>
        /// Creates an engine configuration without graphics or audio devices.
        /// </summary>
        /// <returns>A configured engine setting.</returns>
        public static GameEngineSetting CreateNoGPU()
        {
            return new GameEngineSetting
            {
                GametTickRate = 60,
                Graphics = GraphicsSetting.NoGPU,
                Audio = AudioSetting.NoAudio,
                Assets = AssetsSetting.Default,
                Platform = new ConsolePlatform()
            }.With<PluginDefaultAssets>();
        }

        /// <summary>
        /// Creates an engine configuration with a graphics device but no view.
        /// </summary>
        /// <returns>A configured engine setting.</returns>
        public static GameEngineSetting CreateGPUWithoutView()
        {
            return new GameEngineSetting
            {
                GametTickRate = 60,
                Graphics = GraphicsSetting.Default,
                Assets = AssetsSetting.Default,
                Platform = new ConsolePlatform()
            }.With<PluginDefaultAssets>();
        }


        /// <summary>
        /// Adds a plugin to this configuration.
        /// </summary>
        /// <param name="plugin">Plugin instance to add.</param>
        /// <returns>This setting for fluent configuration.</returns>
        public GameEngineSetting With(IEnginePlugin plugin)
        {
            _plugins.Add(plugin);
            return this;
        }

        /// <summary>
        /// Creates and adds a plugin to this configuration.
        /// </summary>
        /// <typeparam name="T">Plugin type with a public parameterless constructor.</typeparam>
        /// <returns>This setting for fluent configuration.</returns>
        public GameEngineSetting With<T>() where T : IEnginePlugin, new()
        {
            _plugins.Add(new T());
            return this;
        }
    }
}
