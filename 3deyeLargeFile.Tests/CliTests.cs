using System.Text.RegularExpressions;
using LargeFileTools.Shared;
using GeneratorProgram = LargeFileTools.Generation.Program;
using SorterProgram = LargeFileTools.Sorting.Program;
using VerifierProgram = LargeFileTools.Verification.Program;

namespace LargeFileTools.Tests;

/// <summary>
/// Serializes tests that temporarily capture the process console.
/// </summary>
[CollectionDefinition("CLI", DisableParallelization = true)]
public sealed class CliCollection;

/// <summary>
/// Exercises the real entry points and their observable exit codes.
/// </summary>
[Collection("CLI")]
public sealed class CliTests
{
    /// <summary>
    /// Both applications run end to end through their command-line parsers.
    /// </summary>
    [Fact]
    public async Task GeneratesAndSortsThroughCli()
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        var output = directory.FilePath("output.txt");
        var generated = await CaptureAsync(() => GeneratorProgram.Main(
            ["--output", input, "--size", "1", "--repeated-percent", "80", "--copies-per-text", "20"]));
        Assert.Equal(0, generated.Code);
        Assert.Contains("Generated", generated.Output);
        Assert.InRange(new FileInfo(input).Length, 1_048_576, 1_048_855);
        var sorted = await CaptureAsync(() => SorterProgram.Main(
            ["--input", input, "--output", output, "--chunk-lines", "1000", "--max-line-size", "1",
                "--max-merge-files", "2", "--temp-dir", directory.Root]));
        Assert.Equal(0, sorted.Code);
        Assert.Contains("Sorted", sorted.Output);
        Assert.Equal(File.ReadAllLines(input).Order(StringComparer.Ordinal), File.ReadAllLines(output).Order(StringComparer.Ordinal));
        directory.AssertClean();
    }

    /// <summary>
    /// Help succeeds without starting either operation.
    /// </summary>
    [Fact]
    public async Task DisplaysHelp()
    {
        var generator = await CaptureAsync(() => GeneratorProgram.Main(["--help"]));
        var sorter = await CaptureAsync(() => SorterProgram.Main(["--help"]));
        Assert.Equal(0, generator.Code);
        Assert.Equal(0, sorter.Code);
        Assert.Contains("--size", generator.Output);
        Assert.Contains("random if omitted; printed on completion", generator.Output);
        Assert.Contains("--repeated-percent", generator.Output);
        Assert.Contains("--copies-per-text", generator.Output);
        Assert.Contains("Minimum share of records", generator.Output);
        Assert.Contains("Random text collisions are allowed", generator.Output);
        Assert.DoesNotContain("text id", generator.Output);
        Assert.Contains("final group may be incomplete", generator.Output);
        Assert.Contains("at least one complete repeated group", generator.Output);
        Assert.Contains("group larger than the target takes priority", generator.Output);
        Assert.Contains("--shuffle-buffer-size <MB>", generator.Output);
        Assert.Contains("shuffling (8 MB)", generator.Output);
        Assert.Contains("shuffled within each buffer", generator.Output);
        Assert.DoesNotContain("--text-pool-size", generator.Output);
        Assert.Contains("--chunk-lines <int>", sorter.Output);
        Assert.Contains("Maximum lines per portion (100000)", sorter.Output);
        Assert.DoesNotContain("--memory", sorter.Output);
        Assert.Contains("--max-merge-files <int>", sorter.Output);
        Assert.Contains("Maximum files merged at once (32)", sorter.Output);
        Assert.Contains("InvariantCulture", sorter.Output);
        Assert.Contains("positive Int64", sorter.Output);
        Assert.Contains("skipped and logged to stderr", sorter.Output);
    }

    /// <summary>
    /// The CLI applies the requested line limit through chunk creation and merging.
    /// </summary>
    [Fact]
    public async Task AppliesChunkLineLimit()
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        var output = directory.FilePath("output.txt");
        File.WriteAllText(input, "5. A\n4. A\n3. A\n2. A\n1. A\n");

        var result = await CaptureAsync(() => SorterProgram.Main(
            ["--input", input, "--output", output, "--chunk-lines", "2",
                "--max-merge-files", "2", "--temp-dir", directory.Root]));

        Assert.Equal(0, result.Code);
        Assert.Contains("3 chunks, 2 merge passes", result.Output);
        Assert.Equal("1. A\n2. A\n3. A\n4. A\n5. A\n", File.ReadAllText(output));
        directory.AssertClean();
    }

    /// <summary>
    /// The CLI default equals eight MB; a smaller buffer changes only record order.
    /// </summary>
    [Fact]
    public async Task AppliesConfiguredShuffleBuffer()
    {
        using var directory = new TestDirectory();
        var defaultPath = directory.FilePath("default.txt");
        var explicitPath = directory.FilePath("eight.txt");
        var smallerPath = directory.FilePath("one.txt");
        var defaultResult = await CaptureAsync(() => GeneratorProgram.Main(["--output", defaultPath, "--size", "1", "--seed", "42"]));
        var explicitResult = await CaptureAsync(() => GeneratorProgram.Main(
            ["--output", explicitPath, "--size", "1", "--shuffle-buffer-size", "8", "--seed", "42"]));
        var smallerResult = await CaptureAsync(() => GeneratorProgram.Main(
            ["--output", smallerPath, "--size", "1", "--shuffle-buffer-size", "1", "--seed", "42"]));
        Assert.Equal(0, defaultResult.Code);
        Assert.Equal(0, explicitResult.Code);
        Assert.Equal(0, smallerResult.Code);
        Assert.Equal(File.ReadAllBytes(defaultPath), File.ReadAllBytes(explicitPath));
        var original = File.ReadAllLines(defaultPath);
        var smaller = File.ReadAllLines(smallerPath);
        Assert.False(original.SequenceEqual(smaller));
        Assert.Equal(original.Order(StringComparer.Ordinal), smaller.Order(StringComparer.Ordinal));
        directory.AssertClean();
    }

    /// <summary>
    /// The reported seed reproduces identical bytes for random and explicit seeds.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-42")]
    public async Task ReproducesFileUsingReportedSeed(string? suppliedSeed)
    {
        using var directory = new TestDirectory();
        var original = directory.FilePath("original.txt");
        var replay = directory.FilePath("replay.txt");
        var args = new List<string> { "--output", original, "--size", "1" };
        if (suppliedSeed is not null)
            args.AddRange(["--seed", suppliedSeed]);
        var first = await CaptureAsync(() => GeneratorProgram.Main(args.ToArray()));
        Assert.Equal(0, first.Code);
        var match = Regex.Match(first.Output, @"Seed: (-?\d+)\.");
        Assert.True(match.Success, first.Output);
        var reportedSeed = match.Groups[1].Value;
        if (suppliedSeed is not null)
            Assert.Equal(suppliedSeed, reportedSeed);

        var second = await CaptureAsync(() => GeneratorProgram.Main(
            ["--output", replay, "--size", "1", "--seed", reportedSeed]));
        Assert.Equal(0, second.Code);
        Assert.Contains($"Seed: {reportedSeed}.", second.Output);
        Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(replay));
        directory.AssertClean();
    }

    /// <summary>
    /// The minimum CLI target can grow to accommodate its required repeated group.
    /// </summary>
    [Fact]
    public async Task GeneratesGroupLargerThanRequestedSize()
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("output.txt");
        var result = await CaptureAsync(() => GeneratorProgram.Main(
            ["--output", path, "--size", "1", "--repeated-percent", "1", "--copies-per-text", "2",
                "--min-text-length", "600000", "--max-text-length", "600000"]));
        Assert.Equal(0, result.Code);
        Assert.Contains("Generated 2 records", result.Output);
        var records = File.ReadAllLines(path).Select(line => RecordFormat.Parse(line, "generated", 1)).ToArray();
        Assert.Equal(2, records.Length);
        Assert.Equal(records[0].Text.ToString(), records[1].Text.ToString());
        Assert.True(new FileInfo(path).Length > 1_048_576);
        directory.AssertClean();
    }

    /// <summary>
    /// Invalid arguments have exit code two and a diagnostic.
    /// </summary>
    [Fact]
    public async Task ReturnsArgumentExitCode()
    {
        var result = await CaptureAsync(() => SorterProgram.Main(["--unknown"]));
        Assert.Equal(2, result.Code);
        Assert.Contains("Unknown option", result.Error);
        Assert.Empty(result.Output);
    }

    /// <summary>
    /// Invalid input is logged to stderr and omitted from successful output.
    /// </summary>
    [Fact]
    public async Task SkipsInvalidRecordsAndReportsCount()
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        var output = directory.FilePath("output.txt");
        File.WriteAllText(input, "2. Z\nbad\n0. zero\n-1. negative\n1. a\n3. A\n");
        var result = await CaptureAsync(() => SorterProgram.Main(
            ["--input", input, "--output", output, "--temp-dir", directory.Root]));
        Assert.Equal(0, result.Code);
        Assert.Contains($"{input}: record 2: expected", result.Error);
        Assert.Contains($"{input}: record 3: the number", result.Error);
        Assert.Contains($"{input}: record 4: the number", result.Error);
        Assert.Contains("Sorted 3 records; skipped 3;", result.Output);
        Assert.Equal("1. a\n3. A\n2. Z\n", File.ReadAllText(output));
        directory.AssertClean();
    }

    /// <summary>
    /// Both commands reject an existing output and leave its contents intact.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsExistingOutput(bool generator)
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        var output = directory.FilePath("output.txt");
        File.WriteAllText(input, "1. valid\n");
        File.WriteAllText(output, "keep");

        var result = await CaptureAsync(() => generator
            ? GeneratorProgram.Main(["--output", output, "--size", "1"])
            : SorterProgram.Main(["--input", input, "--output", output, "--temp-dir", directory.Root]));

        Assert.Equal(2, result.Code);
        Assert.Contains("Output file already exists", result.Error);
        Assert.Contains(output, result.Error);
        Assert.Empty(result.Output);
        Assert.Equal("keep", File.ReadAllText(output));
        Assert.Equal("1. valid\n", File.ReadAllText(input));
        directory.AssertClean();
    }

    /// <summary>
    /// Malformed output paths are reported as argument errors.
    /// </summary>
    [Fact]
    public async Task RejectsMalformedOutputPath()
    {
        var result = await CaptureAsync(() => GeneratorProgram.Main(["--output", "bad\0path", "--size", "1"]));
        Assert.Equal(2, result.Code);
    }

    /// <summary>
    /// The independent verifier accepts reordered records with preserved content.
    /// </summary>
    [Fact]
    public async Task VerifiesPreservedRecords()
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        var output = directory.FilePath("output.txt");
        File.WriteAllText(input, "2. x\r\n1. A\n2. x");
        File.WriteAllText(output, "1. A\n2. x\n2. x\n");
        var result = await CaptureAsync(() => VerifierProgram.Main([input, output]));
        Assert.Equal(0, result.Code);
        Assert.Contains("Verified 3 records", result.Output);
    }

    /// <summary>
    /// The verifier rejects order errors, lost duplicates, and altered multiplicity.
    /// </summary>
    [Theory]
    [InlineData("2. x\n1. A\n2. x\n")]
    [InlineData("1. A\n2. x\n")]
    [InlineData("1. A\n1. A\n2. x\n")]
    public async Task VerifierRejectsCorruption(string changed)
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        var output = directory.FilePath("output.txt");
        File.WriteAllText(input, "2. x\n1. A\n2. x\n");
        File.WriteAllText(output, changed);
        var result = await CaptureAsync(() => VerifierProgram.Main([input, output]));
        Assert.Equal(1, result.Code);
        Assert.NotEmpty(result.Error);
    }

    /// <summary>
    /// Both files open before parsing, and open failures release the first handle.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifierReportsMissingFileBeforeScanning(bool missingSource)
    {
        using var directory = new TestDirectory();
        var existing = directory.FilePath("existing.txt");
        var missing = directory.FilePath("missing.txt");
        File.WriteAllText(existing, "invalid record\n");

        var result = await CaptureAsync(() => VerifierProgram.Main(
            missingSource ? [missing, existing] : [existing, missing]));

        Assert.Equal(1, result.Code);
        Assert.Contains("missing.txt", result.Error);
        Assert.Empty(result.Output);
        using var exclusive = new FileStream(existing, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanRead);
    }

    /// <summary>
    /// Captures one command while restoring both console streams.
    /// </summary>
    private static async Task<(int Code, string Output, string Error)> CaptureAsync(Func<Task<int>> command)
    {
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var code = await command();
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }
}
