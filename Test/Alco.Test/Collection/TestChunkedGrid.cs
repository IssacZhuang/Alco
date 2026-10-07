using System;
using System.Collections.Generic;

namespace Alco.Test;

[TestFixture]
public class TestChunkedGrid
{
    [Test]
    public void TestConstructorValidatesChunkSize()
    {
        Assert.That(new ChunkedGrid<int>().ChunkSize, Is.EqualTo(64));
        Assert.That(new ChunkedGrid<int>(chunkSize: 32).ChunkSize, Is.EqualTo(32));
        Assert.That(new ChunkedGrid<int>(chunkSize: 32).ChunkCells, Is.EqualTo(1024));
        Assert.That(new ChunkedGrid<int>(chunkSize: 1).ChunkCells, Is.EqualTo(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkedGrid<int>(chunkSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkedGrid<int>(chunkSize: -64));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkedGrid<int>(chunkSize: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkedGrid<int>(chunkSize: 100));
    }

    [Test]
    public void TestNegativeCoordinatesUseFloorDivision()
    {
        // Floor-division cases from the streaming architecture research (section 10.3):
        // cell -1 -> chunk -1, local 63; cell -64 -> chunk -1, local 0; cell -65 -> chunk -2, local 63.
        var grid = new ChunkedGrid<int>(chunkSize: 64);
        Assert.That(grid.GetChunkCoord(-1, -1), Is.EqualTo(new int2(-1, -1)));
        Assert.That(grid.GetLocalCell(-1), Is.EqualTo(63));

        Assert.That(grid.GetChunkCoord(-64, -64), Is.EqualTo(new int2(-1, -1)));
        Assert.That(grid.GetLocalCell(-64), Is.EqualTo(0));

        Assert.That(grid.GetChunkCoord(-65, -65), Is.EqualTo(new int2(-2, -2)));
        Assert.That(grid.GetLocalCell(-65), Is.EqualTo(63));

        Assert.That(grid.GetChunkCoord(-63, 5), Is.EqualTo(new int2(-1, 0)));
        Assert.That(grid.GetChunkCoord(5, -63), Is.EqualTo(new int2(0, -1)));
    }

    [TestCase(64)]
    [TestCase(16)]
    [TestCase(2)]
    public void TestLocalCellAlwaysNonNegative(int chunkSize)
    {
        var grid = new ChunkedGrid<int>(chunkSize: chunkSize);
        int[] cells = { int.MinValue, -33554433, -65, -chunkSize, -1, 0, 1, chunkSize - 1, chunkSize, int.MaxValue };
        foreach (int cell in cells)
        {
            Assert.That(grid.GetLocalCell(cell), Is.InRange(0, chunkSize - 1), $"cell {cell}");
        }
    }

    [TestCase(64)]
    [TestCase(16)]
    public void TestGetLocalIndexRowMajor(int chunkSize)
    {
        var grid = new ChunkedGrid<int>(chunkSize: chunkSize);
        int last = chunkSize - 1;

        // Row-major layout within the chunk: localY*chunkSize + localX.
        Assert.That(grid.GetLocalIndex(0, 0), Is.EqualTo(0));
        Assert.That(grid.GetLocalIndex(1, 0), Is.EqualTo(1));
        Assert.That(grid.GetLocalIndex(last, 0), Is.EqualTo(last));
        Assert.That(grid.GetLocalIndex(0, 1), Is.EqualTo(chunkSize));
        Assert.That(grid.GetLocalIndex(last, last), Is.EqualTo(grid.ChunkCells - 1));
        // Cells in neighboring chunks wrap back to local coordinates.
        Assert.That(grid.GetLocalIndex(chunkSize, 1), Is.EqualTo(chunkSize));
        // Cell (-1,-1) is the max-corner cell of chunk (-1,-1): local (last,last).
        Assert.That(grid.GetLocalIndex(-1, -1), Is.EqualTo(last * chunkSize + last));
        Assert.That(grid.GetLocalIndex(-chunkSize, 0), Is.EqualTo(0));
    }

    [TestCase(64)]
    [TestCase(16)]
    [TestCase(2)]
    public void TestChunkOriginRoundTrip(int chunkSize)
    {
        var grid = new ChunkedGrid<int>(chunkSize: chunkSize);
        int[] cells = { int.MinValue, -65, -chunkSize, -1, 0, 1, chunkSize - 1, chunkSize, int.MaxValue };
        foreach (int cellX in cells)
        {
            foreach (int cellY in cells)
            {
                int2 chunkCoord = grid.GetChunkCoord(cellX, cellY);
                int2 origin = grid.GetChunkOrigin(chunkCoord);
                Assert.That(origin.X + grid.GetLocalCell(cellX), Is.EqualTo(cellX), $"x {cellX}");
                Assert.That(origin.Y + grid.GetLocalCell(cellY), Is.EqualTo(cellY), $"y {cellY}");
            }
        }
    }

    [TestCase(64)]
    [TestCase(16)]
    public void TestReadMissingChunkReturnsDefault(int chunkSize)
    {
        var grid = new ChunkedGrid<int>(chunkSize: chunkSize);
        Assert.That(grid[0, 0], Is.EqualTo(0));
        Assert.That(grid[-1, -1], Is.EqualTo(0));
        Assert.That(grid[1000, -2000], Is.EqualTo(0));
        Assert.That(grid.ChunkCount, Is.EqualTo(0));

        var floatGrid = new ChunkedGrid<float>(15f, chunkSize);
        Assert.That(floatGrid[10, 10], Is.EqualTo(15f));
        Assert.That(floatGrid[-10, -10], Is.EqualTo(15f));
        Assert.That(floatGrid.ChunkCount, Is.EqualTo(0));
    }

    [TestCase(64)]
    [TestCase(16)]
    public void TestSetAllocatesChunkAndRoundTrips(int chunkSize)
    {
        var grid = new ChunkedGrid<byte>(chunkSize: chunkSize);
        grid[3, 5] = 42;
        Assert.That(grid.ChunkCount, Is.EqualTo(1));
        Assert.That(grid[3, 5], Is.EqualTo(42));

        // Negative coordinates land in negative chunks but behave identically.
        grid[-3, -5] = 7;
        Assert.That(grid.ChunkCount, Is.EqualTo(2));
        Assert.That(grid[-3, -5], Is.EqualTo(7));

        // Other cells in the same chunk are untouched.
        Assert.That(grid[4, 5], Is.EqualTo(0));
    }

    [Test]
    public void TestSetDefaultValueDoesNotAllocate()
    {
        var grid = new ChunkedGrid<int>(9);
        grid[10, 10] = 9;
        Assert.That(grid.ChunkCount, Is.EqualTo(0));
        Assert.That(grid[10, 10], Is.EqualTo(9));

        grid[20, 20] = 5;
        Assert.That(grid.ChunkCount, Is.EqualTo(1));
        // Writing the default into an allocated chunk is a plain write, chunk stays.
        grid[20, 20] = 9;
        Assert.That(grid.ChunkCount, Is.EqualTo(1));
        Assert.That(grid[20, 20], Is.EqualTo(9));
    }

    [Test]
    public void TestSetOverwritesExistingValue()
    {
        var grid = new ChunkedGrid<string>();
        grid[1, 1] = "first";
        grid[1, 1] = "second";
        Assert.That(grid.ChunkCount, Is.EqualTo(1));
        Assert.That(grid[1, 1], Is.EqualTo("second"));

        grid.Set(1, 1, "third");
        Assert.That(grid[1, 1], Is.EqualTo("third"));
    }

    [TestCase(64)]
    [TestCase(16)]
    [TestCase(4)]
    public void TestCellsAcrossChunkBoundaries(int chunkSize)
    {
        var grid = new ChunkedGrid<int>(chunkSize: chunkSize);
        // Neighboring cells on both sides of the 0 and -1 chunk boundaries.
        grid[chunkSize - 1, 0] = 1;
        grid[chunkSize, 0] = 2;
        grid[-1, 0] = 3;
        grid[0, -1] = 4;
        grid[0, chunkSize] = 5;

        Assert.That(grid.ChunkCount, Is.EqualTo(5));
        Assert.That(grid[chunkSize - 1, 0], Is.EqualTo(1));
        Assert.That(grid[chunkSize, 0], Is.EqualTo(2));
        Assert.That(grid[-1, 0], Is.EqualTo(3));
        Assert.That(grid[0, -1], Is.EqualTo(4));
        Assert.That(grid[0, chunkSize], Is.EqualTo(5));
    }

    [TestCase(64)]
    [TestCase(16)]
    public void TestExtremeCoordinates(int chunkSize)
    {
        var grid = new ChunkedGrid<int>(chunkSize: chunkSize);
        grid[int.MaxValue, int.MinValue] = 1;
        grid[int.MinValue, int.MaxValue] = 2;
        Assert.That(grid.ChunkCount, Is.EqualTo(2));
        Assert.That(grid[int.MaxValue, int.MinValue], Is.EqualTo(1));
        Assert.That(grid[int.MinValue, int.MaxValue], Is.EqualTo(2));
    }

    [TestCase(64)]
    [TestCase(16)]
    public void TestGetOrCreateChunkPrefillsDefault(int chunkSize)
    {
        var grid = new ChunkedGrid<float>(15f, chunkSize);
        grid[chunkSize + 1, chunkSize + 1] = 99f; // allocates chunk (1,1)

        float[]? cells;
        Assert.That(grid.TryGetChunk(new int2(1, 1), out cells), Is.True);
        Assert.That(cells!.Length, Is.EqualTo(grid.ChunkCells));

        // The written cell holds its value; every other cell still reads the default.
        Assert.That(cells[grid.GetLocalIndex(chunkSize + 1, chunkSize + 1)], Is.EqualTo(99f));
        Assert.That(cells[grid.GetLocalIndex(chunkSize + 5, chunkSize + 5)], Is.EqualTo(15f));
        Assert.That(grid[chunkSize + 5, chunkSize + 5], Is.EqualTo(15f));

        // GetOrCreateChunk on a missing chunk also prefills the default.
        float[] fresh = grid.GetOrCreateChunk(new int2(-5, 3));
        Assert.That(fresh.Length, Is.EqualTo(grid.ChunkCells));
        Assert.That(fresh[0], Is.EqualTo(15f));
        Assert.That(grid.ChunkCount, Is.EqualTo(2));
    }

    [Test]
    public void TestHasChunkAndTryGetChunk()
    {
        var grid = new ChunkedGrid<int>();
        Assert.That(grid.HasChunk(new int2(0, 0)), Is.False);

        grid[5, 5] = 1;
        Assert.That(grid.HasChunk(new int2(0, 0)), Is.True);
        Assert.That(grid.HasChunk(new int2(1, 0)), Is.False);

        var stringGrid = new ChunkedGrid<string>();
        Assert.That(stringGrid.TryGetChunk(new int2(0, 0), out _), Is.False);
    }

    [Test]
    public void TestClearRemovesAllChunks()
    {
        var grid = new ChunkedGrid<int>(3);
        grid[0, 0] = 1;
        grid[-100, 200] = 2;
        grid[4096, 4096] = 4;
        Assert.That(grid.ChunkCount, Is.EqualTo(3));

        grid.Clear();
        Assert.That(grid.ChunkCount, Is.EqualTo(0));
        Assert.That(grid[0, 0], Is.EqualTo(3));
        Assert.That(grid[-100, 200], Is.EqualTo(3));
        Assert.That(grid.Chunks.Count, Is.EqualTo(0));
    }

    [Test]
    public void TestChunksEnumeration()
    {
        var grid = new ChunkedGrid<int>();
        grid[0, 0] = 1;         // chunk (0,0)
        grid[63, 63] = 2;       // chunk (0,0)
        grid[64, 64] = 3;       // chunk (1,1)
        grid[-1, -1] = 4;       // chunk (-1,-1)

        Assert.That(grid.ChunkCount, Is.EqualTo(3));
        var seen = new HashSet<int2>();
        foreach (var (chunk, cells) in grid.Chunks)
        {
            seen.Add(chunk);
            Assert.That(cells.Length, Is.EqualTo(grid.ChunkCells));
        }
        Assert.That(seen, Is.EquivalentTo(new[]
        {
            new int2(0, 0),
            new int2(1, 1),
            new int2(-1, -1),
        }));
    }
}
