using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;

namespace Vocore.Engine
{
    public class Profiler
    {
        private float _timer;
        private int _frameCount;
        private int _fps;
        public int FPS => _fps;
        private StringBuilder _stringBuilder = new StringBuilder();

        public Profiler()
        {
            _timer = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(float delta)
        {
            _timer += delta;
            _frameCount++;
            if (_timer >= 1)
            {
                _fps = _frameCount;
                _timer = 0;
                _frameCount = 0;
            }
        }


    }
}

