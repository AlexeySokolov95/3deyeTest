using LargeFileTools.Shared;
using LargeFileTools.Sorting;

namespace LargeFileTools.Tests.Sorting;

/// <summary>
/// Checks sorter limits supplied through the command line.
/// </summary>
public sealed class SortOptionsTests
{
    /// <summary>
    /// An omitted line limit uses the documented default.
    /// </summary>
    [Fact]
    public void DefaultsToOneHundredThousandLines()
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        File.WriteAllText(input, "1. A\n");

        var options = SortOptions.Parse(["--input", input, "--output", directory.FilePath("output.txt")]);

        Assert.Equal(100_000, options.ChunkLines);
        Assert.Equal(32, options.MaxMergeFiles);
    }

    /// <summary>
    /// Explicit line and merge limits are used as supplied.
    /// </summary>
    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("25000", 25_000)]
    public void ReadsChunkLineLimit(string argument, int expected)
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        File.WriteAllText(input, "1. A\n");

        var options = SortOptions.Parse(["--input", input, "--output", directory.FilePath("output.txt"),
            "--chunk-lines", argument, "--max-merge-files", "17"]);

        Assert.Equal(expected, options.ChunkLines);
        Assert.Equal(17, options.MaxMergeFiles);
    }

    /// <summary>
    /// Line counts must be positive integers within the array limit.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1MB")]
    [InlineData("2147483647")]
    [InlineData("2147483648")]
    public void RejectsInvalidChunkLineLimit(string argument)
    {
        var error = Assert.Throws<CliException>(() => SortOptions.Parse(
            ["--input", "input.txt", "--output", "output.txt", "--chunk-lines", argument]));

        Assert.Contains("--chunk-lines", error.Message);
    }

    /// <summary>
    /// The removed memory option cannot silently affect sorting.
    /// </summary>
    [Fact]
    public void RejectsMemoryOption()
    {
        var error = Assert.Throws<CliException>(() => SortOptions.Parse(
            ["--input", "input.txt", "--output", "output.txt", "--memory", "512"]));

        Assert.Contains("Unknown option", error.Message);
        Assert.Contains("--memory", error.Message);
    }
}
