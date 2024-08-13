using System;
using Vocore.Graphics;

namespace Vocore.Engine
{
    /// <summary>
    /// The game engine setting
    /// </summary>
    public class GameEngineSetting
    {
        private readonly PriorityList<IEnginePlugin> _plugins = new PriorityList<IEnginePlugin>((x, y) => x.Order.CompareTo(y.Order));

        public IReadOnlyList<IEnginePlugin> Plugins => _plugins;

        public GameEngineSetting()
        {
            GametTickRate = 60;
            Window = WindowSetting.Default;
            Graphics = GraphicsSetting.Default;
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
        /// The rate of game logic tick
        /// </summary>
        public int GametTickRate;

        /// <summary>
        /// The engine will stop when error catched
        /// </summary>
        public bool StopWhenError;

        /// <summary>
        /// The engine will run once, then stop. Which mean the game will not loop.
        /// </summary> 
        public bool RunOnce;

        /// <summary>
        /// The window setting
        /// </summary>
        public WindowSetting Window;

        /// <summary>
        /// The graphics setting 
        /// </summary>
        public GraphicsSetting Graphics;

        /// <summary>
        /// The assets setting
        /// </summary>
        public AssetsSetting Assets;

        public Platform? Platform;

        public static GameEngineSetting CreateDefaultSDR()
        {
            GameEngineSetting seting = new GameEngineSetting();
            seting.With<PluginDefaultAssets>().
            With<PluginHDR>();
            return seting;
        }

        public static GameEngineSetting CreateDefaultHDR()
        {
            GameEngineSetting seting = new GameEngineSetting();
            seting.With<PluginDefaultAssets>().
            With<PluginHDR>();
            return seting;
        }

        public static GameEngineSetting CreateNoGPU()
        {
            return new GameEngineSetting
            {
                GametTickRate = 60,
                Graphics = GraphicsSetting.NoGPU,
                Assets = AssetsSetting.Default,
                Platform = new ConsolePlatform()
            }.With<PluginDefaultAssets>();
        }

        public static GameEngineSetting CreateGPUWithoutWindow()
        {
            return new GameEngineSetting
            {
                GametTickRate = 60,
                Graphics = GraphicsSetting.Default,
                Assets = AssetsSetting.Default,
                Platform = new ConsolePlatform()
            }.With<PluginDefaultAssets>().
            With<PluginSDR>();
        }


        public GameEngineSetting With(IEnginePlugin plugin)
        {
            _plugins.Add(plugin);
            return this;
        }

        public GameEngineSetting With<T>() where T : IEnginePlugin, new()
        {
            _plugins.Add(new T());
            return this;
        }
    }
}