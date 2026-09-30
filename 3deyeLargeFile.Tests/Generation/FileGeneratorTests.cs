using LargeFileTools.Generation;
using LargeFileTools.Shared;

namespace LargeFileTools.Tests.Generation;

/// <summary>
/// Verifies reproducibility, valid output, and generator failure behavior.
/// </summary>
public sealed class FileGeneratorTests
{
    /// <summary>
    /// Repeated options produce identical bounded-size files with repeated text.
    /// </summary>
    [Fact]
    public void GeneratesRepeatableValidData()
    {
        using var directory = new TestDirectory();
        var options = new GenerateOptions
        {
            OutputPath = directory.FilePath("first.txt"),
            SizeBytes = 4096,
            RepeatedPercent = 80,
            CopiesPerText = 2,
            MinTextLength = 1,
            MaxTextLength = 25
        };
        var generator = new FileGenerator();
        var result = generator.Generate(options);
        generator.Generate(options with { OutputPath = directory.FilePath("second.txt") });
        var bytes = File.ReadAllBytes(options.OutputPath);
        Assert.Equal(bytes, File.ReadAllBytes(directory.FilePath("second.txt")));
        Assert.Equal(bytes.LongLength, result.Bytes);
        Assert.InRange(result.Bytes, options.SizeBytes, options.SizeBytes + 2L * (options.MaxTextLength + 12) - 1);
        var lines = File.ReadAllLines(options.OutputPath);
        Assert.Equal(lines.LongLength, result.Records);
        var records = lines.Select(line => RecordFormat.Parse(line, "generated", 1)).ToArray();
        Assert.Contains(records.GroupBy(record => record.Text.ToString()), group => group.Count() >= 2);
        Assert.All(records, record => Assert.InRange(record.Number, 1, 999_999_999));
        Assert.DoesNotContain((byte)'\r', bytes);
        directory.AssertClean();
    }

    /// <summary>
    /// A small byte target still publishes a complete repeated group.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 20)]
    [InlineData(4096, 1000)]
    public void GeneratesMinimumGroupForTinyTarget(int size, int copies)
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("output.txt");
        var options = new GenerateOptions
        {
            OutputPath = path, SizeBytes = size, RepeatedPercent = 1, CopiesPerText = copies
        };
        options.Validate();
        var result = new FileGenerator().Generate(options);
        var records = File.ReadAllLines(path).Select(line => RecordFormat.Parse(line, "generated", 1)).ToArray();
        Assert.Equal(copies, result.Records);
        Assert.Equal(copies, records.Length);
        Assert.Single(records.Select(record => record.Text.ToString()).Distinct(StringComparer.InvariantCulture));
        Assert.Equal(new FileInfo(path).Length, result.Bytes);
        Assert.True(result.Bytes >= size);
        directory.AssertClean();
    }

    /// <summary>
    /// The byte target can end a repeated group without filling all its copies.
    /// </summary>
    [Fact]
    public void FinishesWithAnIncompleteGroup()
    {
        using var directory = new TestDirectory();
        var options = new GenerateOptions
        {
            OutputPath = directory.FilePath("output.txt"),
            SizeBytes = 32 * 1024,
            RepeatedPercent = 100,
            CopiesPerText = 1000,
            MinTextLength = 16,
            MaxTextLength = 16
        };
        options.Validate();
        var result = new FileGenerator().Generate(options);
        var records = File.ReadLines(options.OutputPath).Select(line => RecordFormat.Parse(line, "generated", 1)).ToArray();
        var counts = records.GroupBy(record => record.Text.ToString(), StringComparer.InvariantCulture)
            .Select(group => group.Count()).ToArray();
        Assert.Equal(2, counts.Length);
        Assert.Contains(options.CopiesPerText, counts);
        Assert.InRange(Assert.Single(counts, count => count < options.CopiesPerText), 2, options.CopiesPerText - 1);
        Assert.InRange(result.Bytes, options.SizeBytes, options.SizeBytes + 2L * (options.MaxTextLength + 12) - 1);
        directory.AssertClean();
    }

    /// <summary>
    /// Cancellation never publishes partial output.
    /// </summary>
    [Fact]
    public void CancelsWithoutPublishingOutput()
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("output.txt");
        using var stop = new CancellationTokenSource();
        var options = new GenerateOptions { OutputPath = path, SizeBytes = 4096 };
        Assert.Throws<OperationCanceledException>(() => new FileGenerator().Generate(options, stop.Token, _ => stop.Cancel()));
        Assert.False(File.Exists(path));
        directory.AssertClean();
    }

    /// <summary>
    /// Unbounded text allocations are rejected before creating output.
    /// </summary>
    [Fact]
    public void RejectsUnboundedTextLength()
    {
        using var directory = new TestDirectory();
        var options = new GenerateOptions
        {
            OutputPath = directory.FilePath("output.txt"), SizeBytes = 4096, MaxTextLength = int.MaxValue
        };
        Assert.Throws<CliException>(options.Validate);
        Assert.Empty(Directory.GetFiles(directory.Root));
    }

    /// <summary>
    /// Default-length files meet the minimum repeated share and intended group sizes.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(20, 2)]
    [InlineData(80, 20)]
    public void GeneratesRequestedDistribution(int percent, int copies)
    {
        using var directory = new TestDirectory();
        var options = new GenerateOptions
        {
            OutputPath = directory.FilePath("output.txt"),
            SizeBytes = 1 << 20,
            Seed = 42,
            RepeatedPercent = percent,
            CopiesPerText = copies
        };
        options.Validate();
        var result = new FileGenerator().Generate(options);
        var counts = File.ReadLines(options.OutputPath)
            .Select(line => RecordFormat.Parse(line, "generated", 1).Text.ToString())
            .GroupBy(text => text, StringComparer.InvariantCulture)
            .Select(group => group.Count()).ToArray();
        Assert.All(counts, count => Assert.InRange(count, 1, copies));
        Assert.Contains(copies, counts);
        var repeated = counts.Where(count => count > 1).Sum();
        Assert.InRange(repeated - result.Records * percent / 100.0, 0, 2);
        Assert.True(counts.Count(count => count > 1 && count < copies) <= 1);
        directory.AssertClean();
    }

    /// <summary>
    /// Shuffling changes only order, including across full buffers and the final tail.
    /// </summary>
    [Theory]
    [InlineData(4096)]
    [InlineData(5 * 1024 * 1024)]
    public void ShufflesWithoutChangingGeneratedRecords(int sizeBytes)
    {
        using var directory = new TestDirectory();
        var options = new GenerateOptions
        {
            OutputPath = directory.FilePath("output.txt"),
            SizeBytes = sizeBytes,
            RepeatedPercent = 80,
            CopiesPerText = 20
        };
        var expected = new RecordSource(options).Generate().ToArray();
        var expectedBytes = expected.Sum(line => RecordFormat.Encoding.GetByteCount(line) + 1L);

        var result = new FileGenerator().Generate(options);
        var actual = File.ReadAllLines(options.OutputPath);
        Assert.Equal(expectedBytes, result.Bytes);
        Assert.Equal(expected.Length, result.Records);
        Assert.False(expected.SequenceEqual(actual));
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        directory.AssertClean();
    }
}
