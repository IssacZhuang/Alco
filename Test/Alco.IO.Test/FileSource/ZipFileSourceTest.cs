using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Alco.IO;

namespace Alco.IO.Test;

/// <summary>
/// Test fixture for ZipFileSource functionality including reading, writing, 
/// thread safety, error handling, and disposal scenarios.
/// </summary>
[TestFixture]
public class ZipFileSourceTest
{
    /// <summary>
    /// Creates a memory stream with a ZIP archive containing test files.
    /// </summary>
    /// <param name="mode">The ZIP archive mode to use.</param>
    /// <returns>A memory stream with the ZIP archive.</returns>
    private static MemoryStream CreateTestZipStream(ZipArchiveMode mode = ZipArchiveMode.Read)
    {
        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Add a simple text file
            var textEntry = archive.CreateEntry("text.txt");
            using (var entryStream = textEntry.Open())
            {
                var textData = "Hello, World!"u8;
                entryStream.Write(textData);
            }

            // Add a file in a subdirectory
            var subDirEntry = archive.CreateEntry("subfolder/data.bin");
            using (var entryStream = subDirEntry.Open())
            {
                var binaryData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
                entryStream.Write(binaryData);
            }

            // Add an empty file
            var emptyEntry = archive.CreateEntry("empty.txt");
            using (var entryStream = emptyEntry.Open())
            {
                // Write nothing - this creates an empty file
            }

            // Add a file with backslashes in the path (Windows-style)
            var windowsPathEntry = archive.CreateEntry("windows\\path\\file.txt");
            using (var entryStream = windowsPathEntry.Open())
            {
                var windowsData = "Windows path test"u8;
                entryStream.Write(windowsData);
            }
        }

        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Creates an empty memory stream for testing write operations.
    /// </summary>
    /// <returns>An empty memory stream.</returns>
    private static MemoryStream CreateEmptyZipStream()
    {
        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Create an empty archive
        }
        memoryStream.Position = 0;
        return memoryStream;
    }

    [Test]
    public void Constructor_WithValidStream_ShouldInitializeCorrectly()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();

        // Act
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");

        // Assert
        Assert.That(zipFileSource.Name, Is.EqualTo("test_zip"));
        Assert.That(zipFileSource.Priority, Is.EqualTo(0));
        Assert.That(zipFileSource.IsWriteable, Is.False);
        Assert.That(zipFileSource.AllFileNames.Count(), Is.EqualTo(4));
    }

    [Test]
    public void Constructor_WithWriteableMode_ShouldSetWriteableToTrue()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();

        // Act
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Update, "test_zip");

        // Assert
        Assert.That(zipFileSource.IsWriteable, Is.True);
    }

    [Test]
    public void Constructor_WithCreateMode_ShouldSetWriteableToTrue()
    {
        // Arrange
        using var zipStream = new MemoryStream();

        // Act
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Create, "test_zip");

        // Assert
        Assert.That(zipFileSource.IsWriteable, Is.True);
    }

    [Test]
    public void Constructor_WithInvalidStream_ShouldThrowIOException()
    {
        // Arrange
        using var invalidStream = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 }); // Invalid ZIP data

        // Act & Assert
        var ex = Assert.Throws<IOException>(() => new ZipFileSource(invalidStream, ZipArchiveMode.Read, "invalid_zip"));
        Assert.That(ex.Message, Does.Contain("Failed to open ZIP stream: invalid_zip"));
    }

    [Test]
    public void AllFileNames_ShouldReturnNormalizedPaths()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");

        // Act
        var fileNames = zipFileSource.AllFileNames.ToList();

        // Assert
        Assert.That(fileNames, Contains.Item("text.txt"));
        Assert.That(fileNames, Contains.Item("subfolder/data.bin"));
        Assert.That(fileNames, Contains.Item("empty.txt"));
        Assert.That(fileNames, Contains.Item("windows/path/file.txt")); // Should be normalized
        Assert.That(fileNames.Count, Is.EqualTo(4));
    }

    [Test]
    public void TryGetData_WithExistingFile_ShouldReturnData()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");

        // Act
        var result = zipFileSource.TryGetData("text.txt", out var data, out var failureReason);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(failureReason, Is.Null);
        using (data)
        {
            var text = Encoding.UTF8.GetString(data.AsSpan());
            Assert.That(text, Is.EqualTo("Hello, World!"));
        }
    }

    [Test]
    public void TryGetData_WithSubfolderFile_ShouldReturnData()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");

        // Act
        var result = zipFileSource.TryGetData("subfolder/data.bin", out var data, out var failureReason);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(failureReason, Is.Null);
        using (data)
        {
            var bytes = data.AsSpan().ToArray();
            Assert.That(bytes, Is.EqualTo(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }));
        }
    }

    [Test]
    public void TryGetData_WithBackslashPath_ShouldNormalizeAndReturnData()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");

        // Act
        var result = zipFileSource.TryGetData("windows\\path\\file.txt", out var data, out var failureReason);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(failureReason, Is.Null);
        using (data)
        {
            var text = Encoding.UTF8.GetString(data.AsSpan());
            Assert.That(text, Is.EqualTo("Windows path test"));
        }
    }

    [Test]
    public void TryGetData_WithEmptyFile_ShouldReturnEmptyData()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");

        // Act
        var result = zipFileSource.TryGetData("empty.txt", out var data, out var failureReason);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(failureReason, Is.Null);
        using (data)
        {
            Assert.That(data.Size, Is.EqualTo(0));
        }
    }

    [Test]
    public void TryGetData_WithNonExistentFile_ShouldReturnFalse()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");

        // Act
        var result = zipFileSource.TryGetData("nonexistent.txt", out var data, out var failureReason);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(failureReason, Does.Contain("File not found in ZIP archive: nonexistent.txt"));
        Assert.That(data, Is.EqualTo(SafeMemoryHandle.Empty));
    }

    [Test]
    public void TryGetData_AfterDisposal_ShouldReturnFalse()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");
        zipFileSource.Dispose();

        // Act
        var result = zipFileSource.TryGetData("text.txt", out var data, out var failureReason);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(failureReason, Is.EqualTo("ZipFileSource has been disposed"));
        Assert.That(data, Is.EqualTo(SafeMemoryHandle.Empty));
    }

    [Test]
    public void TryWriteData_WithReadOnlyArchive_ShouldReturnFalse()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");
        var testData = "New data"u8;

        // Act
        var result = zipFileSource.TryWriteData("new_file.txt", testData, out var failureReason);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(failureReason, Is.EqualTo("ZIP archive is read-only"));
    }

    [Test]
    public void TryWriteData_WithWriteableArchive_ShouldCreateNewFile()
    {
        // Arrange
        using var zipStream = CreateEmptyZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Update, "test_zip");
        var testData = "New file content"u8;

        // Act
        var result = zipFileSource.TryWriteData("new_file.txt", testData, out var failureReason);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(failureReason, Is.Null);

        // Verify the file was added
        Assert.That(zipFileSource.AllFileNames, Contains.Item("new_file.txt"));

        // Verify we can read the data back
        var readResult = zipFileSource.TryGetData("new_file.txt", out var data, out var readFailureReason);
        Assert.That(readResult, Is.True);
        using (data)
        {
            var content = Encoding.UTF8.GetString(data.AsSpan());
            Assert.That(content, Is.EqualTo("New file content"));
        }
    }

    [Test]
    public void TryWriteData_WithBackslashPath_ShouldNormalizePath()
    {
        // Arrange
        using var zipStream = CreateEmptyZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Update, "test_zip");
        var testData = "Backslash path content"u8;

        // Act
        var result = zipFileSource.TryWriteData("folder\\subfolder\\file.txt", testData, out var failureReason);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(failureReason, Is.Null);

        // Verify the file was added with normalized path
        Assert.That(zipFileSource.AllFileNames, Contains.Item("folder/subfolder/file.txt"));

        // Verify we can read the data back with either path format
        var readResult1 = zipFileSource.TryGetData("folder/subfolder/file.txt", out var data1, out _);
        var readResult2 = zipFileSource.TryGetData("folder\\subfolder\\file.txt", out var data2, out _);

        Assert.That(readResult1, Is.True);
        Assert.That(readResult2, Is.True);

        using (data1)
        using (data2)
        {
            var content1 = Encoding.UTF8.GetString(data1.AsSpan());
            var content2 = Encoding.UTF8.GetString(data2.AsSpan());
            Assert.That(content1, Is.EqualTo("Backslash path content"));
            Assert.That(content2, Is.EqualTo("Backslash path content"));
        }
    }

    [Test]
    public void TryWriteData_AfterDisposal_ShouldReturnFalse()
    {
        // Arrange
        using var zipStream = CreateEmptyZipStream();
        var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Update, "test_zip");
        zipFileSource.Dispose();
        var testData = "Test data"u8;

        // Act
        var result = zipFileSource.TryWriteData("test.txt", testData, out var failureReason);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(failureReason, Is.EqualTo("ZipFileSource has been disposed"));
    }

    [Test]
    public void ThreadSafety_ConcurrentReads_ShouldNotFail()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");
        const int threadCount = 10;
        const int operationsPerThread = 100;
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        var result = zipFileSource.TryGetData("text.txt", out var data, out var failureReason);
                        Assert.That(result, Is.True);
                        using (data)
                        {
                            var content = Encoding.UTF8.GetString(data.AsSpan());
                            Assert.That(content, Is.EqualTo("Hello, World!"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.That(exceptions, Is.Empty, $"Exceptions occurred: {string.Join("; ", exceptions.Select(e => e.Message))}");
    }

    [Test]
    public void ThreadSafety_ConcurrentWrites_ShouldNotFail()
    {
        // Arrange
        using var zipStream = CreateEmptyZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Update, "test_zip");
        const int threadCount = 10;
        const int operationsPerThread = 10;
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        var fileName = $"thread_{threadId}_file_{j}.txt";
                        var content = Encoding.UTF8.GetBytes($"Thread {threadId}, Operation {j}");

                        var result = zipFileSource.TryWriteData(fileName, content, out var failureReason);
                        Assert.That(result, Is.True, $"Failed to write {fileName}: {failureReason}");
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.That(exceptions, Is.Empty, $"Exceptions occurred: {string.Join("; ", exceptions.Select(e => e.Message))}");

        // Verify all files were created
        var fileCount = zipFileSource.AllFileNames.Count();
        Assert.That(fileCount, Is.EqualTo(threadCount * operationsPerThread));
    }

    [Test]
    public void ThreadSafety_MixedOperations_ShouldNotFail()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        using var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Update, "test_zip");
        const int threadCount = 10;
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    // Mix of read and write operations
                    for (int j = 0; j < 50; j++)
                    {
                        if (j % 2 == 0)
                        {
                            // Read operation
                            var result = zipFileSource.TryGetData("text.txt", out var data, out var failureReason);
                            Assert.That(result, Is.True);
                            using (data)
                            {
                                var content = Encoding.UTF8.GetString(data.AsSpan());
                                Assert.That(content, Is.EqualTo("Hello, World!"));
                            }
                        }
                        else
                        {
                            // Write operation
                            var fileName = $"mixed_thread_{threadId}_op_{j}.txt";
                            var content = Encoding.UTF8.GetBytes($"Mixed operation data {threadId}_{j}");

                            var result = zipFileSource.TryWriteData(fileName, content, out var failureReason);
                            Assert.That(result, Is.True);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.That(exceptions, Is.Empty, $"Exceptions occurred: {string.Join("; ", exceptions.Select(e => e.Message))}");
    }

    [Test]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");

        // Act
        zipFileSource.Dispose();

        // Assert
        Assert.That(zipFileSource.IsDisposed, Is.True);

        // Further operations should fail gracefully
        var result = zipFileSource.TryGetData("text.txt", out var data, out var failureReason);
        Assert.That(result, Is.False);
        Assert.That(failureReason, Is.EqualTo("ZipFileSource has been disposed"));
    }

    [Test]
    public void Dispose_MultipleCallsShouldNotThrow()
    {
        // Arrange
        using var zipStream = CreateTestZipStream();
        var zipFileSource = new ZipFileSource(zipStream, ZipArchiveMode.Read, "test_zip");

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            zipFileSource.Dispose();
            zipFileSource.Dispose(); // Second call should not throw
            zipFileSource.Dispose(); // Third call should not throw
        });
    }

    [Test]
    public void Performance_LargeNumberOfFiles_ShouldPerformWell()
    {
        // Arrange
        const int fileCount = 1000;
        var memoryStream = new MemoryStream();

        // Create a ZIP with many files
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            for (int i = 0; i < fileCount; i++)
            {
                var entry = archive.CreateEntry($"file_{i:D4}.txt");
                using var entryStream = entry.Open();
                var content = Encoding.UTF8.GetBytes($"Content of file {i}");
                entryStream.Write(content);
            }
        }

        memoryStream.Position = 0;
        using var zipFileSource = new ZipFileSource(memoryStream, ZipArchiveMode.Read, "performance_test");

        // Act & Assert
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Test enumeration performance
        var allFiles = zipFileSource.AllFileNames.ToList();
        Assert.That(allFiles.Count, Is.EqualTo(fileCount));

        // Test random access performance
        for (int i = 0; i < 100; i++)
        {
            var fileName = $"file_{i:D4}.txt";
            var result = zipFileSource.TryGetData(fileName, out var data, out var failureReason);
            Assert.That(result, Is.True);
            using (data)
            {
                var content = Encoding.UTF8.GetString(data.AsSpan());
                Assert.That(content, Is.EqualTo($"Content of file {i}"));
            }
        }

        stopwatch.Stop();

        // Performance assertion - should complete within reasonable time
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000), "Performance test took too long");
    }
}