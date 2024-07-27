namespace SlangSharp;

public readonly partial struct SlangSession
{
    public static readonly SlangSession Null = IntPtr.Zero;
    public readonly IntPtr Handle;

    public bool IsNull => Handle == IntPtr.Zero;
    public bool IsNotNull => Handle != IntPtr.Zero;


    public SlangSession(IntPtr handle)
    {
        Handle = handle;
    }

    public static implicit operator SlangSession(IntPtr handle) => new SlangSession(handle);

    public static bool operator ==(SlangSession left, SlangSession right) => left.Handle == right.Handle;
    public static bool operator !=(SlangSession left, SlangSession right) => left.Handle != right.Handle;
    public static bool operator ==(SlangSession left, nint right) => left.Handle == right;
    public static bool operator !=(SlangSession left, nint right) => left.Handle != right;
    public bool Equals(SlangSession other) => Handle == other.Handle;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SlangSession handle && Equals(handle);
    /// <inheritdoc/>
    public override int GetHashCode() => Handle.GetHashCode();
}

public readonly partial struct SlangCompileRequest
{
    public static readonly SlangCompileRequest Null = IntPtr.Zero;
    public readonly IntPtr Handle;

    public bool IsNull => Handle == IntPtr.Zero;
    public bool IsNotNull => Handle != IntPtr.Zero;


    public SlangCompileRequest(IntPtr handle)
    {
        Handle = handle;
    }

    public static implicit operator SlangCompileRequest(IntPtr handle) => new SlangCompileRequest(handle);

    public static bool operator ==(SlangCompileRequest left, SlangCompileRequest right) => left.Handle == right.Handle;
    public static bool operator !=(SlangCompileRequest left, SlangCompileRequest right) => left.Handle != right.Handle;
    public static bool operator ==(SlangCompileRequest left, nint right) => left.Handle == right;
    public static bool operator !=(SlangCompileRequest left, nint right) => left.Handle != right;
    public bool Equals(SlangCompileRequest other) => Handle == other.Handle;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SlangCompileRequest handle && Equals(handle);
    /// <inheritdoc/>
    public override int GetHashCode() => Handle.GetHashCode();
}