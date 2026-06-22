namespace Alco.Profiler.BuildTool;

/// <summary>
/// Controls whether a coverage rule changes the current instrumentation decision.
/// </summary>
public enum MethodProfileDecision
{
    /// <summary>
    /// Preserves the previous rule decision.
    /// </summary>
    Inherit,

    /// <summary>
    /// Includes the method.
    /// </summary>
    Include,

    /// <summary>
    /// Excludes the method.
    /// </summary>
    Exclude,
}

/// <summary>
/// Controls whether instrumented IL passes the instance to the profiler.
/// </summary>
public enum MethodProfileContextCapture
{
    /// <summary>
    /// Does not capture an instance.
    /// </summary>
    None,

    /// <summary>
    /// Captures the runtime type of the reference-type instance.
    /// </summary>
    RuntimeType,
}

/// <summary>
/// Describes a target method without loading its assembly into the execution context.
/// </summary>
public sealed class MethodProfileDescriptor
{
    /// <summary>
    /// Gets the target assembly name.
    /// </summary>
    public required string AssemblyName { get; init; }

    /// <summary>
    /// Gets the declaring namespace.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Gets the full declaring type name.
    /// </summary>
    public required string DeclaringTypeName { get; init; }

    /// <summary>
    /// Gets the method metadata name.
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// Gets the normalized signature.
    /// </summary>
    public required string Signature { get; init; }

    /// <summary>
    /// Gets the full names of the declaring type and its resolvable base types.
    /// </summary>
    public required IReadOnlyList<string> TypeHierarchy { get; init; }

    /// <summary>
    /// Gets effective interface method identities served by this method in the target module.
    /// </summary>
    public required IReadOnlyList<string> EffectiveInterfaceSlots { get; init; }

    /// <summary>
    /// Gets whether the method has an IL body.
    /// </summary>
    public required bool HasBody { get; init; }

    /// <summary>
    /// Gets whether this is a static method.
    /// </summary>
    public required bool IsStatic { get; init; }

    /// <summary>
    /// Gets whether this is an instance or static constructor.
    /// </summary>
    public required bool IsConstructor { get; init; }

    /// <summary>
    /// Gets whether this is a property or event accessor.
    /// </summary>
    public required bool IsAccessor { get; init; }

    /// <summary>
    /// Gets whether compiler-generated metadata marks this method or its declaring type.
    /// </summary>
    public required bool IsCompilerGenerated { get; init; }

    /// <summary>
    /// Gets whether the build tool can safely transform this method.
    /// </summary>
    public required bool IsSupported { get; init; }

    /// <summary>
    /// Gets the reason a method is unsupported.
    /// </summary>
    public string? UnsupportedReason { get; init; }

    /// <summary>
    /// Gets whether the declaring type derives from the supplied metadata type name.
    /// </summary>
    /// <param name="fullTypeName">Full metadata type name.</param>
    /// <returns>True when the type hierarchy contains the name.</returns>
    public bool DeclaringTypeIs(string fullTypeName)
    {
        for (int i = 0; i < TypeHierarchy.Count; i++)
        {
            if (string.Equals(TypeHierarchy[i], fullTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Contains one rule's instrumentation changes.
/// </summary>
/// <param name="Decision">Coverage decision.</param>
/// <param name="Tags">Tags added while included.</param>
/// <param name="ContextCapture">Context capture behavior.</param>
/// <param name="RequiredRuntimeInterface">Optional interface required on the captured concrete type.</param>
public readonly record struct MethodProfileRuleResult(
    MethodProfileDecision Decision,
    MethodProfileTag Tags = MethodProfileTag.None,
    MethodProfileContextCapture ContextCapture = MethodProfileContextCapture.None,
    string? RequiredRuntimeInterface = null);

/// <summary>
/// Defines one composable C# coverage rule.
/// </summary>
public interface IMethodProfileRule
{
    /// <summary>
    /// Gets a stable diagnostic rule name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Evaluates one target method.
    /// </summary>
    /// <param name="method">Target method descriptor.</param>
    /// <returns>Rule changes for the method.</returns>
    MethodProfileRuleResult Evaluate(MethodProfileDescriptor method);
}

/// <summary>
/// Includes supported, non-accessor methods in first-party Alco assemblies.
/// </summary>
public sealed class EngineDefaultMethodProfileRule : IMethodProfileRule
{
    /// <inheritdoc />
    public string Name => nameof(EngineDefaultMethodProfileRule);

    /// <inheritdoc />
    public MethodProfileRuleResult Evaluate(MethodProfileDescriptor method)
    {
        if (!method.AssemblyName.StartsWith("Alco.", StringComparison.Ordinal) ||
            method.AssemblyName.StartsWith("Alco.Profiler", StringComparison.Ordinal) ||
            (method.DeclaringTypeName == "Alco.Engine.GameEngine" &&
                method.MethodName is "InternalTick" or "ExecuteTickBody") ||
            method.IsAccessor || method.IsCompilerGenerated || !method.IsSupported)
        {
            return default;
        }

        return new MethodProfileRuleResult(MethodProfileDecision.Include);
    }
}
