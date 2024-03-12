using System;
using System.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Windowing;
using Silk.NET.Maths;
using Vocore.Graphics;
using Vocore.Rendering;
using System.Diagnostics;



#pragma warning disable CS8618
#pragma warning disable CS8625

namespace Vocore.Engine
{
    /// <summary>
    /// The entry point for the game <br/>
    /// The integration of the game loop, base API, sdl window and graphics device
    /// </summary>
    public partial class GameEngine : IDisposable
    {
        public static GameEngine Instance { get; private set; }
        private GameEngineSetting _setting;

        #region  Resources
        private readonly GPUDevice _graphicsDevice;
        private readonly AssetManager _assets;
        private readonly IWindow _window;
        private readonly PriorityList<IEnginePlugin> _plugins = new PriorityList<IEnginePlugin>((x, y) => x.Priority.CompareTo(y.Priority));
        #endregion

        #region  Internal
        internal EngineGraphics _graphics;
        internal EngineTimer _timer;
        internal EngineProfiler _profiler;
        internal EngineInput _input;

        #endregion

        #region API

        public EngineAPI_Window Window { get; private set; }
        public EngineInput Input
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _input;
        }


        #endregion

        #region  State
        private int _engineThread;
        private bool _isDisposed = false;
        private bool _isRunning = false;

        #endregion


        #region Properties

        /// <summary>
        /// The setting of the game engine<br/>
        /// Can only set in the constructor and modify by plugins before the engine starts
        /// </summary>
        public GameEngineSetting Setting
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _setting;
        }

        /// <summary>
        /// The main thread of the game main loop
        /// </summary>
        public int MainThread
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _engineThread;
        }

        /// <summary>
        /// Check if the current thread is the main thread
        /// </summary>
        public bool IsCalledFromMainThread
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _engineThread == Environment.CurrentManagedThreadId;
        }

        public static string WorkingDirectory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// The graphics device of the game<br/>
        /// Which provides the low level graphics API,<br/>
        /// It is dangerous to use if you not familiar with graphics programming
        /// </summary>
        public GPUDevice GraphicsDevice
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _graphicsDevice;
        }

        /// <summary>
        /// The asset manager of the game<br/>
        /// Which provides the asset loading and caching
        /// </summary>
        public AssetManager Assets
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _assets;
        }

        #endregion

        public GameEngine() : this(GameEngineSetting.Default)
        {
        }

        public GameEngine(GameEngineSetting setting)
        {
            if (Instance != null)
            {
                throw new Exception("The GameEngine can only have one instance.");
            }
            Instance = this;
            _setting = setting;

            if (_setting.HasGraphics)
            {
                GraphicsWindow.CreateGraphicsDeviceWithWindow(new WindowSetting
                {
                    Width = _setting.Width,
                    Height = _setting.Height,
                    Title = _setting.WindowName
                }, 
                _setting.GraphicsAPI,
                out GPUDevice graphicsDevice, 
                out IWindow window);
                
                ShaderResource.SetGlobalDevice(graphicsDevice);
                
                _window = window;
                _graphicsDevice = graphicsDevice;
                _assets = new AssetManager(2);

                //_window.Initialize();

                _window.Resize += (Vector2D<int> size) =>
                {
                    _graphicsDevice.ResizeSurface((uint)size.X, (uint)size.Y);

                    _setting.Width = size.X;
                    _setting.Height = size.Y;
                };
            }
            else
            {
                _window = null;
                _graphicsDevice = null;
            }
        }

        ~GameEngine()
        {
            Dispose();
        }

        #region  Lifecycle

        /// <summary>
        /// Start the main loop of the game
        /// </summary>
        [STAThread]
        public void Run()
        {
            _engineThread = Environment.CurrentManagedThreadId;
            _isRunning = true;

            InitializeInfrastructure();
            InitializeAPI();
            InitializePlugins();
            Assets.SetMainThread();
            
            InternaleRun();
        }


        /// <summary>
        /// The loop with graphics, which is used for the client
        /// </summary>
        private void InternaleRun()
        {
            InternalStart();
            _timer.Start();
            _window.Initialize();
            while (_isRunning)
            {
                _window.DoEvents();
                InternalUpdate();
            }
            InternalStop();
        }

        /// <summary>
        /// The start point of the game, which is called before the main loop
        /// </summary>
        protected virtual void OnStart()
        {

        }


        /// <summary>
        /// The game tick, which handles the game logic
        /// </summary>
        protected virtual void OnTick(float delta)
        {

        }


        /// <summary>
        /// The frame tick, which handles the frame logic and rendering
        /// </summary>
        protected virtual void OnUpdate(float delta)
        {

        }


        /// <summary>
        /// Called when player exit the game
        /// </summary>
        protected virtual void OnStop()
        {

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalTick(float delta)
        {
            try
            {
                OnTick(delta);
            }
            catch (Exception e)
            {
                Log.Error("[Tick Error]", e);
                Stop();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalUpdate()
        {
            _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);

            _input.Update();

            if (canInvokePhysicsTick)
            {
                try
                {
                    InternalTick(physicsDeltaTime);
                }
                catch (Exception e)
                {
                    Log.Error("[Tick Error]", e);
                    TryErrorStop();
                }
            }

            _graphics.BeginFrameUpdate(updateDeltaTime);

            try
            {
                OnUpdate(updateDeltaTime);
            }
            catch (Exception e)
            {
                Log.Error("[Update Error]", e);
                TryErrorStop();
            }

            Assets.OnUpdate();

            _graphics.EndFrame();

            _input.Reset();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalStart()
        {
            try
            {
                OnStart();
            }
            catch (Exception e)
            {
                Log.Error("[Start Error]", e);
                TryErrorStop();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalStop()
        {
            try
            {
                OnStop();
            }
            catch (Exception e)
            {
                Log.Error("[Stop Error]", e);
            }
        }

        private void TryErrorStop()
        {
            if (_setting.StopWhenError)
            {
                Stop();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Instance = null;
            GraphicsDevice.Dispose();
            _window.Close();
            _window.Dispose();
            Assets.Dispose();
            GC.SuppressFinalize(this);
        }

        private void InitializePlugins()
        {
            for (int i = 0; i < _plugins.Count; i++)
            {
                try
                {
                    _plugins[i].OnInitilize(this, ref _setting);
                }
                catch (Exception e)
                {
                    Log.Error($"Error when initialize plugin {_plugins[i].GetType().Name}: ");
                    Log.Error(e);
                }
            }
        }

        private void InitializeInfrastructure()
        {
            if (_setting.HasGraphics)
            {
                Vector2 screenSizeFloat = new Vector2(_setting.Width, _setting.Height);
                _graphics = new EngineGraphics(this, screenSizeFloat);
                _input = new EngineInput(_window);
            }

            _timer = new EngineTimer(this);
            _profiler = new EngineProfiler(this);
        }

        private void InitializeAPI()
        {
            Window = new EngineAPI_Window(_window);
        }

        #endregion

        #region API

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMainThread(int threadId)
        {
            return threadId == _engineThread;
        }

        public void RegisterPlugin<T>() where T : IEnginePlugin, new()
        {
            RegisterPlugin(new T());
        }

        public void RegisterPlugin(IEnginePlugin plugin)
        {
            if (_isRunning)
            {
                Log.Error("Cannot register plugin when the engine is running.");
            }

            try
            {
                _plugins.Add(plugin);
            }
            catch (Exception e)
            {
                Log.Error($"Error when register plugin {plugin.GetType().Name}: ");
                Log.Error(e);
            }
        }

        public void Stop()
        {
            _isRunning = false;
        }

        #endregion

        #region Internal

        #endregion
    }
}