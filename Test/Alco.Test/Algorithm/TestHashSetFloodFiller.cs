using System.Linq;
using Alco;
using NUnit.Framework;

namespace Alco.Test.Algorithm;

/// <summary>Tests <see cref="HashSetFloodFiller"/> traversal, step limits and coordinate handling.</summary>
[TestFixture]
public class TestHashSetFloodFiller
{
    // The HashSet filler has no grid bounds, so traversability — including range — is expressed in the
    // predicate. In3x3 bounds a fill to the [0,3)×[0,3) region so tests mirror the GridFloodFiller ones.
    private static bool In3x3(int2 cell) => cell.X >= 0 && cell.X < 3 && cell.Y >= 0 && cell.Y < 3;

    /// <summary>Tests that a fill restricted to the start cell returns only that cell.</summary>
    [Test]
    public void SingleCellFill_Works()
    {
        var filler = new HashSetFloodFiller();
        var filled = filler.Fill(new int2(1, 1), cell => cell.X == 1 && cell.Y == 1);

        Assert.That(filled, Is.True);
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new int2(1, 1)));
    }

    /// <summary>Tests that a fill covers the whole predicate-bounded region.</summary>
    [Test]
    public void FillEntireRegion_WhenAllCellsTraversable()
    {
        var filler = new HashSetFloodFiller();
        var filled = filler.Fill(new int2(0, 0), In3x3);

        Assert.That(filled, Is.True);
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(9));
        // Check that all cells are filled
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.That(result.Contains(new int2(x, y)), Is.True);
            }
        }
    }

    /// <summary>Tests that a fill skips blocked cells.</summary>
    [Test]
    public void FillWithObstacles_Works()
    {
        var filler = new HashSetFloodFiller();
        // Block the center cell
        var filled = filler.Fill(new int2(0, 0), cell => In3x3(cell) && !(cell.X == 1 && cell.Y == 1));

        Assert.That(filled, Is.True);
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(8)); // 9 total - 1 blocked
        Assert.That(result.Any(c => c.X == 1 && c.Y == 1), Is.False);
        // Check that all other cells are filled
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                if (x == 1 && y == 1)
                    Assert.That(result.Any(c => c.X == x && c.Y == y), Is.False);
                else
                    Assert.That(result.Any(c => c.X == x && c.Y == y), Is.True);
            }
        }
    }

    /// <summary>Tests that maxStep limits the filled cells and returns false.</summary>
    [Test]
    public void FillWithMaxStep_LimitsResult()
    {
        var filler = new HashSetFloodFiller();
        // Unbounded predicate: only maxStep stops the fill.
        var filled = filler.Fill(new int2(0, 0), cell => true, maxStep: 5);

        Assert.That(filled, Is.False); // Should return false because maxStep was reached
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(5));
    }

    /// <summary>Tests that a zero maxStep returns an empty result.</summary>
    [Test]
    public void FillWithMaxStepZero_ReturnsEmpty()
    {
        var filler = new HashSetFloodFiller();
        var filled = filler.Fill(new int2(0, 0), cell => true, maxStep: 0);

        Assert.That(filled, Is.False); // Should return false because maxStep was reached immediately
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(0));
    }

    /// <summary>Tests that a non-traversable start returns an empty result.</summary>
    [Test]
    public void FillNonTraversableStart_ReturnsEmpty()
    {
        var filler = new HashSetFloodFiller();
        var filled = filler.Fill(new int2(1, 1), cell => false);

        Assert.That(filled, Is.False); // Should return false for non-traversable start
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(0));
    }

    /// <summary>Tests that a fill stops at the boundary of a traversable island.</summary>
    [Test]
    public void FillIsland_IslandOnly()
    {
        var filler = new HashSetFloodFiller();
        // A bounded island: only cells in the 3×3 bottom-right corner (the predicate supplies the
        // bounds that a grid would otherwise provide).
        var filled = filler.Fill(new int2(3, 3), cell => cell.X >= 2 && cell.X <= 4 && cell.Y >= 2 && cell.Y <= 4);

        Assert.That(filled, Is.True);
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(9)); // Cells: (2,2)..(4,4)

        // Check that all result cells satisfy the condition
        foreach (var cell in result)
        {
            Assert.That(cell.X >= 2 && cell.X <= 4 && cell.Y >= 2 && cell.Y <= 4, Is.True);
        }
    }

    /// <summary>Tests that repeated fills reset the previous result.</summary>
    [Test]
    public void MultipleFills_ResetProperly()
    {
        var filler = new HashSetFloodFiller();

        // First fill
        var filled1 = filler.Fill(new int2(0, 0), In3x3);
        Assert.That(filled1, Is.True);
        var firstResult = filler.Result.ToArray();
        Assert.That(firstResult.Length, Is.EqualTo(9));

        // Second fill with obstacles
        var filled2 = filler.Fill(new int2(0, 0), cell => In3x3(cell) && cell != new int2(1, 1));
        Assert.That(filled2, Is.True);
        var secondResult = filler.Result.ToArray();
        Assert.That(secondResult.Length, Is.EqualTo(8));

        // Third fill with maxStep
        var filled3 = filler.Fill(new int2(0, 0), cell => true, maxStep: 3);
        Assert.That(filled3, Is.False); // Should return false because maxStep was reached
        var thirdResult = filler.Result.ToArray();
        Assert.That(thirdResult.Length, Is.EqualTo(3));
    }

    /// <summary>Tests that a fill returns false when maxStep is reached.</summary>
    [Test]
    public void FillReturnsFalse_WhenMaxStepReached()
    {
        var filler = new HashSetFloodFiller();
        // Unbounded area: the fill can only stop at maxStep.
        var filled = filler.Fill(new int2(0, 0), cell => true, maxStep: 50);

        Assert.That(filled, Is.False); // Should return false because maxStep was reached before filling entire area
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(50));
    }

    /// <summary>Tests that a fill works across negative coordinates.</summary>
    [Test]
    public void FillWithNegativeCoordinates_Works()
    {
        // The HashSet filler has no internal bounds, so it works across arbitrary (incl. negative)
        // coordinates — something the grid-backed GridFloodFiller cannot do.
        var filler = new HashSetFloodFiller();
        // 3×3 region spanning negative coordinates: x,y in [-5,-3]
        var filled = filler.Fill(new int2(-4, -4), cell => cell.X >= -5 && cell.X <= -3 && cell.Y >= -5 && cell.Y <= -3);

        Assert.That(filled, Is.True);
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(9));
        for (int y = -5; y <= -3; y++)
        {
            for (int x = -5; x <= -3; x++)
            {
                Assert.That(result.Contains(new int2(x, y)), Is.True);
            }
        }
    }
}
