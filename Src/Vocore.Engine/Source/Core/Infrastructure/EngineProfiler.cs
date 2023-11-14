using System;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    /// <summary>
    /// The profiler module <br/>
    /// Have only one instance in the Class Engine
    /// </summary>
    internal struct EngineProfiler
    {
        private float _timer;
        private int _frameCount;
        private int _fps;
        public int FPS
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _fps;
        }

        public EngineProfiler(GameEngine engine)
        {
            _timer = 0;
            _frameCount = 0;
            _fps = 0;
        }

        public void Initialize()
        {
            _timer = 0;
            _frameCount = 0;
            _fps = 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float delta)
        {
            _timer += delta;
            _frameCount++;
            if (_timer >= 1)
            {
                _fps = _frameCount;
                _timer -= 1;
                _frameCount = 0;
            }
        }
    }
}