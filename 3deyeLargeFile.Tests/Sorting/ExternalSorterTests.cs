using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using LargeFileTools.Shared;
using LargeFileTools.Sorting;

namespace LargeFileTools.Tests.Sorting;

/// <summary>
/// Checks the complete external sort against independent expectations.
/// </summary>
public sealed class ExternalSorterTests
{
    /// <summary>
    /// Reproduces the assignment's expected output.
    /// </summary>
    [Fact]
    public async Task SortsAssignmentExample()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllText(options.InputPath, "415. Apple\n30432. Something something something\n1. Apple\n32. Cherry is the best\n2. Banana is yellow\n");
        var result = await new ExternalSorter().SortAsync(options);
        Assert.Equal("1. Apple\n415. Apple\n2. Banana is yellow\n32. Cherry is the best\n30432. Something something something\n", File.ReadAllText(options.OutputPath));
        Assert.Equal(5, result.Records);
        Assert.Equal(1, result.InitialChunks);
        Assert.Equal(0, result.MergePasses);
        directory.AssertClean();
    }

    /// <summary>
    /// Multiple passes preserve ordering and every duplicate for varying workers.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task MatchesIndependentSortAcrossMultiplePasses(int workers)
    {
        using var directory = new TestDirectory();
        var options = Options(directory) with { Workers = workers };
        var random = new Random(7331);
        var vocabulary = new[] { "Apple", "apple", "Z", "Яблоко", "é", "e\u0301", "😀", "\uE000", "", " text ", "a. b" };
        var lines = Enumerable.Range(0, 25_000)
            .Select(_ => string.Create(CultureInfo.InvariantCulture, $"{random.NextInt64(1, 1001)}. {vocabulary[random.Next(vocabulary.Length)]}"))
            .ToArray();
        File.WriteAllLines(options.InputPath, lines, RecordFormat.Encoding);
        var expected = lines.Select(line => (Line: line, Parts: line.Split(". ", 2, StringSplitOptions.None)))
            .OrderBy(item => item.Parts[1], StringComparer.InvariantCulture)
            .ThenBy(item => long.Parse(item.Parts[0], CultureInfo.InvariantCulture))
            .Select(item => item.Line).ToArray();

        var result = await new ExternalSorter().SortAsync(options);
        Assert.True(result.InitialChunks > options.MaxMergeFiles);
        Assert.True(result.MergePasses >= 2);
        var actual = File.ReadAllLines(options.OutputPath);
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        for (var i = 0; i < expected.Length; i++)
        {
            var expectedParts = expected[i].Split(". ", 2, StringSplitOptions.None);
            var actualParts = actual[i].Split(". ", 2, StringSplitOptions.None);
            Assert.Equal(0, StringComparer.InvariantCulture.Compare(expectedParts[1], actualParts[1]));
            Assert.Equal(expectedParts[0], actualParts[0]);
        }
        Assert.Equal(lines.LongLength, result.Records);
        Assert.Equal(0, result.SkippedRecords);
        Assert.Equal(new FileInfo(options.OutputPath).Length, result.OutputBytes);
        Assert.Equal(lines, File.ReadAllLines(options.InputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// Preserves spelling and multiplicity despite unspecified equal-key order.
    /// </summary>
    [Fact]
    public async Task PreservesSpellingsAndNormalizesTerminators()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllText(options.InputPath, "\uFEFF001. x\r\n2.  x \n1. x\r\n001. x", new UTF8Encoding(false));
        await new ExternalSorter().SortAsync(options);
        var result = File.ReadAllLines(options.OutputPath);
        Assert.Equal("2.  x ", result[0]);
        Assert.Equal(["001. x", "001. x", "1. x"], result[1..].Order(StringComparer.Ordinal));
        var bytes = File.ReadAllBytes(options.OutputPath);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    /// <summary>
    /// An empty source publishes an empty file without merge readers.
    /// </summary>
    [Fact]
    public async Task HandlesEmptyFile()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllBytes(options.InputPath, []);
        var result = await new ExternalSorter().SortAsync(options);
        Assert.Equal(0, result.Records);
        Assert.Equal(0, result.InitialChunks);
        Assert.Equal(0, result.MergePasses);
        Assert.Empty(File.ReadAllBytes(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// A late invalid record is logged while valid records finish sorting.
    /// </summary>
    [Fact]
    public async Task SkipsLateInvalidRecord()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllLines(options.InputPath, Enumerable.Repeat("1. valid", 30_000).Append("bad").Append("2. valid"));
        var messages = new List<string>();
        var result = await new ExternalSorter().SortAsync(options, report: messages.Add)
            .WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(30_001, result.Records);
        Assert.Equal(1, result.SkippedRecords);
        Assert.True(result.MergePasses >= 2);
        Assert.Contains(messages, message => message.Contains($"{options.InputPath}: record 30001: expected"));
        Assert.Equal(Enumerable.Repeat("1. valid", 30_000).Append("2. valid"), File.ReadAllLines(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// An entirely invalid source produces an empty output with an accurate skip count.
    /// </summary>
    [Fact]
    public async Task HandlesOnlyInvalidRecords()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllText(options.InputPath, "bad\n0. zero\n-1. negative\n\n1.5. fraction");
        var messages = new List<string>();
        var result = await new ExternalSorter().SortAsync(options, report: messages.Add);
        Assert.Equal(0, result.Records);
        Assert.Equal(0, result.InitialChunks);
        Assert.Equal(5, result.SkippedRecords);
        Assert.Equal(5, messages.Count(message => message.StartsWith("Skipped ", StringComparison.Ordinal)));
        Assert.Empty(File.ReadAllBytes(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// Cancellation after chunk creation removes chunks without publishing output.
    /// </summary>
    [Fact]
    public async Task CancelsBetweenPhases()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllLines(options.InputPath, Enumerable.Repeat("1. valid", 10_000));
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new ExternalSorter().SortAsync(options, stop.Token,
            message =>
            {
                if (message.StartsWith("Created ", StringComparison.Ordinal))
                    stop.Cancel();
            }));
        Assert.False(File.Exists(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// A worker write failure unblocks the producer and completes every task.
    /// </summary>
    [Fact]
    public async Task WorkerFailureDoesNotDeadlock()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllLines(options.InputPath, Enumerable.Repeat("1. valid", 40_000));
        using var workspace = new TempWorkspace(directory.Root);
        File.WriteAllText(workspace.ChunkPath(0, 0), "collision");
        using var builder = new SortedChunkBuilder(options, workspace, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => builder.BuildAsync().WaitAsync(TimeSpan.FromSeconds(15)));
    }

    /// <summary>
    /// Missing records in a temporary chunk prevent publication and trigger cleanup.
    /// </summary>
    [Fact]
    public async Task RejectsRecordLossDuringMerging()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllLines(options.InputPath, Enumerable.Repeat("1. valid", 25_000));
        var error = await Assert.ThrowsAsync<IOException>(() => new ExternalSorter().SortAsync(options,
            report: message =>
            {
                if (message.StartsWith("Created ", StringComparison.Ordinal))
                {
                    var workspace = Assert.Single(Directory.GetDirectories(directory.Root, "3deye-sort-*"));
                    var chunks = Directory.GetFiles(workspace, "*.txt");
                    Assert.True(chunks.Length > options.MaxMergeFiles);
                    File.WriteAllText(chunks[0], string.Empty);
                }
            }));
        Assert.Equal("Record count changed during merging.", error.Message);
        Assert.False(File.Exists(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// The destination cannot target the source itself.
    /// </summary>
    [Fact]
    public async Task RejectsIdenticalInputAndOutput()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllText(options.InputPath, "1. keep\n");
        await Assert.ThrowsAsync<CliException>(() => new ExternalSorter().SortAsync(
            options with { OutputPath = options.InputPath }));
        Assert.Equal("1. keep\n", File.ReadAllText(options.InputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// Windows device paths cannot disguise the source as another destination.
    /// </summary>
    [Theory]
    [InlineData(@"\\?\", false)]
    [InlineData(@"\\?\", true)]
    [InlineData(@"\\.\", false)]
    [InlineData(@"\\.\", true)]
    public async Task RejectsWindowsAliases(string prefix, bool aliasInput)
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var directory = new TestDirectory();
        var options = Options(directory);
        const string original = "2. Z\n1. A\n";
        File.WriteAllText(options.InputPath, original);
        var alias = prefix + options.InputPath;
        Assert.True(File.Exists(alias));

        await Assert.ThrowsAsync<CliException>(() => new ExternalSorter().SortAsync(options with
        {
            InputPath = aliasInput ? alias : options.InputPath,
            OutputPath = aliasInput ? options.InputPath : alias
        }));

        Assert.Equal(original, File.ReadAllText(options.InputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// A Windows hard link still designates the same input file.
    /// </summary>
    [Fact]
    public async Task RejectsWindowsHardLink()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var directory = new TestDirectory();
        var options = Options(directory);
        const string original = "2. Z\n1. A\n";
        File.WriteAllText(options.InputPath, original);
        if (!CreateHardLink(options.OutputPath, options.InputPath, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        await Assert.ThrowsAsync<CliException>(() => new ExternalSorter().SortAsync(options));

        Assert.Equal(original, File.ReadAllText(options.InputPath));
        Assert.Equal(original, File.ReadAllText(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// Identity checks distinguish separate files with equal contents.
    /// </summary>
    [Fact]
    public void DistinguishesFilesWithIdenticalContents()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        const string original = "2. Z\n1. A\n";
        File.WriteAllText(options.InputPath, original);
        File.WriteAllText(options.OutputPath, original);

        FilePaths.RequireDifferent(options.InputPath, options.OutputPath);

        Assert.Equal(original, File.ReadAllText(options.InputPath));
        Assert.Equal(original, File.ReadAllText(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// An existing output causes an error before sorting starts.
    /// </summary>
    [Fact]
    public async Task RejectsExistingDestination()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllText(options.InputPath, "1. a\n");
        File.WriteAllText(options.OutputPath, "keep");
        var error = await Assert.ThrowsAsync<CliException>(() => new ExternalSorter().SortAsync(options));
        Assert.Contains("Output file already exists", error.Message);
        Assert.Equal("keep", File.ReadAllText(options.OutputPath));
        Assert.Equal("1. a\n", File.ReadAllText(options.InputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// Changing the line limit changes chunk boundaries without changing the result.
    /// </summary>
    [Theory]
    [InlineData(1, 7)]
    [InlineData(2, 4)]
    [InlineData(3, 3)]
    [InlineData(7, 1)]
    [InlineData(8, 1)]
    public async Task UsesConfiguredChunkLines(int chunkLines, int expectedChunks)
    {
        using var directory = new TestDirectory();
        var options = Options(directory) with { ChunkLines = chunkLines };
        var expected = Enumerable.Range(1, 7).Select(i => $"{i}. same").ToArray();
        File.WriteAllLines(options.InputPath, expected.Reverse());

        var result = await new ExternalSorter().SortAsync(options);

        Assert.Equal(expectedChunks, result.InitialChunks);
        Assert.Equal(expected.Length, result.Records);
        Assert.Equal(expected, File.ReadAllLines(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// Long common prefixes exercise byte buffers and numeric ties across passes.
    /// </summary>
    [Fact]
    public async Task SortsLongRecordsAcrossPasses()
    {
        using var directory = new TestDirectory();
        var options = Options(directory) with { ChunkLines = 10, MaxLineBytes = 9000 };
        var text = new string('x', 8192);
        var expected = Enumerable.Range(1, 80).Select(i => $"{i}. {text}").ToArray();
        File.WriteAllLines(options.InputPath, expected.Reverse());
        var result = await new ExternalSorter().SortAsync(options);
        Assert.True(result.MergePasses >= 2);
        Assert.Equal(expected, File.ReadAllLines(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// A publication race leaves the concurrent destination untouched.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleansStagingAfterPublicationFailure(bool createDirectory)
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllText(options.InputPath, "1. a\n");
        await Assert.ThrowsAnyAsync<IOException>(() => new ExternalSorter().SortAsync(options, report: message =>
        {
            if (message.StartsWith("Created ", StringComparison.Ordinal))
            {
                if (createDirectory)
                    Directory.CreateDirectory(options.OutputPath);
                else
                    File.WriteAllText(options.OutputPath, "keep");
            }
        }));
        if (createDirectory)
            Assert.True(Directory.Exists(options.OutputPath));
        else
            Assert.Equal("keep", File.ReadAllText(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// Small portions and two-way merging force the external-sort path.
    /// </summary>
    private static SortOptions Options(TestDirectory directory) => new()
    {
        InputPath = directory.FilePath("input.txt"),
        OutputPath = directory.FilePath("output.txt"),
        TempDirectory = directory.Root,
        ChunkLines = 1000,
        MaxLineBytes = 256,
        MaxMergeFiles = 2,
        Workers = 2
    };

    /// <summary>
    /// Creates a second directory entry for the Windows identity regression.
    /// </summary>
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", ExactSpelling = true,
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string linkPath, string existingPath, IntPtr securityAttributes);
}
