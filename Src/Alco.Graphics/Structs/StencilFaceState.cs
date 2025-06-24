namespace Alco.Graphics;

/// <summary>
/// Represents the stencil test configuration for a single face (front or back) of a primitive.
/// Defines how stencil testing is performed and what operations are executed based on test results.
/// </summary>
public struct StencilFaceState
{
    /// <summary>
    /// Initializes a new instance of the StencilFaceState structure.
    /// </summary>
    /// <param name="compare">The comparison function used for stencil testing.</param>
    /// <param name="passOperation">The operation to perform when both stencil and depth tests pass.</param>
    /// <param name="depthFailOperation">The operation to perform when stencil test passes but depth test fails.</param>
    /// <param name="stencilFailOperation">The operation to perform when stencil test fails.</param>
    public StencilFaceState(
        CompareFunction compare,
        StencilOperation passOperation,
        StencilOperation depthFailOperation,
        StencilOperation stencilFailOperation
        
        )
    {
        Compare = compare;
        PassOperation = passOperation;
        StencilFailOperation = stencilFailOperation;
        DepthFailOperation = depthFailOperation;
    }

    /// <summary>
    /// Gets the comparison function used for stencil testing.
    /// </summary>
    public CompareFunction Compare { get; init; }

    /// <summary>
    /// Gets the operation to perform when stencil test fails.
    /// </summary>
    public StencilOperation StencilFailOperation { get; init; }

    /// <summary>
    /// Gets the operation to perform when stencil test passes but depth test fails.
    /// </summary>
    public StencilOperation DepthFailOperation { get; init; }

    /// <summary>
    /// Gets the operation to perform when both stencil and depth tests pass.
    /// </summary>
    public StencilOperation PassOperation { get; init; }

    /// <summary>
    /// Default stencil face state that always passes and keeps the stencil value.
    /// </summary>
    public static readonly StencilFaceState Default = new(
        CompareFunction.Always,
        StencilOperation.Keep,
        StencilOperation.Keep,
        StencilOperation.Keep);

    /// <summary>
    /// Stencil face state that always passes and replaces the stencil value.
    /// </summary>
    public static readonly StencilFaceState Write = new(
        CompareFunction.Always,
        StencilOperation.Replace,
        StencilOperation.Keep,
        StencilOperation.Keep);

    /// <summary>
    /// Stencil face state that passes when stencil values are equal and keeps the stencil value.
    /// </summary>
    public static readonly StencilFaceState CompareEqual = new(
        CompareFunction.Equal,
        StencilOperation.Keep,
        StencilOperation.Keep,
        StencilOperation.Keep);

    /// <summary>
    /// Stencil face state that passes when stencil values are not equal and keeps the stencil value.
    /// </summary>
    public static readonly StencilFaceState CompareNotEqual = new(
        CompareFunction.NotEqual,
        StencilOperation.Keep,
        StencilOperation.Keep,
        StencilOperation.Keep);

    /// <summary>
    /// Determines whether two StencilFaceState instances are equal.
    /// </summary>
    /// <param name="left">The first StencilFaceState to compare.</param>
    /// <param name="right">The second StencilFaceState to compare.</param>
    /// <returns>true if the StencilFaceState instances are equal; otherwise, false.</returns>
    public static bool operator ==(StencilFaceState left, StencilFaceState right)
    {
        return left.Compare == right.Compare &&
               left.StencilFailOperation == right.StencilFailOperation &&
               left.DepthFailOperation == right.DepthFailOperation &&
               left.PassOperation == right.PassOperation;
    }

    /// <summary>
    /// Determines whether two StencilFaceState instances are not equal.
    /// </summary>
    /// <param name="left">The first StencilFaceState to compare.</param>
    /// <param name="right">The second StencilFaceState to compare.</param>
    /// <returns>true if the StencilFaceState instances are not equal; otherwise, false.</returns>
    public static bool operator !=(StencilFaceState left, StencilFaceState right)
    {
        return left.Compare != right.Compare ||
               left.StencilFailOperation != right.StencilFailOperation ||
               left.DepthFailOperation != right.DepthFailOperation ||
               left.PassOperation != right.PassOperation;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current StencilFaceState.
    /// </summary>
    /// <param name="obj">The object to compare with the current StencilFaceState.</param>
    /// <returns>true if the specified object is equal to the current StencilFaceState; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is StencilFaceState state && this == state;
    }

    /// <summary>
    /// Returns the hash code for this StencilFaceState.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 23 + Compare.GetHashCode();
        hash = hash * 23 + StencilFailOperation.GetHashCode();
        hash = hash * 23 + DepthFailOperation.GetHashCode();
        hash = hash * 23 + PassOperation.GetHashCode();
        return hash;
    }
}