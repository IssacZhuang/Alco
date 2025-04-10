using Alco.Graphics;

namespace Alco.Engine;

public class ConsolePlatform : Platform
{
    private readonly NoInputSystem _input = new NoInputSystem();
    public override InputSystem Input => _input;
    private EngineTimer _timer;
    private bool _isStopped = false;

    public ConsolePlatform()
    {
        _timer = new EngineTimer();
    }

    public override void CloseView(View window)
    {
        
    }

    public override View CreateView(GPUDevice device, ViewSetting setting)
    {
        return new NoView();
    }

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

        _timer.Start();
        while (!_isStopped)
        {
            _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);

            if (canInvokePhysicsTick)
            {
                DoTick(physicsDeltaTime);
            }

            DoUpdate(updateDeltaTime);
        }
    }

    public override void StopMainLoop()
    {
        _isStopped = true;
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}