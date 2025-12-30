using System.Linq;
using Alco;
using NUnit.Framework;

namespace Alco.Test.Algorithm;

[TestFixture]
public class TestFloodFiller
{
    [Test]
    public void SingleCellFill_Works()
    {
        var filler = new FloodFiller(3, 3);
        var filled = filler.Fill(new int2(1, 1), cell => cell.X == 1 && cell.Y == 1);

        Assert.That(filled, Is.True);
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new int2(1, 1)));
    }

    [Test]
    public void FillEntireGrid_WhenAllCellsTraversable()
    {
        var filler = new FloodFiller(3, 3);
        var filled = filler.Fill(new int2(0, 0), cell => true);

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

    [Test]
    public void FillWithObstacles_Works()
    {
        var filler = new FloodFiller(3, 3);
        // Block the center cell
        var filled = filler.Fill(new int2(0, 0), cell => !(cell.X == 1 && cell.Y == 1));

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

    [Test]
    public void FillWithMaxStep_LimitsResult()
    {
        var filler = new FloodFiller(3, 3);
        var filled = filler.Fill(new int2(0, 0), cell => true, maxStep: 5);

        Assert.That(filled, Is.False); // Should return false because maxStep was reached
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(5));
    }

    [Test]
    public void FillWithMaxStepZero_ReturnsEmpty()
    {
        var filler = new FloodFiller(3, 3);
        var filled = filler.Fill(new int2(0, 0), cell => true, maxStep: 0);

        Assert.That(filled, Is.False); // Should return false because maxStep was reached immediately
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public void FillOutOfBounds_ReturnsEmpty()
    {
        var filler = new FloodFiller(3, 3);
        var filled = filler.Fill(new int2(-1, 0), cell => true);

        Assert.That(filled, Is.False); // Should return false for out of bounds start
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public void FillNonTraversableStart_ReturnsEmpty()
    {
        var filler = new FloodFiller(3, 3);
        var filled = filler.Fill(new int2(1, 1), cell => false);

        Assert.That(filled, Is.False); // Should return false for non-traversable start
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public void FillIsland_IslandOnly()
    {
        var filler = new FloodFiller(5, 5);
        // Create an island: only cells in the bottom-right corner
        var filled = filler.Fill(new int2(3, 3), cell => cell.X >= 2 && cell.Y >= 2);

        Assert.That(filled, Is.True);
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(9)); // Cells: (2,2),(3,2),(4,2),(2,3),(3,3),(4,3),(2,4),(3,4),(4,4)

        // Check that all result cells satisfy the condition
        foreach (var cell in result)
        {
            Assert.That(cell.X >= 2 && cell.Y >= 2, Is.True);
        }
    }

    [Test]
    public void Resize_Works()
    {
        var filler = new FloodFiller(2, 2);
        filler.Resize(3, 3);

        var filled = filler.Fill(new int2(0, 0), cell => true);
        Assert.That(filled, Is.True);
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(9));
    }

    [Test]
    public void MultipleFills_ResetProperly()
    {
        var filler = new FloodFiller(3, 3);

        // First fill
        var filled1 = filler.Fill(new int2(0, 0), cell => true);
        Assert.That(filled1, Is.True);
        var firstResult = filler.Result.ToArray();
        Assert.That(firstResult.Length, Is.EqualTo(9));

        // Second fill with obstacles
        var filled2 = filler.Fill(new int2(0, 0), cell => cell != new int2(1, 1));
        Assert.That(filled2, Is.True);
        var secondResult = filler.Result.ToArray();
        Assert.That(secondResult.Length, Is.EqualTo(8));

        // Third fill with maxStep
        var filled3 = filler.Fill(new int2(0, 0), cell => true, maxStep: 3);
        Assert.That(filled3, Is.False); // Should return false because maxStep was reached
        var thirdResult = filler.Result.ToArray();
        Assert.That(thirdResult.Length, Is.EqualTo(3));
    }

    [Test]
    public void FillReturnsFalse_WhenMaxStepReached()
    {
        var filler = new FloodFiller(10, 10);
        // Create a large area that would need more than 50 steps to fill completely
        var filled = filler.Fill(new int2(0, 0), cell => true, maxStep: 50);

        Assert.That(filled, Is.False); // Should return false because maxStep was reached before filling entire area
        var result = filler.Result.ToArray();
        Assert.That(result.Length, Is.EqualTo(50));
    }

}
