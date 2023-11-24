using System;
using System.Threading;
using System.Numerics;
using System.Runtime.CompilerServices;

using Veldrid;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.Windowing.Extensions.Veldrid;
using Silk.NET.Maths;
using Silk.NET.Input.Glfw;
using Silk.NET.GLFW;



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
        private readonly IWindow _window;
        private readonly PriorityList<IEnginePlugin> _plugins = new PriorityList<IEnginePlugin>((x, y) => x.Priority.CompareTo(y.Priority));
        #endregion

        #region  Internal
        internal EngineGraphics _frame;
        internal EngineTimer _timer;
        internal EngineProfiler _profiler;
        internal EngineShaderContext _shaderContext;

        #endregion

        #region API

        public EngineAPI_Window Window { get; private set; }
        public EngineAPI_Input Input { get; private set; }
        public EngineAPI_Shader Shader { get; private set; }
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

        public ICamera? Camera
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _frame.Camera;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _frame.Camera = value;
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
                GlfwInput.RegisterPlatform();
                GlfwWindowing.RegisterPlatform();

                VeldridWindow.CreateWindowAndGraphicsDevice(WindowOptions.Default with
                {
                    Position = new Vector2D<int>(100, 100),
                    Size = new Vector2D<int>(_setting.width, _setting.height),
                    Title = _setting.windowName
                }, new GraphicsDeviceOptions
                {
                    SwapchainDepthFormat = CompatibilityHelper.GetPlatformDepthTestingFormat(),
                }, _setting.backend, out IWindow window, out GraphicsDevice graphicsDevice);

                _window = window;

                _graphicsDevice = graphicsDevice;

                _window.Initialize();

                _window.Resize += (Vector2D<int> size) =>
                {
                    _graphicsDevice.MainSwapchain.Resize((uint)size.X, (uint)size.Y);

                    Log.Info($"Window Resized {size.X}x{size.Y}");
                    _setting.width = size.X;
                    _setting.height = size.Y;
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
                InternalUpdate();
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
            _window.Update += (delta) =>
            {
                InternalUpdate();

            };

            _window.Render += (delta) =>
            {
                InternalDraw((float)delta);
            };

            _window.Closing += () =>
            {
                _isRunning = false;
            };
            
            _window.Run();
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

            if (canInvokePhysicsTick)
            {
                try
                {
                    InternalTick(physicsDeltaTime);
                }
                catch (Exception e)
                {
                    Log.Error("[Tick Error]", e);
                    Stop();
                }
            }
            try
            {
                OnUpdate(updateDeltaTime);
            }
            catch (Exception e)
            {
                Log.Error("[Update Error]", e);
                Stop();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InternalDraw(float delta)
        {
            try
            {
                _frame.BeginFrameUpdate(delta);
                OnDraw(delta);
                _frame.EndFrame();
            }
            catch (Exception e)
            {
                Log.Error("[Draw Error]", e);
                Stop();
            }
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
                Stop();
            }
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
                _shaderContext = new EngineShaderContext(GraphicsDevice);
            }

            _timer = new EngineTimer(this);
            _profiler = new EngineProfiler(this);
        }

        private void InitializeAPI()
        {
            Window = new EngineAPI_Window(_window);
            Input = new EngineAPI_Input(_window);
            Shader = new EngineAPI_Shader(_shaderContext);
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

        public void Stop()
        {
            if (_setting.hasGraphics)
            {
                _window.Close();
            }
            else
            {
                _isRunning = false;
            }
        }

        #endregion

        #region Internal

        #endregion
    }
}