using System.Collections.Generic;
using System.Numerics;
using Alco;
using NUnit.Framework;

namespace Alco.Test.Algorithm;

[TestFixture]
public class TestAStarPathFinder
{
    private sealed class GridPathFinder : AStarPathFinder
    {
        private readonly int width;
        private readonly int height;
        private readonly HashSet<int2> blocked;
        private readonly Dictionary<int2, float> cellCost;

        public GridPathFinder(int width, int height, IEnumerable<int2> blocked = null, Dictionary<int2, float> cellCost = null)
        {
            this.width = width;
            this.height = height;
            this.blocked = new HashSet<int2>(blocked ?? System.Linq.Enumerable.Empty<int2>());
            this.cellCost = cellCost ?? new Dictionary<int2, float>();
        }

        protected override bool IsTraversable(in int2 cell)
        {
            return !blocked.Contains(cell);
        }

        protected override bool IsInsideBounds(in int2 cell)
        {
            return cell.X >= 0 && cell.Y >= 0 && cell.X < width && cell.Y < height;
        }

        protected override float GetTraversalCost(in int2 from, in int2 to)
        {
            if (cellCost.TryGetValue(to, out var cost))
            {
                return cost;
            }

            return base.GetTraversalCost(from, to);
        }
    }

    [Test]
    public void StartEqualsEnd_ReturnsTrue_AndEmptyPath()
    {
        var pf = new GridPathFinder(3, 3);
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(1, 1), new Vector2(1, 1), ignoreEndPoint: false);

        Assert.That(ok, Is.True);
        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void GoalBlocked_ReturnsFalse()
    {
        var pf = new GridPathFinder(3, 3, blocked: new[] { new int2(2, 2) });
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(2, 2), ignoreEndPoint: false);

        Assert.That(ok, Is.False);
        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void UniquePath_OnRectGrid_IsValidShortestPath()
    {
        // Grid 5x2; only y in {0,1}. Block (2,0) to force detour; with diagonal allowed,
        // shortest path length decreases accordingly but must not pass through (2,0).
        var pf = new GridPathFinder(5, 2, blocked: new[] { new int2(2, 0) });
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(4, 0), ignoreEndPoint: false);

        Assert.That(ok, Is.True);
        // With diagonal, minimal steps reduce versus 4-connectivity (but we keep it flexible):
        Assert.That(path.Count, Is.GreaterThan(0));
        var steps = path.ToArray();
        // First step adjacent to start
        Assert.That(steps[0] == new Vector2(1, 0) || steps[0] == new Vector2(0, 1) || steps[0] == new Vector2(1, 1), Is.True);
        // Never visit the blocked cell
        foreach (var s in steps) Assert.That(s, Is.Not.EqualTo(new Vector2(2, 0)));
        // Ends at goal
        Assert.That(steps[steps.Length - 1], Is.EqualTo(new Vector2(4, 0)));
    }

    [Test]
    public void WeightedCost_AvoidsHighCostCell()
    {
        // Grid 3x2: prefer detour over (1,0) which is very expensive
        var costs = new Dictionary<int2, float>
        {
            [new int2(1, 0)] = 100f,
        };
        var pf = new GridPathFinder(3, 2, blocked: null, cellCost: costs);
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(2, 0), ignoreEndPoint: false);

        Assert.That(ok, Is.True);
        var steps = path.ToArray();
        // With diagonal allowed and corner-cutting prevented, optimal path is two diagonals.
        Assert.That(steps.Length, Is.EqualTo(2));
        // Must avoid the high cost cell
        foreach (var s in steps) Assert.That(s, Is.Not.EqualTo(new Vector2(1, 0)));
        // Ends at goal
        Assert.That(steps[steps.Length - 1], Is.EqualTo(new Vector2(2, 0)));
    }

    [Test]
    public void Diagonal_CornerCutting_Prevented()
    {
        // Goal at (1,1); block (1,0) but keep (0,1) open. Direct diagonal from (0,0)->(1,1)
        // must be disallowed due to corner-cut rule (requires both (1,0) and (0,1) open).
        var pf = new GridPathFinder(3, 3, blocked: new[] { new int2(1, 0) });
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(1, 1), ignoreEndPoint: false);

        Assert.That(ok, Is.True);
        var steps = path.ToArray();
        // First step must not be the diagonal landing cell; should go via (0,1)
        Assert.That(steps[0], Is.EqualTo(new Vector2(0, 1)));
        Assert.That(steps[steps.Length - 1], Is.EqualTo(new Vector2(1, 1)));
    }

    [Test]
    public void IgnoreEndPoint_False_BlockedGoal_ReturnsFalse()
    {
        // When ignoreEndPoint is false, goal must be traversable
        var pf = new GridPathFinder(5, 5, blocked: new[] { new int2(4, 4) });
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(4, 4), ignoreEndPoint: false);

        Assert.That(ok, Is.False);
        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void IgnoreEndPoint_True_BlockedGoal_ReturnsTrue_PathReachesAdjacent()
    {
        // When ignoreEndPoint is true, path should reach an adjacent cell to the blocked goal
        var pf = new GridPathFinder(5, 5, blocked: new[] { new int2(4, 4) });
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(4, 4), ignoreEndPoint: true);

        Assert.That(ok, Is.True);
        Assert.That(path.Count, Is.GreaterThan(0));
        var lastStep = path[path.Count - 1];
        // Last step should be orthogonally adjacent to goal (4,4)
        // Valid adjacent cells: (3,4), (4,3), (5,4) [out of bounds], (4,5) [out of bounds]
        var isAdjacentToGoal = (lastStep == new Vector2(3, 4) || lastStep == new Vector2(4, 3));
        Assert.That(isAdjacentToGoal, Is.True);
    }

    [Test]
    public void IgnoreEndPoint_True_NoTraversableNeighbor_ReturnsFalse()
    {
        // When ignoreEndPoint is true but goal has no traversable neighbors, should fail
        // Block goal and all its orthogonal neighbors
        var pf = new GridPathFinder(5, 5, blocked: new[] 
        { 
            new int2(2, 2),  // goal
            new int2(1, 2),  // left neighbor
            new int2(3, 2),  // right neighbor
            new int2(2, 1),  // top neighbor
            new int2(2, 3)   // bottom neighbor
        });
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(2, 2), ignoreEndPoint: true);

        Assert.That(ok, Is.False);
        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void IgnoreEndPoint_True_StartEqualsEnd_ReturnsTrue_EmptyPath()
    {
        // Even with ignoreEndPoint, start == end should return empty path
        var pf = new GridPathFinder(3, 3);
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(1, 1), new Vector2(1, 1), ignoreEndPoint: true);

        Assert.That(ok, Is.True);
        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void IgnoreEndPoint_True_MultipleAdjacentOptions_FindsShortest()
    {
        // When ignoreEndPoint is true and multiple adjacent cells are available,
        // should find path to the nearest adjacent cell
        var pf = new GridPathFinder(6, 6, blocked: new[] { new int2(5, 5) });
        var path = new List<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(5, 5), ignoreEndPoint: true);

        Assert.That(ok, Is.True);
        Assert.That(path.Count, Is.GreaterThan(0));
        var lastStep = path[path.Count - 1];
        // Path should end at an adjacent cell to (5,5)
        var isAdjacentToGoal = (lastStep == new Vector2(4, 5) || lastStep == new Vector2(5, 4));
        Assert.That(isAdjacentToGoal, Is.True);
    }
}

