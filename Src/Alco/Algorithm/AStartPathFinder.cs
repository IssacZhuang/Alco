using System;
using System.Collections.Generic;
using System.Numerics;
using Alco;

namespace Alco;

/// <summary>
/// A reusable, allocation-conscious A* pathfinder base decoupled from any specific map.
/// Default configuration searches on an 8-connected grid (orthogonal + diagonal moves).
/// Diagonal steps have a geometric cost multiplier of √2 and corner-cutting is prevented
/// (a diagonal move is allowed only if both adjacent orthogonal cells are inside bounds
/// and traversable).
/// Implementors provide traversability, bounds, traversal cost, and heuristic estimation.
/// </summary>
public abstract class AStarPathFinder : IPathFinder
{
    private readonly PriorityQueue<int2, float> _openSet = new();
    private readonly Dictionary<int2, int2> _cameFrom = new();
    private readonly Dictionary<int2, float> _gScore = new();
    private readonly HashSet<int2> _inOpen = new();
    private readonly List<int2> _tmpPath = new();

    /// <summary>
    /// Attempts to compute a path from <paramref name="start"/> to <paramref name="end"/> on an abstract grid.
    /// Uses 4-connected A* with a user-provided cost function and heuristic.
    /// </summary>
    /// <param name="path">Output path queue (cleared before writing) containing grid cells to traverse.</param>
    /// <param name="start">World-space start position; will be cast to integral grid cell.</param>
    /// <param name="end">World-space goal position; will be cast to integral grid cell.</param>
    /// <returns>True if a path was found; otherwise false.</returns>
    public bool TryGetPath(ICollection<Vector2> path, Vector2 start, Vector2 end)
    {
        ArgumentNullException.ThrowIfNull(path);
        Reset();

        int2 startCell = math.round(start);
        int2 endCell = math.round(end);

        // Trivial case
        if (startCell.X == endCell.X && startCell.Y == endCell.Y)
        {
            path.Clear();
            return true;
        }

        if (!IsInsideBounds(endCell) || !IsTraversable(endCell))
        {
            return false;
        }

        _gScore[startCell] = 0f;
        _openSet.Enqueue(startCell, EstimateCostToGoal(startCell, endCell));
        _inOpen.Add(startCell);

        ReadOnlySpan<int2> neighbors = stackalloc int2[8]
        {
            new int2( 1, 0),
            new int2(-1, 0),
            new int2( 0, 1),
            new int2( 0,-1),
            new int2( 1, 1),
            new int2( 1,-1),
            new int2(-1, 1),
            new int2(-1,-1)
        };

        while (_openSet.Count > 0)
        {
            _openSet.TryDequeue(out int2 current, out _);
            _inOpen.Remove(current);

            if (current.X == endCell.X && current.Y == endCell.Y)
            {
                // Reconstruct using reusable buffer
                ReconstructPath(current);
                path.Clear();
                for (int i = 0; i < _tmpPath.Count; i++)
                {
                    path.Add(_tmpPath[i]);
                }
                return true;
            }

            for (int i = 0; i < neighbors.Length; i++)
            {
                int2 delta = neighbors[i];
                int2 next = new int2(current.X + delta.X, current.Y + delta.Y);

                if (!IsInsideBounds(next) || !IsTraversable(next))
                {
                    continue;
                }

                // Prevent corner-cutting on diagonal moves: both adjacent orthogonal cells
                // must be inside bounds and traversable.
                bool isDiagonal = delta.X != 0 && delta.Y != 0;
                if (isDiagonal)
                {
                    int2 sideA = new int2(current.X + delta.X, current.Y);
                    int2 sideB = new int2(current.X, current.Y + delta.Y);
                    if (!IsInsideBounds(sideA) || !IsTraversable(sideA) ||
                        !IsInsideBounds(sideB) || !IsTraversable(sideB))
                    {
                        continue;
                    }
                }

                float stepCost = GetTraversalCost(current, next);
                if (isDiagonal)
                {
                    stepCost *= 1.41421356f; // sqrt(2)
                }
                float tentativeG = _gScore[current] + stepCost;
                if (!_gScore.TryGetValue(next, out float existingG) || tentativeG < existingG)
                {
                    _cameFrom[next] = current;
                    _gScore[next] = tentativeG;
                    float fScore = tentativeG + EstimateCostToGoal(next, endCell);
                    if (!_inOpen.Contains(next))
                    {
                        _openSet.Enqueue(next, fScore);
                        _inOpen.Add(next);
                    }
                }
            }
        }

        return false;
    }

    private void Reset()
    {
        _openSet.Clear();
        _cameFrom.Clear();
        _gScore.Clear();
        _inOpen.Clear();
        _tmpPath.Clear();
    }

    private void ReconstructPath(int2 current)
    {
        _tmpPath.Clear();
        _tmpPath.Add(current);
        while (_cameFrom.TryGetValue(current, out int2 prev))
        {
            current = prev;
            _tmpPath.Add(current);
        }
        _tmpPath.Reverse();
        // remove the start cell so that the consumer moves from its current cell to the next cells
        if (_tmpPath.Count >= 2)
        {
            _tmpPath.RemoveAt(0);
        }
    }

    protected abstract bool IsTraversable(in int2 cell);
    protected abstract bool IsInsideBounds(in int2 cell);
	/// <summary>
	/// Returns the traversal cost from one adjacent cell to another.
	/// Default implementation returns 1 for uniform-cost grids.
	/// Override to inject terrain weights or dynamic penalties.
	/// </summary>
	/// <param name="from">Origin cell.</param>
	/// <param name="to">Destination cell.</param>
	/// <returns>Traversal cost; defaults to 1.</returns>
	protected virtual float GetTraversalCost(in int2 from, in int2 to)
	{
		return 1.0f;
	}
    /// <summary>
    /// Estimates the remaining cost from <paramref name="from"/> to <paramref name="to"/>.
    /// Default implementation uses the octile distance which is admissible for 8-connected grids.
    /// Override to provide domain-specific heuristics when appropriate.
    /// </summary>
    /// <param name="from">Current grid cell.</param>
    /// <param name="to">Goal grid cell.</param>
    /// <returns>Estimated remaining cost.</returns>
    protected virtual float EstimateCostToGoal(in int2 from, in int2 to)
    {
        int dx = Math.Abs(from.X - to.X);
        int dy = Math.Abs(from.Y - to.Y);
        // Octile distance: min(dx,dy)*sqrt(2) + |dx-dy|*1
        int min = dx < dy ? dx : dy;
        int max = dx > dy ? dx : dy;
        return min * 1.41421356f + (max - min);
    }
}