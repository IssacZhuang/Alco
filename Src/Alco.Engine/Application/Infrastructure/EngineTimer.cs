using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace Alco.Engine
{
    /// <summary>
    /// The tiemr module <br/>
    /// Have only one instance in the Class Engine
    /// </summary>
    public struct EngineTimer
    {
        // Frequency substracted by Physics Tick Rate
        public static int DefaultPhysicalTickRate = 60;
        public static long Frequency = Stopwatch.Frequency;
        public static long MaxTimerTick = long.MaxValue / 2;

        private readonly Stopwatch _stopwatch;

        private long _pyhsicsTickInterval;
        private long _detlaTimerTick;
        private long _updateTickTimer;
        private long _physicsTickTimer;
        private float _pyhsicsDeltaTime;
        private float _gameSpeed;
        private int _physicsTickRate;
        private int _maxPhysicsTickAccumulation;

        public float GameSpeed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _gameSpeed;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _gameSpeed = value;
        }

        public int PhysicsTickRate
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _physicsTickRate;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _physicsTickRate = value;
                _pyhsicsTickInterval = Frequency / _physicsTickRate;
                _pyhsicsDeltaTime = 1f / _physicsTickRate;
            }
        }

        /// <summary>
        /// Maximum physics tick accumulation multiplier to prevent excessive time accumulation.
        /// Default is 5x the physics interval. For example, at 60Hz, this allows ~83ms maximum accumulation.
        /// </summary>
        public int MaxPhysicsTickAccumulation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _maxPhysicsTickAccumulation;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _maxPhysicsTickAccumulation = value;
        }

        public EngineTimer()
        {
            _stopwatch = new Stopwatch();
            _physicsTickRate = DefaultPhysicalTickRate;
            _pyhsicsTickInterval = Frequency / DefaultPhysicalTickRate;
            _pyhsicsDeltaTime = 1f / DefaultPhysicalTickRate;
            _detlaTimerTick = 0;
            _updateTickTimer = 0;
            _physicsTickTimer = 0;
            _gameSpeed = 1f;
            _maxPhysicsTickAccumulation = 3;
        }

        /// <summary>
        /// Call at a certain timer point to update the timer
        /// </summary>
        public void ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick)
        {
            _detlaTimerTick = _stopwatch.ElapsedTicks - _updateTickTimer;
            _updateTickTimer = _stopwatch.ElapsedTicks;

            updateDeltaTime = (float)_detlaTimerTick * _gameSpeed / Frequency;
            physicsDeltaTime = _pyhsicsDeltaTime * _gameSpeed;

            _physicsTickTimer += _detlaTimerTick;

            // Limit maximum accumulated time to prevent excessive accumulation
            long maxAccumulatedTime = _pyhsicsTickInterval * _maxPhysicsTickAccumulation;
            _physicsTickTimer = Math.Min(_physicsTickTimer, maxAccumulatedTime);

            if (_physicsTickTimer >= _pyhsicsTickInterval)
            {
                _physicsTickTimer -= _pyhsicsTickInterval;
                canInvokePhysicsTick = true;
            }
            else
            {
                canInvokePhysicsTick = false;
            }
            PreventTimerOverflow();
        }

        public void Start()
        {
            _stopwatch.Reset();
            _stopwatch.Start();
        }

        private void PreventTimerOverflow()
        {
            if (_stopwatch.ElapsedTicks > MaxTimerTick)
            {
                _stopwatch.Restart();
            }
        }
    }
}