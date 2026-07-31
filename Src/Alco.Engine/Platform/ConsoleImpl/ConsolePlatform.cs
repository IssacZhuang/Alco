using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// Hosts the engine main loop without a window or operating-system input source.
/// </summary>
public class ConsolePlatform : Platform
{
    private readonly NoInput _input = new();
    private EngineTimer _timer;
    private bool _isStopped;

    /// <inheritdoc/>
    public override Input Input => _input;

    /// <summary>
    /// Initializes a new console platform.
    /// </summary>
    public ConsolePlatform()
    {
        _timer = new EngineTimer();
    }

    /// <inheritdoc/>
    public override void CloseView(View window)
    {
    }

    /// <inheritdoc/>
    public override View CreateView(GPUDevice device, ViewSetting setting)
    {
        return new NoView(setting);
    }

    /// <inheritdoc/>
    public override void RunMainLoop(bool runOnce)
    {
        if (runOnce)
        {
            _timer.Start();
            _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);
            DoTick(physicsDeltaTime);
            DoUpdate(updateDeltaTime);
            return;
        }

        _timer.Start(TargetFrameRate);
        while (!_isStopped)
        {
            _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);

            if (canInvokePhysicsTick)
            {
                DoTick(physicsDeltaTime);
            }

            DoUpdate(updateDeltaTime);
            _timer.WaitForNextFrame();
        }
    }

    /// <inheritdoc/>
    public override void StopMainLoop()
    {
        _isStopped = true;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
    }
}
