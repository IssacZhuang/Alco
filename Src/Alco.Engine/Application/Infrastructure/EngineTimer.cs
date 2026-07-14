using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Alco.Engine;

/// <summary>
/// Provides monotonic frame timing, fixed-tick accumulation, and optional frame pacing.
/// </summary>
public struct EngineTimer
{
    /// <summary>
    /// Default fixed-tick frequency used by the engine.
    /// </summary>
    public const int DefaultPhysicalTickRate = 60;

    /// <summary>
    /// Frequency of the monotonic timestamp source.
    /// </summary>
    public static readonly long Frequency = Stopwatch.Frequency;

    /// <summary>
    /// Timestamp threshold used to reset the timer before its counters can overflow.
    /// </summary>
    public static readonly long MaxTimerTick = long.MaxValue / 2;

    private static readonly long SleepReserveTicks = Math.Max(1, Frequency / 1000);

    private readonly Stopwatch _stopwatch;

    private long _physicsTickInterval;
    private long _deltaTimerTick;
    private long _updateTickTimer;
    private long _physicsTickTimer;
    private long _frameIntervalTick;
    private long _nextFrameDeadlineTick;
    private float _physicsDeltaTime;
    private float _gameSpeed;
    private int _physicsTickRate;
    private int _maxPhysicsTickAccumulation;

    /// <summary>
    /// Gets or sets the multiplier applied to update and fixed-tick delta times.
    /// </summary>
    public float GameSpeed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => _gameSpeed;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _gameSpeed = value;
    }

    /// <summary>
    /// Gets or sets the fixed-tick frequency in hertz.
    /// </summary>
    public int PhysicsTickRate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => _physicsTickRate;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _physicsTickRate = value;
            _physicsTickInterval = Frequency / _physicsTickRate;
            _physicsDeltaTime = 1f / _physicsTickRate;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of fixed-tick intervals retained after a stall.
    /// </summary>
    public int MaxPhysicsTickAccumulation
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => _maxPhysicsTickAccumulation;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _maxPhysicsTickAccumulation = value;
    }

    /// <summary>
    /// Initializes a new engine timer with a 60 hertz fixed tick.
    /// </summary>
    public EngineTimer()
    {
        _stopwatch = new Stopwatch();
        _physicsTickRate = DefaultPhysicalTickRate;
        _physicsTickInterval = Frequency / DefaultPhysicalTickRate;
        _physicsDeltaTime = 1f / DefaultPhysicalTickRate;
        _deltaTimerTick = 0;
        _updateTickTimer = 0;
        _physicsTickTimer = 0;
        _frameIntervalTick = 0;
        _nextFrameDeadlineTick = 0;
        _gameSpeed = 1f;
        _maxPhysicsTickAccumulation = 3;
    }

    /// <summary>
    /// Updates elapsed time and fixed-tick availability.
    /// </summary>
    /// <param name="updateDeltaTime">Wall-clock time since the previous update, scaled by game speed.</param>
    /// <param name="physicsDeltaTime">Fixed-tick duration scaled by game speed.</param>
    /// <param name="canInvokePhysicsTick">Whether one fixed tick is currently available.</param>
    public void ProcessTime(
        out float updateDeltaTime,
        out float physicsDeltaTime,
        out bool canInvokePhysicsTick)
    {
        long elapsedTicks = _stopwatch.ElapsedTicks;
        _deltaTimerTick = elapsedTicks - _updateTickTimer;
        _updateTickTimer = elapsedTicks;

        updateDeltaTime = (float)_deltaTimerTick * _gameSpeed / Frequency;
        physicsDeltaTime = _physicsDeltaTime * _gameSpeed;

        _physicsTickTimer += _deltaTimerTick;
        long maxAccumulatedTime = _physicsTickInterval * _maxPhysicsTickAccumulation;
        _physicsTickTimer = Math.Min(_physicsTickTimer, maxAccumulatedTime);

        if (_physicsTickTimer >= _physicsTickInterval)
        {
            _physicsTickTimer -= _physicsTickInterval;
            canInvokePhysicsTick = true;
        }
        else
        {
            canInvokePhysicsTick = false;
        }

        PreventTimerOverflow(elapsedTicks);
    }

    /// <summary>
    /// Restarts timing and configures optional frame pacing.
    /// </summary>
    /// <param name="targetFrameRate">Target frame rate, or a value less than or equal to zero for unlimited frames.</param>
    public void Start(int targetFrameRate = 0)
    {
        _stopwatch.Restart();
        _deltaTimerTick = 0;
        _updateTickTimer = 0;
        _physicsTickTimer = 0;
        _frameIntervalTick = targetFrameRate > 0
            ? Math.Max(1, Frequency / targetFrameRate)
            : 0;
        _nextFrameDeadlineTick = _frameIntervalTick;
    }

    /// <summary>
    /// Waits for the next configured frame boundary without allocating.
    /// </summary>
    internal void WaitForNextFrame()
    {
        if (_frameIntervalTick <= 0)
        {
            return;
        }

        long deadline = _nextFrameDeadlineTick;
        long beforeWait = _stopwatch.ElapsedTicks;
        bool missedDeadline = beforeWait >= deadline;

        long now = beforeWait;
        while (now < deadline)
        {
            long remainingTicks = deadline - now;
            if (remainingTicks > SleepReserveTicks)
            {
                long sleepTicks = remainingTicks - SleepReserveTicks;
                int sleepMilliseconds = (int)(sleepTicks * 1000 / Frequency);
                if (sleepMilliseconds > 0)
                {
                    Thread.Sleep(sleepMilliseconds);
                }
                else
                {
                    Thread.Yield();
                }
            }
            else
            {
                Thread.SpinWait(64);
            }

            now = _stopwatch.ElapsedTicks;
        }

        long lateness = now - deadline;
        _nextFrameDeadlineTick = missedDeadline || lateness > SleepReserveTicks
            ? now + _frameIntervalTick
            : deadline + _frameIntervalTick;
    }

    private void PreventTimerOverflow(long elapsedTicks)
    {
        if (elapsedTicks <= MaxTimerTick)
        {
            return;
        }

        _stopwatch.Restart();
        _deltaTimerTick = 0;
        _updateTickTimer = 0;
        _physicsTickTimer = 0;
        _nextFrameDeadlineTick = _frameIntervalTick;
    }
}
