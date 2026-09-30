using System.Globalization;
using LargeFileTools.Generation;
using LargeFileTools.Shared;

namespace LargeFileTools.Tests.Generation;

/// <summary>
/// Checks generation argument bounds and defaults.
/// </summary>
public sealed class GenerateOptionsTests
{
    /// <summary>
    /// Explicit repetition arguments are parsed and validated together.
    /// </summary>
    [Fact]
    public void ParsesRepetitionSettings()
    {
        using var directory = new TestDirectory();
        var options = GenerateOptions.Parse(["--output", directory.FilePath("output.txt"), "--size", "1",
            "--repeated-percent", "80", "--copies-per-text", "20"]);
        Assert.Equal(80, options.RepeatedPercent);
        Assert.Equal(20, options.CopiesPerText);
        Assert.Equal(1 << 20, options.SizeBytes);
    }

    /// <summary>
    /// The minimum repetition percentage and copy count are accepted together.
    /// </summary>
    [Fact]
    public void AcceptsMinimumRepetitionSettings()
    {
        using var directory = new TestDirectory();
        var options = GenerateOptions.Parse(["--output", directory.FilePath("output.txt"), "--size", "1",
            "--repeated-percent", "1", "--copies-per-text", "2"]);
        Assert.Equal(1, options.RepeatedPercent);
        Assert.Equal(2, options.CopiesPerText);
    }

    /// <summary>
    /// Defaults select a modest share of paired texts.
    /// </summary>
    [Fact]
    public void UsesPairedDefaults()
    {
        using var directory = new TestDirectory();
        var options = GenerateOptions.Parse(["--output", directory.FilePath("output.txt"), "--size", "1"]);
        Assert.Equal(20, options.RepeatedPercent);
        Assert.Equal(2, options.CopiesPerText);
        Assert.Equal(8L * 1024 * 1024, options.ShuffleBufferBytes);
        Assert.Equal(options.ShuffleBufferBytes,
            new GenerateOptions { OutputPath = options.OutputPath, SizeBytes = options.SizeBytes }.ShuffleBufferBytes);
    }

    /// <summary>
    /// Omitting the seed does not select a fixed value for every invocation.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChoosesRandomSeedByDefault(bool parse)
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("output.txt");
        var seeds = Enumerable.Range(0, 8).Select(_ => parse
            ? GenerateOptions.Parse(["--output", path, "--size", "1"]).Seed
            : new GenerateOptions { OutputPath = path, SizeBytes = 1 << 20 }.Seed).ToArray();
        Assert.True(seeds.Distinct().Count() > 1);
        Assert.Empty(Directory.GetFiles(directory.Root));
    }

    /// <summary>
    /// Explicit seeds preserve every signed Int32 value, including zero.
    /// </summary>
    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void PreservesExplicitSeed(int seed)
    {
        using var directory = new TestDirectory();
        var options = GenerateOptions.Parse(["--output", directory.FilePath("output.txt"), "--size", "1",
            "--seed", seed.ToString(CultureInfo.InvariantCulture)]);
        Assert.Equal(seed, options.Seed);
    }

    /// <summary>
    /// Malformed or overflowing seeds fail instead of silently choosing randomness.
    /// </summary>
    [Theory]
    [InlineData("1.5")]
    [InlineData("random")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    public void RejectsInvalidSeed(string seed)
    {
        using var directory = new TestDirectory();
        var error = Assert.Throws<CliException>(() => GenerateOptions.Parse(
            ["--output", directory.FilePath("output.txt"), "--size", "1", "--seed", seed]));
        Assert.Contains("--seed", error.Message);
        Assert.Empty(Directory.GetFiles(directory.Root));
    }

    /// <summary>
    /// Shuffle sizes are parsed as whole megabytes within the memory budget.
    /// </summary>
    [Theory]
    [InlineData("1", 1_048_576L)]
    [InlineData("8", 8_388_608L)]
    [InlineData("16", 16_777_216L)]
    [InlineData("31", 32_505_856L)]
    public void ParsesShuffleBufferSize(string value, long bytes)
    {
        using var directory = new TestDirectory();
        var options = GenerateOptions.Parse(["--output", directory.FilePath("output.txt"), "--size", "1",
            "--shuffle-buffer-size", value]);
        Assert.Equal(bytes, options.ShuffleBufferBytes);
    }

    /// <summary>
    /// Invalid sizes and values exceeding the generation budget fail before output.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("8MB")]
    [InlineData("32")]
    [InlineData("2147483647")]
    [InlineData("2147483648")]
    public void RejectsInvalidShuffleBufferSize(string value)
    {
        using var directory = new TestDirectory();
        var error = Assert.Throws<CliException>(() => GenerateOptions.Parse(
            ["--output", directory.FilePath("output.txt"), "--size", "1", "--shuffle-buffer-size", value]));
        Assert.Contains("--shuffle-buffer-size", error.Message);
        Assert.Empty(Directory.GetFiles(directory.Root));
    }

    /// <summary>
    /// The chosen shuffle size is validated together with text construction storage.
    /// </summary>
    [Fact]
    public void AccountsForShuffleAndTextMemoryTogether()
    {
        using var directory = new TestDirectory();
        var options = new GenerateOptions
        {
            OutputPath = directory.FilePath("output.txt"),
            SizeBytes = 16L << 20,
            MaxTextLength = 1 << 20,
            ShuffleBufferBytes = 24L << 20
        };
        options.Validate();
        var error = Assert.Throws<CliException>((options with { ShuffleBufferBytes = 28L << 20 }).Validate);
        Assert.Contains("64 MB", error.Message);
        Assert.Contains("--max-text-length", error.Message);
    }

    /// <summary>
    /// Direct options reject invalid byte budgets without arithmetic overflow.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void RejectsInvalidShuffleByteBudget(long bytes)
    {
        using var directory = new TestDirectory();
        var options = new GenerateOptions
        {
            OutputPath = directory.FilePath("output.txt"),
            SizeBytes = 1 << 20,
            ShuffleBufferBytes = bytes
        };
        Assert.Throws<CliException>(options.Validate);
    }

    /// <summary>
    /// Invalid repetition ranges and the removed pool option are rejected.
    /// </summary>
    [Theory]
    [InlineData("--repeated-percent", "0")]
    [InlineData("--repeated-percent", "-1")]
    [InlineData("--repeated-percent", "101")]
    [InlineData("--repeated-percent", "1.5")]
    [InlineData("--copies-per-text", "0")]
    [InlineData("--copies-per-text", "1")]
    [InlineData("--copies-per-text", "2147483648")]
    [InlineData("--text-pool-size", "10000")]
    public void RejectsInvalidRepetitionArguments(string name, string value)
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("output.txt");
        Assert.Throws<CliException>(() => GenerateOptions.Parse(["--output", path, "--size", "1", name, value]));
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// Even large file targets accept one-character texts without requiring unique identifiers.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("2147483647")]
    public void AcceptsShortTextsForAnyFileSize(string size)
    {
        using var directory = new TestDirectory();
        var options = GenerateOptions.Parse(
            ["--output", directory.FilePath("output.txt"), "--size", size, "--min-text-length", "1", "--max-text-length", "1"]);
        Assert.Equal(1, options.MinTextLength);
        Assert.Equal(1, options.MaxTextLength);
        Assert.Empty(Directory.GetFiles(directory.Root));
    }

    /// <summary>
    /// Large file and copy counts are accepted without materializing their texts.
    /// </summary>
    [Fact]
    public void SupportsLargeFilesWithoutAllocatingTheirTexts()
    {
        using var directory = new TestDirectory();
        var options = GenerateOptions.Parse(["--output", directory.FilePath("output.txt"), "--size", "2147483647",
            "--repeated-percent", "100", "--copies-per-text", "2147483647"]);
        Assert.Equal(int.MaxValue, options.CopiesPerText);
        Assert.Empty(Directory.GetFiles(directory.Root));
    }
}
