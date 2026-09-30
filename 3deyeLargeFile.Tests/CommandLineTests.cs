using LargeFileTools.Shared;
using LargeFileTools.Sorting;

namespace LargeFileTools.Tests;

/// <summary>
/// Validates sizes and rejects ambiguous command lines.
/// </summary>
public sealed class CommandLineTests
{
    /// <summary>
    /// Converts whole megabytes without overflowing intermediate integers.
    /// </summary>
    [Theory]
    [InlineData("1", 1_048_576L)]
    [InlineData("512", 536_870_912L)]
    [InlineData("102400", 107_374_182_400L)]
    [InlineData("2147483647", 2_251_799_812_636_672L)]
    public void ReadsMegabytes(string text, long expected) =>
        Assert.Equal(expected, new CliArguments(["--size", text], "--size").Megabytes("--size"));

    /// <summary>
    /// Rejects nonpositive values, fractions, suffixes, and integer overflow.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1MB")]
    [InlineData("1KiB")]
    [InlineData("1B")]
    [InlineData("2147483648")]
    [InlineData("")]
    public void RejectsInvalidMegabytes(string text) => Assert.Throws<CliException>(() =>
        new CliArguments(["--size", text], "--size").Megabytes("--size"));

    /// <summary>
    /// Optional sizes use megabyte defaults; required sizes cannot be omitted.
    /// </summary>
    [Fact]
    public void HandlesMissingMegabytes()
    {
        var cli = new CliArguments([]);
        Assert.Equal(8_388_608L, cli.Megabytes("--shuffle-buffer-size", 8));
        Assert.Contains("Missing required option", Assert.Throws<CliException>(() => cli.Megabytes("--size")).Message);
    }

    /// <summary>
    /// The line limit must fit the reader's integer byte capacity.
    /// </summary>
    [Fact]
    public void RejectsUnrepresentableLineLimit()
    {
        var error = Assert.Throws<CliException>(() => SortOptions.Parse(
            ["--input", "input.txt", "--output", "output.txt", "--max-line-size", "2048"]));
        Assert.Contains("--max-line-size must not exceed 2047 MB", error.Message);
    }

    /// <summary>
    /// Rejects repeated, unknown, and valueless options.
    /// </summary>
    [Fact]
    public void RejectsInvalidOptions()
    {
        Assert.Throws<CliException>(() => new CliArguments(["--unknown"]));
        Assert.Throws<CliException>(() => new CliArguments(["--size"], "--size"));
        Assert.Throws<CliException>(() => new CliArguments(["--size", "1", "--size", "2"], "--size"));
    }
}
