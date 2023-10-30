using System;
using System.Numerics;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

#pragma warning disable CS8618

namespace Vocore.Engine
{
    public class Engine
    {
        private static readonly long frequency = Stopwatch.Frequency;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        
        private WindowCreateInfo _windowCreateInfo;
        private Sdl2Window _window;
        private GraphicsDevice _graphicsDevice;
        private GlobalGraphicsCommand _graphicsCommand;
        private CommandBuffer _commandBuffer;
        private Profiler _profiler;

        private RenderPipelineManager _renderPipelineManager;
        private ThreadManager _threadManager;

        private int _physicsFps = 30;
        private long physicsTickInterval;
        private float physicsDeltaTime;
        private bool _isRunning = true;

        protected RenderPipelineManager RenderPipelineManager
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _renderPipelineManager;
            }
        }

        protected Sdl2Window Window
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _window;
            }
        }

        protected GraphicsDevice GraphicsDevice
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _graphicsDevice;
            }
        }

        protected CommandBuffer Graphics
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _commandBuffer;
            }
        }

        public Profiler Profiler
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _profiler;
            }
        }

        public ThreadManager ThreadManager
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _threadManager;
            }
        }

        public int PhysicsTickRate
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _physicsFps;
            }
            set
            {
                UpdatePhysicsTickRate(value);
            }
        }

        public bool IsRunning
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _isRunning;
            }
        }

        public bool Fullscreen
        {
            get
            {
                return _window.WindowState == WindowState.BorderlessFullScreen;
            }
            set
            {
                if (value)
                {
                    _window.WindowState = WindowState.BorderlessFullScreen;
                }
                else
                {
                    _window.WindowState = WindowState.Normal;
                }
            }
        }

        public Engine(GraphicsBackend backend, string name = "Vocore")
        {
            Init(backend, name);
        }

        public Engine(string name = "Vocore")
        {
            Init(VeldridStartup.GetPlatformDefaultBackend(), name);
        }

        [STAThread]
        public void Run()
        {
            Application.MainThread = Environment.CurrentManagedThreadId;

            try
            {
                OnStart();
            }
            catch (Exception e)
            {
                Log.Error("Startup Error: ", e);
                OnQuit();
                return;
            }

            UpdatePhysicsTickRate(_physicsFps);

            long _timer = 0;
            long lastTime = 0;
            long delta = 0;
            float deltaTime = 0;

            _stopwatch.Start();

            while (_window.Exists)
            {
                delta = _stopwatch.ElapsedTicks - lastTime;
                lastTime = _stopwatch.ElapsedTicks;
                _timer += delta;
                deltaTime = (float)delta / frequency;

                PumpInput();

                if (_timer >= physicsTickInterval)
                {
                    _timer -= physicsTickInterval;
                    try
                    {
                        OnTick(physicsDeltaTime);

                    }
                    catch (Exception e)
                    {
                        Log.Error("Tick Error: ", e);
                    }
                }

                try
                {

                    _graphicsCommand.BeginFrame();
                    _graphicsCommand.Update(deltaTime);
                    OnUpdate(deltaTime);
                    _renderPipelineManager.OnDraw(_graphicsCommand.CommandList);
                    _threadManager.Update();
                    _graphicsCommand.EndFrame();
                }
                catch (Exception e)
                {
                    Log.Error("Update Error: ", e);
                    return;
                }
                Profiler.Update(deltaTime);

                if (!_isRunning)
                {
                    _window.Close();
                }
            }


            // while (_window.clos);

            OnQuit();
            InternalQuit();
            _window.Close();
        }

        protected virtual void OnStart()
        {

        }

        protected virtual void OnTick(float delta)
        {

        }

        protected virtual void OnUpdate(float delta)
        {

        }

        protected virtual void OnWindowResize(int width, int height)
        {

        }

        protected virtual void OnQuit()
        {

        }

        private void Init(GraphicsBackend backend, string name)
        {
            if (Current.Engine != null)
            {
                throw new Exception("You can only have one engine instance");
            }

            _profiler = new Profiler();

            _windowCreateInfo = new WindowCreateInfo
            {
                X = 100,
                Y = 100,
                WindowWidth = 640,
                WindowHeight = 360,
                WindowTitle = name
            };

            _window = VeldridStartup.CreateWindow(ref _windowCreateInfo);
            //_window.WindowState = WindowState.Maximized;

            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window,
            new GraphicsDeviceOptions
            {
                SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt,
                PreferDepthRangeZeroToOne = true,
                PreferStandardClipSpaceYDirection = true,
            },
            backend);

            PrintGraphicDeviceDetail();

            _graphicsCommand = new GlobalGraphicsCommand(_graphicsDevice);
            _commandBuffer = new CommandBuffer(_graphicsDevice, _graphicsCommand.ResourceGlobalData);

            _window.Resized += () =>
            {
                _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                OnWindowResize(_window.Width, _window.Height);
            };

            _renderPipelineManager = new RenderPipelineManager(_graphicsDevice);
            LoadAllBuiltInShader();
            AddBuiltInRenderPipeline();   

            _threadManager = new ThreadManager();

            Current.Window = _window;
            Current.GraphicsDevice = _graphicsDevice;
            Current.ResourceFactory = _graphicsDevice.ResourceFactory;
            Current.Engine = this;
        }

        private void InternalQuit()
        {
            _graphicsCommand.Dispose();
            _graphicsDevice.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PumpInput()
        {
            Current.InputSnapshot = _window.PumpEvents();
            Input.UpdateKeyStates();
        }

        private void AddBuiltInRenderPipeline()
        {
            RenderCoordinate renderCoordinate = new RenderCoordinate();
            _renderPipelineManager.AddRenderPipeline(renderCoordinate);
        }

        private void LoadAllBuiltInShader()
        {
            IEnumerable<string> shaderNames = EmbbedResources.GetAllFileNamesWithExtension("glsl");
            foreach (var shaderName in shaderNames)
            {
                // \ to /
                string parsedShaderName = shaderName.Replace('\\', '/');
                Log.Info($"Loading Shader {parsedShaderName}");
                if (EmbbedResources.IsShaderLib(parsedShaderName, out var filename))
                {
                    ShaderPool.SourceLibs.TryAddData(filename, EmbbedResources.GetBytes(shaderName));
                }
                else if (EmbbedResources.IsGraphicsShader(parsedShaderName, out filename))
                {
                    ShaderPool.SourceGraphics.TryAddData(filename, EmbbedResources.GetBytes(shaderName));
                }
                else if (EmbbedResources.IsComputeShader(parsedShaderName, out filename))
                {
                    ShaderPool.SourceCompute.TryAddData(filename, EmbbedResources.GetBytes(shaderName));
                }
            }

            //complie graphics shader
            foreach (var shader in ShaderPool.SourceGraphics.AllFiles)
            {
                try{
                    string filename = shader.Key;
                    string shaderText = System.Text.Encoding.UTF8.GetString(shader.Value);
                    string processedShaderText = ShaderComplier.ProcessInclude(shaderText, filename, ShaderPool.SourceLibs);

                    Shader shaderInstance = new Shader(_graphicsDevice, processedShaderText, filename);
                    ShaderPool.Add(filename, shaderInstance);
                    Log.Success($"Graphic Shader {filename} loaded");
                }
                catch (Exception e)
                {
                    Log.Error($"Graphic Shader {shader.Key} load failed\n{e}");
                }
                
            }
        }

        private void UpdatePhysicsTickRate(int rate)
        {
            _physicsFps = rate;
            physicsTickInterval = Stopwatch.Frequency / _physicsFps;
            physicsDeltaTime = 1f / _physicsFps;
        }

        private void PrintGraphicDeviceDetail()
        {
            Log.Info("Graphics Backend: \t" + _graphicsDevice.BackendType);
            Log.Info("IsDepthRangeZeroToOne: \t" + _graphicsDevice.IsDepthRangeZeroToOne);
            Log.Info("IsClipSpaceYInverted: \t" + _graphicsDevice.IsClipSpaceYInverted);
        }

        public void Stop()
        {
            _isRunning = false;
        }
    }
}