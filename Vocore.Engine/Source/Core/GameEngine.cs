using System;
using System.Threading;
using System.Runtime.CompilerServices;

using Veldrid;
using Veldrid.Sdl2;
using System.Runtime.InteropServices;
using System.Numerics;

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
        private readonly GraphicsDevice _graphicsDevice;
        private readonly Sdl2Window _window;
        private readonly PriorityList<IEnginePlugin> _plugins = new PriorityList<IEnginePlugin>((x, y) => x.Priority.CompareTo(y.Priority));
        #endregion

        #region  Internal
        internal EngineGraphics _frame;
        internal EngineTimer _timer;
        internal EngineProfiler _profiler;

        #endregion

        #region API

        public EngineAPI_Window Window { get; private set; }
        public EngineAPI_Input Input { get; private set; }
        public EngineAPI_Graphics Graphics { get; private set; }

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
        public GraphicsDevice GraphicsDevice
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _graphicsDevice;
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

            if (_setting.hasGraphics)
            {
                WindowCreateInfo windowCreateInfo = new WindowCreateInfo
                {
                    X = 100,
                    Y = 100,
                    WindowWidth = _setting.width,
                    WindowHeight = _setting.height,
                    WindowTitle = _setting.windowName
                };

                _window = VeldridStartup.CreateWindow(ref windowCreateInfo);

                _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window, new GraphicsDeviceOptions
                {
                    SwapchainDepthFormat = PixelFormat.D32_Float_S8_UInt,
                }, _setting.backend);

                _window.Resized += () =>
                {
                    _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                    _setting.width = _window.Width;
                    _setting.height = _window.Height;
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

            if (_setting.hasGraphics)
            {
                RunWithGraphics();
            }
            else
            {
                RunWithoutGraphics();
            }
        }

        /// <summary>
        /// The loop without graphics, which is used for the server
        /// </summary>
        private void RunWithoutGraphics()
        {
            InternalStart();
            _timer.Start();
            while (_isRunning)
            {
                _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);
                if (canInvokePhysicsTick)
                {
                    InternalTick(physicsDeltaTime);
                }
                InternalUpdate(updateDeltaTime);
                //InternalDraw(updateDeltaTime);
            }
        }

        /// <summary>
        /// The loop with graphics, which is used for the client
        /// </summary>
        private void RunWithGraphics()
        {
            InternalStart();
            _timer.Start();
            while (_isRunning)
            {
                _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);
                if (canInvokePhysicsTick)
                {
                    InternalTick(physicsDeltaTime);
                }
                InternalUpdate(updateDeltaTime);
                InternalDraw(updateDeltaTime);
            }
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
        /// The frame tick, which handles the frame logic such as movement lerp and animation
        /// </summary>
        protected virtual void OnUpdate(float delta)
        {

        }

        /// <summary>
        /// The render tick, which handles the render logic such as draw calls
        /// </summary>
        protected virtual void OnDraw(float delta)
        {

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalTick(float delta)
        {
            OnTick(delta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalUpdate(float delta)
        {
            _window.PumpEvents();
            OnUpdate(delta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalDraw(float delta)
        {
            _frame.BeginFrameUpdate(delta);
            OnDraw(delta);
            _frame.EndFrame();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalStart()
        {
            OnStart();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Instance = null;
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
            if (_setting.hasGraphics)
            {
                Vector2 screenSizeFloat = new Vector2(_setting.width, _setting.height);
                _frame = new EngineGraphics(this, screenSizeFloat);
            }

            _timer = new EngineTimer(this);
            _profiler = new EngineProfiler(this);
        }

        private void InitializeAPI()
        {
            Window = new EngineAPI_Window(_window);
            Input = new EngineAPI_Input(_window);
            Graphics = new EngineAPI_Graphics(_graphicsDevice, _frame.CreateGlobalShaderDataResourceSet());
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

        #endregion

        #region Internal

        #endregion
    }
}