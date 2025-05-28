using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Alco.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkFramework;

namespace Alco.Engine.Benchmark;

/// <summary>
/// Benchmark comparing Alco.IO Package (Valve Pak) vs ZIP performance
/// Tests initialization, reading, and writing operations without compression
/// </summary>
[CustomConfigParam(2, 4, 16)]
[MemoryDiagnoser]
public class BenchmarkPackage
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "AlcoPackageBenchmark");
    private readonly string _packageFile = Path.Combine(Path.GetTempPath(), "AlcoPackageBenchmark", "test.vpk");
    private readonly string _zipFile = Path.Combine(Path.GetTempPath(), "AlcoPackageBenchmark", "test.zip");


    private readonly Dictionary<string, byte[]> _testFiles = new();
    private Package? _package;
    private ZipArchive? _zipArchive;
    private FileStream? _zipStream;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Create test directory
        Directory.CreateDirectory(_testDirectory);

        // Generate test files with different sizes at runtime

        GenerateTestFiles();

        // Create initial archives

        CreateInitialPackage();
        CreateInitialZip();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _package?.Dispose();
        _zipArchive?.Dispose();
        _zipStream?.Dispose();


        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    /// <summary>
    /// Generate test files with various sizes for realistic testing
    /// </summary>
    private void GenerateTestFiles()
    {
        var random = new System.Random(42); // Fixed seed for reproducible results

        // Small files (1KB - 10KB)

        for (int i = 0; i < 50; i++)
        {
            var size = random.Next(1024, 10240);
            var data = new byte[size];
            random.NextBytes(data);
            _testFiles[$"small_{i:D3}.dat"] = data;
        }

        // Medium files (100KB - 1MB)

        for (int i = 0; i < 20; i++)
        {
            var size = random.Next(102400, 1048576);
            var data = new byte[size];
            random.NextBytes(data);
            _testFiles[$"medium_{i:D3}.dat"] = data;
        }

        // Large files (5MB - 10MB)

        for (int i = 0; i < 5; i++)
        {
            var size = random.Next(5242880, 10485760);
            var data = new byte[size];
            random.NextBytes(data);
            _testFiles[$"large_{i:D3}.dat"] = data;
        }

        // Text files

        for (int i = 0; i < 10; i++)
        {
            var content = GenerateTextContent(random.Next(1000, 50000));
            _testFiles[$"text_{i:D3}.txt"] = Encoding.UTF8.GetBytes(content);
        }
    }

    /// <summary>
    /// Generate text content for testing
    /// </summary>
    private string GenerateTextContent(int length)
    {
        var sb = new StringBuilder(length);
        var random = new System.Random(42);
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 \n";


        for (int i = 0; i < length; i++)
        {
            sb.Append(chars[random.Next(chars.Length)]);
        }


        return sb.ToString();
    }

    /// <summary>
    /// Create initial Package archive for testing
    /// </summary>
    private void CreateInitialPackage()
    {
        using var package = new Package();


        foreach (var file in _testFiles)
        {
            package.AddFile(file.Key, file.Value);
        }


        package.Write(_packageFile);
    }

    /// <summary>
    /// Create initial ZIP archive for testing (without compression)
    /// </summary>
    private void CreateInitialZip()
    {
        using var zipStream = new FileStream(_zipFile, FileMode.Create);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);


        foreach (var file in _testFiles)
        {
            var entry = zip.CreateEntry(file.Key, CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            entryStream.Write(file.Value);
        }
    }

    #region Initialization Benchmarks

    /// <summary>
    /// Benchmark Package initialization (opening existing archive)
    /// </summary>
    [Benchmark]
    public void Package_Initialize()
    {
        using var package = new Package();
        package.Read(_packageFile);
    }

    /// <summary>
    /// Benchmark ZIP initialization (opening existing archive)
    /// </summary>
    [Benchmark]
    public void Zip_Initialize()
    {
        using var zipStream = new FileStream(_zipFile, FileMode.Open, FileAccess.Read);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // Enumerate entries to ensure full initialization

        var entryCount = zip.Entries.Count;
    }

    #endregion

    #region Reading Benchmarks

    /// <summary>
    /// Benchmark reading all files from Package
    /// </summary>
    [Benchmark]
    public long Package_ReadAllFiles()
    {
        using var package = new Package();
        package.Read(_packageFile);


        long totalBytes = 0;
        foreach (var entry in package.AllEntries)
        {
            package.ReadEntry(entry, out var data, validateCrc: false);
            totalBytes += data.Length;
        }


        return totalBytes;
    }

    /// <summary>
    /// Benchmark reading all files from ZIP
    /// </summary>
    [Benchmark]
    public long Zip_ReadAllFiles()
    {
        using var zipStream = new FileStream(_zipFile, FileMode.Open, FileAccess.Read);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);


        long totalBytes = 0;
        foreach (var entry in zip.Entries)
        {
            using var entryStream = entry.Open();
            byte[] buffer = new byte[entry.Length];
            totalBytes += entryStream.Read(buffer);
        }


        return totalBytes;
    }

    /// <summary>
    /// Benchmark reading specific files from Package
    /// </summary>
    [Benchmark]
    public byte[] Package_ReadSpecificFile()
    {
        using var package = new Package();
        package.Read(_packageFile);


        var entry = package.FindEntry("medium_001.dat");
        if (entry != null)
        {
            package.ReadEntry(entry, out var data, validateCrc: false);
            return data;
        }


        return Array.Empty<byte>();
    }

    /// <summary>
    /// Benchmark reading specific files from ZIP
    /// </summary>
    [Benchmark]
    public byte[] Zip_ReadSpecificFile()
    {
        using var zipStream = new FileStream(_zipFile, FileMode.Open, FileAccess.Read);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);


        var entry = zip.GetEntry("medium_001.dat");
        if (entry != null)
        {
            using var entryStream = entry.Open();
            byte[] buffer = new byte[entry.Length];
            _= entryStream.Read(buffer);
            return buffer;
        }


        return Array.Empty<byte>();
    }

    #endregion

    #region Writing Benchmarks

    /// <summary>
    /// Benchmark creating new Package with all test files
    /// </summary>
    [Benchmark]
    public void Package_WriteAllFiles()
    {
        var outputFile = Path.Combine(_testDirectory, $"benchmark_package_{Guid.NewGuid():N}.vpk");


        using var package = new Package();


        foreach (var file in _testFiles)
        {
            package.AddFile(file.Key, file.Value);
        }


        package.Write(outputFile);

        // Cleanup

        File.Delete(outputFile);
    }

    /// <summary>
    /// Benchmark creating new ZIP with all test files (no compression)
    /// </summary>
    [Benchmark]
    public void Zip_WriteAllFiles()
    {
        var outputFile = Path.Combine(_testDirectory, $"benchmark_zip_{Guid.NewGuid():N}.zip");


        using (var zipStream = new FileStream(outputFile, FileMode.Create))
        {
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {


                foreach (var file in _testFiles)
                {
                    var entry = zip.CreateEntry(file.Key, CompressionLevel.NoCompression);
                    using var entryStream = entry.Open();
                    entryStream.Write(file.Value);
                }

            }
        }

        // Cleanup happens when using statements dispose

        File.Delete(outputFile);
    }

    /// <summary>
    /// Benchmark adding single file to existing Package
    /// </summary>
    [Benchmark]
    public void Package_AddSingleFile()
    {
        var outputFile = Path.Combine(_testDirectory, $"add_package_{Guid.NewGuid():N}.vpk");


        using var package = new Package();

        // Add a few base files

        for (int i = 0; i < 5; i++)
        {
            var fileName = $"small_{i:D3}.dat";
            if (_testFiles.TryGetValue(fileName, out var data))
            {
                package.AddFile(fileName, data);
            }
        }

        // Add the test file

        package.AddFile("new_file.dat", _testFiles["medium_001.dat"]);


        package.Write(outputFile);

        // Cleanup

        File.Delete(outputFile);
    }

    /// <summary>
    /// Benchmark adding single file to existing ZIP
    /// /// </summary>
    [Benchmark]
    public void Zip_AddSingleFile()
    {
        var outputFile = Path.Combine(_testDirectory, $"add_zip_{Guid.NewGuid():N}.zip");


        using (var zipStream = new FileStream(outputFile, FileMode.Create))
        {
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                // Add a few base files

                for (int i = 0; i < 5; i++)
                {
                    var fileName = $"small_{i:D3}.dat";
                    if (_testFiles.TryGetValue(fileName, out var data))
                    {
                        var entry = zip.CreateEntry(fileName, CompressionLevel.NoCompression);
                        using var entryStream = entry.Open();
                        entryStream.Write(data);
                    }
                }

                // Add the test file

                var newEntry = zip.CreateEntry("new_file.dat", CompressionLevel.NoCompression);
                using var newEntryStream = newEntry.Open();
                newEntryStream.Write(_testFiles["medium_001.dat"]);

                // Cleanup happens when using statements dispose
            }
        }


        File.Delete(outputFile);
    }

    #endregion
}
