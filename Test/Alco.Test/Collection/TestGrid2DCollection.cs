using System;

namespace Alco.Test;

[TestFixture]
public class TestGrid2DCollection
{
    [Test]
    public void TestConstructor()
    {
        // Test valid constructor
        var grid = new Grid2DCollection<string>(10, 15);
        Assert.That(grid.Width, Is.EqualTo(10));
        Assert.That(grid.Height, Is.EqualTo(15));
        Assert.That(grid.Infos.Count, Is.EqualTo(0));
    }

    [Test]
    public void TestTrySet()
    {
        var grid = new Grid2DCollection<string>(3, 3);

        // Test valid set
        bool result = grid.TrySet(1, 1, "center");
        Assert.That(result, Is.True);
        Assert.That(grid.Infos.Count, Is.EqualTo(1));
        Assert.That(grid.Infos[0].X, Is.EqualTo(1));
        Assert.That(grid.Infos[0].Y, Is.EqualTo(1));
        Assert.That(grid.Infos[0].Data, Is.EqualTo("center"));

        // Test set to already occupied position
        result = grid.TrySet(1, 1, "another");
        Assert.That(result, Is.False);
        Assert.That(grid.Infos.Count, Is.EqualTo(1));

        // Test out of bounds
        result = grid.TrySet(-1, 0, "invalid");
        Assert.That(result, Is.False);

        result = grid.TrySet(0, -1, "invalid");
        Assert.That(result, Is.False);

        result = grid.TrySet(3, 0, "invalid");
        Assert.That(result, Is.False);

        result = grid.TrySet(0, 3, "invalid");
        Assert.That(result, Is.False);
    }

    [Test]
    public void TestSet()
    {
        var grid = new Grid2DCollection<string>(3, 3);

        // Test valid set
        grid.Set(1, 1, "center");
        Assert.That(grid.Infos.Count, Is.EqualTo(1));

        // Test overwrite existing value
        grid.Set(1, 1, "updated");
        Assert.That(grid.Infos.Count, Is.EqualTo(1));

        // Verify the data was updated
        bool result = grid.TryGet(1, 1, out string data);
        Assert.That(result, Is.True);
        Assert.That(data, Is.EqualTo("updated"));

        // Test out of bounds
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Set(-1, 0, "invalid"));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Set(0, -1, "invalid"));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Set(3, 0, "invalid"));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Set(0, 3, "invalid"));
    }

    [Test]
    public void TestTryGet()
    {
        var grid = new Grid2DCollection<string>(3, 3);
        grid.Set(1, 1, "center");

        // Test valid get
        bool result = grid.TryGet(1, 1, out string data);
        Assert.That(result, Is.True);
        Assert.That(data, Is.EqualTo("center"));

        // Test get from empty cell
        result = grid.TryGet(0, 0, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);

        // Test out of bounds
        result = grid.TryGet(-1, 0, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);

        result = grid.TryGet(0, -1, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);

        result = grid.TryGet(3, 0, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);

        result = grid.TryGet(0, 3, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);
    }

    [Test]
    public void TestTryRemove()
    {
        var grid = new Grid2DCollection<string>(3, 3);
        grid.Set(1, 1, "center");

        // Test valid remove
        bool result = grid.TryRemove(1, 1, out string data);
        Assert.That(result, Is.True);
        Assert.That(data, Is.EqualTo("center"));
        Assert.That(grid.Infos.Count, Is.EqualTo(0));

        // Test remove from empty cell
        result = grid.TryRemove(1, 1, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);

        // Test out of bounds
        result = grid.TryRemove(-1, 0, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);

        result = grid.TryRemove(0, -1, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);

        result = grid.TryRemove(3, 0, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);

        result = grid.TryRemove(0, 3, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);
    }

    [Test]
    public void TestClear()
    {
        var grid = new Grid2DCollection<string>(3, 3);
        grid.Set(0, 0, "topLeft");
        grid.Set(2, 2, "bottomRight");

        Assert.That(grid.Infos.Count, Is.EqualTo(2));

        // Test clear
        grid.Clear();
        Assert.That(grid.Infos.Count, Is.EqualTo(0));

        // Verify cells are empty
        bool result = grid.TryGet(0, 0, out string data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);

        result = grid.TryGet(2, 2, out data);
        Assert.That(result, Is.False);
        Assert.That(data, Is.Null);
    }
}