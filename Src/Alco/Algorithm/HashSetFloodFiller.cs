using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// Breadth-first flood fill helper that tracks visited cells with a <see cref="HashSet{T}"/> instead
/// of a fixed-size bitmap. Unlike <see cref="GridFloodFiller"/>, it imposes no grid dimensions:
/// coordinates are arbitrary integers (negatives included), and bounds/validity are decided entirely
/// by the supplied <paramref name="isTraversable"/> predicate. This trades higher per-cell overhead
/// and allocations that grow with the filled area for the freedom to fill without first carving out a
/// rectangular region, so prefer <see cref="GridFloodFiller"/> when the fill is always bounded to a
/// known rectangle.
/// </summary>
public sealed class HashSetFloodFiller
{
    private readonly Deque<int2> _pending = new Deque<int2>();
    private readonly UnorderedList<int2> _result = new UnorderedList<int2>();
    private readonly HashSet<int2> _visited = new HashSet<int2>();

    /// <summary>
    /// Gets the cells visited by the last <see cref="Fill"/> operation.
    /// </summary>
    public ReadOnlySpan<int2> Result => _result.AsSpan();

    /// <summary>
    /// Initializes a new instance of the <see cref="HashSetFloodFiller"/>.
    /// </summary>
    public HashSetFloodFiller()
    {
    }

    /// <summary>
    /// Runs a flood fill starting from the given <paramref name="root"/> cell. There are no built-in
    /// bounds: every cell (including <paramref name="root"/>) is admitted or rejected solely by
    /// <paramref name="isTraversable"/>.
    /// </summary>
    /// <param name="root">Start cell.</param>
    /// <param name="isTraversable">Predicate indicating whether a cell can be traversed. It is also
    /// responsible for any bounds/range checks, since this filler has no notion of a grid.</param>
    /// <param name="maxStep">Maximum number of steps to fill. Default is 256.</param>
    /// <returns>True if the entire reachable area was filled within the step limit, false if the fill was stopped due to reaching maxStep or a non-traversable root.</returns>
    public bool Fill(int2 root, Func<int2, bool> isTraversable, int maxStep = 256)
    {
        Reset();

        if (!isTraversable(root))
        {
            return false;
        }

        _visited.Add(root);
        _pending.EnqueueTail(root);
        int steps = 0;

        while (_pending.Count > 0 && steps < maxStep)
        {
            _pending.TryDequeueHead(out int2 current);
            _result.Add(current);

            AddNeighbors(current, isTraversable);
            steps++;
        }
        return steps < maxStep;
    }

    private void AddNeighbors(int2 current, Func<int2, bool> isTraversable)
    {
        EnqueueIfValid(current + new int2(1, 0), isTraversable);
        EnqueueIfValid(current + new int2(-1, 0), isTraversable);
        EnqueueIfValid(current + new int2(0, 1), isTraversable);
        EnqueueIfValid(current + new int2(0, -1), isTraversable);
    }

    private void EnqueueIfValid(int2 position, Func<int2, bool> isTraversable)
    {
        if (_visited.Contains(position))
        {
            return;
        }
        if (!isTraversable(position))
        {
            return;
        }

        _visited.Add(position);
        _pending.EnqueueTail(position);
    }

    private void Reset()
    {
        _pending.Clear();
        _result.Clear();
        _visited.Clear();
    }
}
