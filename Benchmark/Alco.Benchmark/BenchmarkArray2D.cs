// using System.Runtime.InteropServices;
// using BenchmarkDotNet.Attributes;
// using BenchmarkFramework;

// namespace Alco.Benchmark;

// /// <summary>
// /// Benchmark to compare performance of different 2D grid implementations.
// /// </summary>
// [CustomConfigParam(10, 16, 128)]
// public class BenchmarkArray2D
// {
//     // Different implementations of 2D grid
//     private int[] _grid1;      // Flatten 1D array (simulating 2D)
//     private int[,] _grid2;     // Native 2D array
//     private int[][] _grid3;    // Jagged array

//     [Params(100, 1000, 5000)]
//     public int Size { get; set; }

//     [GlobalSetup]
//     public void Setup()
//     {
//         // Initialize grids
//         _grid1 = new int[Size * Size];
//         _grid2 = new int[Size, Size];
//         _grid3 = new int[Size][];
//         for (int i = 0; i < Size; i++)
//         {
//             _grid3[i] = new int[Size];
//         }

//         // Fill with initial data
//         for (int y = 0; y < Size; y++)
//         {
//             for (int x = 0; x < Size; x++)
//             {
//                 int value = y * Size + x;
//                 _grid1[y * Size + x] = value;
//                 _grid2[y, x] = value;
//                 _grid3[y][x] = value;
//             }
//         }
//     }

//     [Benchmark]
//     public int ReadFlatten1D()
//     {
//         int sum = 0;
//         for (int y = 0; y < Size; y++)
//         {
//             for (int x = 0; x < Size; x++)
//             {
//                 sum += _grid1[y * Size + x];
//             }
//         }
//         return sum;
//     }

//     [Benchmark]
//     public int ReadNative2D()
//     {
//         int sum = 0;
//         for (int y = 0; y < Size; y++)
//         {
//             for (int x = 0; x < Size; x++)
//             {
//                 sum += _grid2[y, x];
//             }
//         }
//         return sum;
//     }

//     [Benchmark]
//     public int ReadJagged()
//     {
//         int sum = 0;
//         for (int y = 0; y < Size; y++)
//         {
//             for (int x = 0; x < Size; x++)
//             {
//                 sum += _grid3[y][x];
//             }
//         }
//         return sum;
//     }

//     [Benchmark]
//     public void WriteFlatten1D()
//     {
//         for (int y = 0; y < Size; y++)
//         {
//             for (int x = 0; x < Size; x++)
//             {
//                 _grid1[y * Size + x] = x + y;
//             }
//         }
//     }

//     [Benchmark]
//     public void WriteNative2D()
//     {
//         for (int y = 0; y < Size; y++)
//         {
//             for (int x = 0; x < Size; x++)
//             {
//                 _grid2[y, x] = x + y;
//             }
//         }
//     }

//     [Benchmark]
//     public void WriteJagged()
//     {
//         for (int y = 0; y < Size; y++)
//         {
//             for (int x = 0; x < Size; x++)
//             {
//                 _grid3[y][x] = x + y;
//             }
//         }
//     }
// }