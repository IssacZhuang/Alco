using System.Text;
using BenchmarkDotNet.Attributes;

namespace Alco.Benchmark;

/// <summary>
/// Benchmark to compare performance of SpanStringBuilder vs StringBuilder.
/// Tests various scenarios including small string operations, large string building,
/// character appending, and mixed content operations.
/// </summary>
public class BenchmarkStringBuilder
{
    private readonly string[] _testStrings =
    {
        "Hello", "World", "Test", "String", "Builder", "Performance", "Benchmark", "Comparison"
    };

    private readonly string _longString = new string('A', 1000);

    [Params(10, 100, 1000)]
    public int Operations { get; set; }

    /// <summary>
    /// Benchmark small string concatenation using SpanStringBuilder
    /// </summary>
    [Benchmark(Description = "SpanStringBuilder - Small Strings")]
    public string SpanStringBuilderSmall()
    {
        var sb = new SpanStringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append(_testStrings[i % _testStrings.Length]);
            if (i % 2 == 0) sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark small string concatenation using StringBuilder
    /// </summary>
    [Benchmark(Description = "StringBuilder - Small Strings")]
    public string StringBuilderSmall()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append(_testStrings[i % _testStrings.Length]);
            if (i % 2 == 0) sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark character appending using SpanStringBuilder
    /// </summary>
    [Benchmark(Description = "SpanStringBuilder - Characters")]
    public string SpanStringBuilderChars()
    {
        var sb = new SpanStringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append((char)('A' + (i % 26)));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark character appending using StringBuilder
    /// </summary>
    [Benchmark(Description = "StringBuilder - Characters")]
    public string StringBuilderChars()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append((char)('A' + (i % 26)));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark number formatting using SpanStringBuilder
    /// </summary>
    [Benchmark(Description = "SpanStringBuilder - Numbers")]
    public string SpanStringBuilderNumbers()
    {
        var sb = new SpanStringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append(i);
            if (i % 10 == 0) sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark number formatting using StringBuilder
    /// </summary>
    [Benchmark(Description = "StringBuilder - Numbers")]
    public string StringBuilderNumbers()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append(i);
            if (i % 10 == 0) sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark line-based operations using SpanStringBuilder
    /// </summary>
    [Benchmark(Description = "SpanStringBuilder - Lines")]
    public string SpanStringBuilderLines()
    {
        var sb = new SpanStringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append("Line ");
            sb.Append(i);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark line-based operations using StringBuilder
    /// </summary>
    [Benchmark(Description = "StringBuilder - Lines")]
    public string StringBuilderLines()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append("Line ");
            sb.Append(i);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark mixed content operations using SpanStringBuilder
    /// </summary>
    [Benchmark(Description = "SpanStringBuilder - Mixed")]
    public string SpanStringBuilderMixed()
    {
        var sb = new SpanStringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append("Item: ");
            sb.Append(i);
            sb.Append(", Value: ");
            sb.Append(i * 3.14f);
            sb.Append(", Active: ");
            sb.Append(i % 2 == 0);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark mixed content operations using StringBuilder
    /// </summary>
    [Benchmark(Description = "StringBuilder - Mixed")]
    public string StringBuilderMixed()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Operations; i++)
        {
            sb.Append("Item: ");
            sb.Append(i);
            sb.Append(", Value: ");
            sb.Append(i * 3.14f);
            sb.Append(", Active: ");
            sb.Append(i % 2 == 0);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark large string operations using SpanStringBuilder
    /// </summary>
    [Benchmark(Description = "SpanStringBuilder - Large Strings")]
    public string SpanStringBuilderLarge()
    {
        var sb = new SpanStringBuilder();
        for (int i = 0; i < Math.Min(Operations, 100); i++) // Limit for large strings
        {
            sb.Append(_longString);
            sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark large string operations using StringBuilder
    /// </summary>
    [Benchmark(Description = "StringBuilder - Large Strings")]
    public string StringBuilderLarge()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Math.Min(Operations, 100); i++) // Limit for large strings
        {
            sb.Append(_longString);
            sb.Append(' ');
        }
        return sb.ToString();
    }
}