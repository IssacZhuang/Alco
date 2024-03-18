using System;
using System.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Windowing;
using Silk.NET.Maths;
using Vocore.Graphics;
using Vocore.Rendering;
using System.Diagnostics;





namespace Vocore.Engine
{
    /// <summary>
    /// The entry point for the game <br/>
    /// The integration of the game loop, base API, sdl window and graphics device
    /// </summary>
    public partial class GameEngine : IDisposable
    {
#pragma warning disable CS8618
        public static GameEngine Instance { get; private set; }
#pragma warning restore CS8618
        private GameEngineSetting _setting;

        #region  Resources
        private readonly GPUDevice _graphicsDevice;
        private readonly Window _window;
        private readonly AssetManager _assets;
        private readonly PriorityList<IEnginePlugin> _plugins = new PriorityList<IEnginePlugin>((x, y) => x.Priority.CompareTo(y.Priority));
        #endregion

        #region  Internal
        internal EngineGraphics _graphics;
        internal EngineTimer _timer;
        internal EngineProfiler _profiler;
        internal Input _input;

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

        public Window Window
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _window;
        }

        public Input Input
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _input;
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
                GraphicsWindow.CreateGraphicsDeviceWithWindow(
                _setting.Graphics,
                _setting.Window,
                out GPUDevice graphicsDevice,
                out SilkWindow slikWindow);

                ShaderResource.SetGlobalDevice(graphicsDevice);
                
                _window = slikWindow;
                _input = new SilkInput(slikWindow.InternalWindow);
                _graphicsDevice = graphicsDevice;

                _window.OnResize += (int2 size) =>
                {
                    _graphicsDevice.ResizeSurface((uint)size.x, (uint)size.y);

                    _setting.Window.Width = size.x;
                    _setting.Window.Height = size.y;
                    OnResize(size);
                };
            }
            else
            {
                _window = new NoWindow();
                _input  = new NoInput();
                _graphicsDevice = GraphicsFactory.GetNoGPUDevice();
            }

            Vector2 screenSizeFloat = new Vector2(_setting.Window.Width, _setting.Window.Height);
            _graphics = new EngineGraphics(this, screenSizeFloat);
            
            _timer = new EngineTimer(this);
            _profiler = new EngineProfiler(this);
            _assets = new AssetManager(2);
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
            //_window.Initialize();
            while (_isRunning)
            {
                _input.DoEvent();
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

        protected virtual void OnResize(int2 size)
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
#pragma warning disable CS8625
            Instance = null;
#pragma warning restore CS8625
            GraphicsDevice.Dispose();
            Window.Close();
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