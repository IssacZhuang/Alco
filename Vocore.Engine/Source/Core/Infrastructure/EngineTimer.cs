using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace Vocore.Engine
{
    internal struct EngineTimer
    {
        public static int DefaultPhysicalTickRate = 30;
        public static long Frequency = Stopwatch.Frequency;

        private Stopwatch _stopwatch;
        // Frequency substracted by Physics Tick Rate
        private long _pyhsicalTickInterval;
        private float _pyhsicalDeltaTime;
        private int _physicalTickRate;

        public int PhysicsTickRate
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _physicalTickRate;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _physicalTickRate = value;
                _pyhsicalTickInterval = Frequency / _physicalTickRate;
                _pyhsicalDeltaTime = 1f / _physicalTickRate;
            }
        }

        public EngineTimer(GameEngine engine)
        {
            _stopwatch = new Stopwatch();
            _physicalTickRate = DefaultPhysicalTickRate;
            _pyhsicalTickInterval = Frequency / DefaultPhysicalTickRate; // default 30 ticks per second
            _pyhsicalDeltaTime = 1f / DefaultPhysicalTickRate;
        }

        public void Start()
        {
            _stopwatch.Reset();
            _stopwatch.Start();
        }

    }
}