namespace SlangSharp;

public class SlangCompileRequest : IDisposable
{
    public nint Handle { get; }

    public SlangCompileRequest(SlangSession session)
    {
        Handle = Slang.spCreateCompileRequest(session.Handle);
    }

    public void SetCodeGenTarget(SlangCompileTarget target)
    {
        Slang.spSetCodeGenTarget(Handle, target);
    }

    public void SetMatrixLayoutMode(SlangMatrixLayoutMode mode)
    {
        Slang.spSetMatrixLayoutMode(Handle, mode);
    }

    public void SetTargetFlags(int targetIndex, SlangTargetFlags flags)
    {
        Slang.spSetTargetFlags(Handle, targetIndex, flags);
    }

    public void Dispose()
    {
        Slang.spDestroyCompileRequest(Handle);
    }
}