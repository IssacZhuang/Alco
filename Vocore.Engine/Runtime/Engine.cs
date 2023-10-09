using System;
using System.Numerics;
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
        private int _physicsFps = 30;
        private long physicsTickInterval;
        private float physicsDeltaTime;

        protected GraphicsCommand GraphicsCommand
        {
            get
            {
                return _graphicsCommand;
            }
        }

        protected Sdl2Window Window
        {
            get
            {
                return _window;
            }
        }

        protected GraphicsDevice GraphicsDevice
        {
            get
            {
                return _graphicsDevice;
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
            _windowCreateInfo = new WindowCreateInfo
            {
                X = 100,
                Y = 100,
                WindowWidth = 640,
                WindowHeight = 360,
                WindowTitle = name
            };

            _window = VeldridStartup.CreateWindow(ref _windowCreateInfo);
            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window, GraphicsBackend.Direct3D11);
            _graphicsCommand = new GraphicsCommand(_graphicsDevice);
            _window.Resized += () =>
            {
                _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                OnWindowResize(_window.Width, _window.Height);
            };
            Global.Window = _window;
            Global.GraphicsDevice = _graphicsDevice;
            Global.ResourceFactory = _graphicsDevice.ResourceFactory;
        }

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

            _stopwatch.Start();

            while (_window.Exists)
            {
                delta = _stopwatch.ElapsedTicks - lastTime;
                lastTime = _stopwatch.ElapsedTicks;
                _timer += delta;

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
                    OnUpdate((float)delta / frequency);
                }
                catch (Exception e)
                {
                    Log.Error("Update Error: ", e);
                }
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

        private void PumpInput()
        {
            Global.InputSnapshot = _window.PumpEvents();
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