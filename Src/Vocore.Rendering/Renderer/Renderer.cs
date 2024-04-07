using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public abstract class Renderer : AutoDisposable
{
    private int _threadId = Environment.CurrentManagedThreadId;

    private ICamera _camera;

    public ICamera Camera
    {
        get => _camera;
        set
        {
            _camera = value;
        }
    }

    public Renderer(ICamera camera)
    {
        _camera = camera;
    }


    public void SetOwnerThread()
    {
        _threadId = Environment.CurrentManagedThreadId;
    }

    protected void CheckThread([CallerMemberName] string? caller = "")
    {
        if (_threadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException($"Renderer method {caller} called from wrong thread. Expected thread id {_threadId} but got {Environment.CurrentManagedThreadId}");
        }
    }
}