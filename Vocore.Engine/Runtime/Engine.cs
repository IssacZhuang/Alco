using System;
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
        private int _physicsFps = 30;
        private long physicsTickInterval;
        private float physicsDeltaTime;
        private WindowCreateInfo _windowCreateInfo;
        private Sdl2Window _window;
        private GraphicsDevice _graphicsDevice;

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
                WindowWidth = 1280,
                WindowHeight = 720,
                WindowTitle = name
            };

            _window = VeldridStartup.CreateWindow(ref _windowCreateInfo);
            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window);
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
                Log.Error("Startup Error: ", e.Message);
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
                        Log.Error("Tick Error: ", e.Message);
                    }
                }

                try
                {
                    OnUpdate((float)delta / frequency);
                }
                catch (Exception e)
                {
                    Log.Error("Update Error: ", e.Message);
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