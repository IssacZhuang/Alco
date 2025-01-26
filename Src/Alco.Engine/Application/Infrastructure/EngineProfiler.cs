using System;
using System.Runtime.CompilerServices;

namespace Alco.Engine
{
    /// <summary>
    /// The profiler module <br/>
    /// Have only one instance in the Class Engine
    /// </summary>
    internal struct EngineProfiler
    {
        private const float UpdateInterval = 0.5f;
        private const float Mutiplier = 1.0f / UpdateInterval;
        private float _timer;
        private int _frameCount;
        private int _fps;
        private float _frameTime;
        public int FPS
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _fps;
        }

        public float FrameTime
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _frameTime;
        }

        public EngineProfiler(GameEngine engine)
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
            // if (_timer >= 1)
            // {
            //     _fps = _frameCount;
            //     _timer -= 1;
            //     _frameCount = 0;
            // }
            if (_timer >= UpdateInterval)
            {
                _fps = (int)(_frameCount * Mutiplier);
                _timer -= UpdateInterval;
                _frameCount = 0;
                _frameTime = UpdateInterval / _fps;
            }
        }
    }
}