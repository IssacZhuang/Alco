using System.Collections.Generic;
using System.Numerics;
using Alco;

namespace Alco;

/// <summary>
/// A pathfinding strategy that produces a sequence of world-space waypoints.
/// </summary>
public interface IPathFinder
{
    bool TryGetPath(ICollection<Vector2> path, Vector2 start, Vector2 end, bool ignoreEndPoint);
}