namespace SlangSharp;

public class SlangSession : IDisposable
{
    public nint Handle { get; }

    public SlangSession(string name = "slang_session")
    {
        Handle = Slang.spCreateSession(name);
    }

    public SlangCompileRequest CreateCompileRequest()
    {
        return new SlangCompileRequest(this);
    }

    public void Dispose()
    {
        Slang.spDestroySession(Handle);
    }
}