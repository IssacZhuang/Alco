using System.Collections.Generic;
using System.Numerics;
using Alco;

namespace Alco;

public interface IPathFinder
{
    bool TryGetPath(ICollection<Vector2> path, Vector2 start, Vector2 end);
}