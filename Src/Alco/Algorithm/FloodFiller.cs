using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// Breadth-first flood fill helper for grid-based traversal.
/// </summary>
public sealed class FloodFiller
{
    private readonly Deque<int2> _pending = new Deque<int2>();
    private readonly UnorderedList<int2> _result = new UnorderedList<int2>();
    private byte[] _visited;
    private int _width;
    private int _height;

    /// <summary>
    /// Gets the cells visited by the last <see cref="Fill"/> operation.
    /// </summary>
    public ReadOnlySpan<int2> Result => _result.AsSpan();

    /// <summary>
    /// Initializes a new instance of the <see cref="FloodFiller"/> with the specified grid size.
    /// </summary>
    /// <param name="width">Grid width in cells.</param>
    /// <param name="height">Grid height in cells.</param>
    public FloodFiller(int width, int height)
    {
        _width = width;
        _height = height;
        _visited = new byte[width * height];
    }

    /// <summary>
    /// Resizes the internal buffers to match the provided grid size.
    /// </summary>
    /// <param name="width">New grid width.</param>
    /// <param name="height">New grid height.</param>
    public void Resize(int width, int height)
    {
        int length = width * height;
        if (length != _visited.Length)
        {
            _visited = new byte[length];
        }
        _width = width;
        _height = height;
    }

    /// <summary>
    /// Runs a flood fill starting from the given <paramref name="root"/> cell.
    /// </summary>
    /// <param name="root">Start cell.</param>
    /// <param name="isTraversable">Predicate indicating whether a cell can be traversed.</param>
    /// <param name="maxStep">Maximum number of steps to fill. Default is 256.</param>
    /// <returns>True if the entire reachable area was filled within the step limit, false if the fill was stopped due to reaching maxStep.</returns>
    public bool Fill(int2 root, Func<int2, bool> isTraversable, int maxStep = 256)
    {
        Reset();

        if (!IsInBounds(root) || !isTraversable(root))
        {
            return false;
        }

        MarkVisited(root);
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

    private bool IsInBounds(int2 position){
        return (uint)position.X < (uint)_width && (uint)position.Y < (uint)_height;
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
        if (!IsInBounds(position))
        {
            return;
        }
        if (IsVisited(position))
        {
            return;
        }
        if (!isTraversable(position))
        {
            return;
        }

        MarkVisited(position);
        _pending.EnqueueTail(position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsVisited(int2 position)
    {
        int index = ToIndex(position);
        return _visited[index] != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkVisited(int2 position)
    {
        int index = ToIndex(position);
        _visited[index] = 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ToIndex(int2 position)
    {
        return position.Y * _width + position.X;
    }

    private void Reset()
    {
        _pending.Clear();
        _result.Clear();
        _visited.AsSpan().Clear();
    }
}