using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;


namespace Vocore.Engine
{
    public class Engine
    {
        private static readonly long frequency = Stopwatch.Frequency;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        
        private WindowCreateInfo _windowCreateInfo;
        private Sdl2Window _window;
        private GraphicsDevice _graphicsDevice;
        private GraphicsCommand _graphicsCommand;
        private Profiler _profiler;
        private int _physicsFps = 30;
        private long physicsTickInterval;
        private float physicsDeltaTime;
        private bool _isRunning = true;

        protected GraphicsCommand GraphicsCommand
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _graphicsCommand;
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

        public Profiler Profiler
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _profiler;
            }
        }

        public int PhysicsTickRate
        {
            get
            {
                return _physicsFps;
            }
            set
            {
                UpdatePhysicsTickRate(value);
            }
        }

        public Engine(string name = "Vocore")
        {
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
            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window,
            new GraphicsDeviceOptions{
                SwapchainDepthFormat = PixelFormat.D24_UNorm_S8_UInt
            },
             GraphicsBackend.Vulkan);
            _graphicsCommand = new GraphicsCommand(_graphicsDevice);
            _window.Resized += () =>
            {
                _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                OnWindowResize(_window.Width, _window.Height);
            };
            _window.Closing += () =>
            {
                _isRunning = false;
            };
            RuntimeGlobal.Window = _window;
            RuntimeGlobal.GraphicsDevice = _graphicsDevice;
            RuntimeGlobal.ResourceFactory = _graphicsDevice.ResourceFactory;
        }

        [STAThread]
        public void Run()
        {
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
                        if (_isRunning) OnTick(physicsDeltaTime);

                    }
                    catch (Exception e)
                    {
                        Log.Error("Tick Error: ", e);
                    }
                }

                try
                {
                    if (_isRunning) OnUpdate(deltaTime);
                }
                catch (Exception e)
                {
                    Log.Error("Update Error: ", e);
                }
                Profiler.Update(deltaTime);
            }

            OnQuit();
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

        private void InternalQuit()
        {
            _graphicsDevice.Dispose();
            _graphicsCommand.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PumpInput()
        {
            RuntimeGlobal.InputSnapshot = _window.PumpEvents();
            Input.UpdateKeyStates();
        }

        private void UpdatePhysicsTickRate(int rate)
        {
            _physicsFps = rate;
            physicsTickInterval = Stopwatch.Frequency / _physicsFps;
            physicsDeltaTime = 1f / _physicsFps;
        }
    }
}