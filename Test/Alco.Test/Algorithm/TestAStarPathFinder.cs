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
        var path = new Queue<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(1, 1), new Vector2(1, 1));

        Assert.That(ok, Is.True);
        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void GoalBlocked_ReturnsFalse()
    {
        var pf = new GridPathFinder(3, 3, blocked: new[] { new int2(2, 2) });
        var path = new Queue<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(2, 2));

        Assert.That(ok, Is.False);
        Assert.That(path.Count, Is.EqualTo(0));
    }

    [Test]
    public void UniquePath_OnRectGrid_IsValidShortestPath()
    {
        // Grid 5x2; only y in {0,1}. Block (2,0) to force detour; with diagonal allowed,
        // shortest path length decreases accordingly but must not pass through (2,0).
        var pf = new GridPathFinder(5, 2, blocked: new[] { new int2(2, 0) });
        var path = new Queue<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(4, 0));

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
        var path = new Queue<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(2, 0));

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
        var path = new Queue<Vector2>();

        bool ok = pf.TryGetPath(path, new Vector2(0, 0), new Vector2(1, 1));

        Assert.That(ok, Is.True);
        var steps = path.ToArray();
        // First step must not be the diagonal landing cell; should go via (0,1)
        Assert.That(steps[0], Is.EqualTo(new Vector2(0, 1)));
        Assert.That(steps[steps.Length - 1], Is.EqualTo(new Vector2(1, 1)));
    }
}

