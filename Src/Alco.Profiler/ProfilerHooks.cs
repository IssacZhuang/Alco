using System.Runtime.CompilerServices;

namespace Alco.Profiler;

/// <summary>
/// Provides the non-throwing entry points injected into instrumented method bodies.
/// </summary>
public static class ProfilerHooks
{
    /// <summary>
    /// Registers method metadata emitted by an instrumented module.
    /// </summary>
    /// <param name="id">Deterministic method identifier.</param>
    /// <param name="assemblyName">Assembly containing the method.</param>
    /// <param name="declaringTypeName">Full declaring type name.</param>
    /// <param name="methodName">Source-facing method name.</param>
    /// <param name="signature">Normalized method signature.</param>
    /// <param name="tags">Special aggregation tags.</param>
    /// <param name="requiredRuntimeInterface">Optional interface required on the captured context.</param>
    public static void RegisterMethod(
        ulong id,
        string assemblyName,
        string declaringTypeName,
        string methodName,
        string signature,
        MethodProfileTag tags,
        string? requiredRuntimeInterface)
    {
        try
        {
            MethodProfilerRuntime.Instance.RegisterMethod(new MethodProfileMetadata(
                id,
                assemblyName,
                declaringTypeName,
                methodName,
                signature,
                tags,
                requiredRuntimeInterface));
        }
        catch (Exception exception)
        {
            MethodProfilerRuntime.Instance.ReportInstrumentationFault(exception.Message);
        }
    }

    /// <summary>
    /// Begins one instrumented method interval.
    /// </summary>
    /// <param name="methodId">Registered method identifier.</param>
    /// <param name="tags">Special aggregation tags.</param>
    /// <param name="context">Optional instance used for concrete-type aggregation.</param>
    /// <returns>A token consumed by <see cref="Exit"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MethodProfileToken Enter(ulong methodId, MethodProfileTag tags, object? context)
    {
        try
        {
            return MethodProfilerRuntime.Instance.Enter(methodId, tags, context);
        }
        catch (Exception exception)
        {
            MethodProfilerRuntime.Instance.ReportSessionFault(exception.Message);
            return default;
        }
    }

    /// <summary>
    /// Completes one instrumented method interval.
    /// </summary>
    /// <param name="token">Token returned by <see cref="Enter"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Exit(MethodProfileToken token)
    {
        if (!token.IsValid)
        {
            return;
        }

        try
        {
            MethodProfilerRuntime.Instance.Exit(token);
        }
        catch (Exception exception)
        {
            MethodProfilerRuntime.Instance.ReportSessionFault(exception.Message);
        }
    }
}
