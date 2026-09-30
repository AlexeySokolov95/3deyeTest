using System.Text;
using LargeFileTools.Sorting;

namespace LargeFileTools.Tests.Sorting;

/// <summary>
/// Checks parallel parsing, diagnostics, and chunk numbering.
/// </summary>
public sealed class SortedChunkBuilderTests
{
    /// <summary>
    /// Invalid-only portions leave no holes between usable chunks.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task PreservesCountsAndNumbersAcrossMixedPortions(int workers)
    {
        using var directory = new TestDirectory();
        var options = Options(directory) with { Workers = workers };
        var bytes = Encoding.UTF8.GetBytes("\uFEFFbad\n0. zero\n2. Я\r\n1. A\n" + new string('x', 100) + "\r\nbad\n-1. x\n3. A\r\n")
            .Concat(new byte[] { 49, 46, 32, 0xFF, 10 })
            .Concat("4. z"u8.ToArray()).ToArray();
        File.WriteAllBytes(options.InputPath, bytes);
        var messages = new List<string>();
        using var workspace = new TempWorkspace(directory.Root);
        using var builder = new SortedChunkBuilder(options, workspace,
            CancellationToken.None, messages.Add);
        var result = await builder.BuildAsync().WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(3, result.ChunkCount);
        Assert.Equal(4, result.RecordCount);
        Assert.Equal(6, result.SkippedRecords);
        Assert.Equal(6, messages.Count);
        foreach (var number in new[] { 1, 2, 5, 6, 7, 9 })
            Assert.Single(messages, message => message.Contains($"{options.InputPath}: record {number}:"));
        var paths = Enumerable.Range(0, 3).Select(i => workspace.ChunkPath(0, i)).ToArray();
        Assert.Equal(3, Directory.GetFiles(workspace.RootPath).Length);
        var count = new ChunkGroupMerger().Merge(paths, options.OutputPath, options.MaxLineBytes, CancellationToken.None);
        Assert.Equal(4, count);
        Assert.Equal("1. A\n3. A\n4. z\n2. Я\n", File.ReadAllText(options.OutputPath));
    }

    /// <summary>
    /// Concurrent builders keep queues, counters, and diagnostics independent.
    /// </summary>
    [Fact]
    public async Task ConcurrentBuildersKeepStateIsolated()
    {
        using var firstDirectory = new TestDirectory();
        using var secondDirectory = new TestDirectory();
        var firstOptions = Options(firstDirectory);
        var secondOptions = Options(secondDirectory);
        File.WriteAllText(firstOptions.InputPath, "bad\n2. A\n1. A\n3. Z\n");
        File.WriteAllText(secondOptions.InputPath, "bad\n4. B\n2. B\n1. B\n3. B\n");
        using var firstWorkspace = new TempWorkspace(firstDirectory.Root);
        using var secondWorkspace = new TempWorkspace(secondDirectory.Root);
        using var overlap = new Barrier(2);
        var report = (string message) =>
        {
            if (message.StartsWith("Skipped ", StringComparison.Ordinal))
                Assert.True(overlap.SignalAndWait(TimeSpan.FromSeconds(10)));
        };
        using var firstBuilder = new SortedChunkBuilder(firstOptions, firstWorkspace, CancellationToken.None, report);
        using var secondBuilder = new SortedChunkBuilder(secondOptions, secondWorkspace, CancellationToken.None, report);

        var first = firstBuilder.BuildAsync();
        var second = secondBuilder.BuildAsync();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(new ChunkBuildResult(2, 3, 1), results[0]);
        Assert.Equal(new ChunkBuildResult(3, 4, 1), results[1]);
        Assert.Equal(2, Directory.GetFiles(firstWorkspace.RootPath).Length);
        Assert.Equal(3, Directory.GetFiles(secondWorkspace.RootPath).Length);
        var firstPaths = Enumerable.Range(0, 2).Select(i => firstWorkspace.ChunkPath(0, i)).ToArray();
        var secondPaths = Enumerable.Range(0, 3).Select(i => secondWorkspace.ChunkPath(0, i)).ToArray();
        var merger = new ChunkGroupMerger();
        merger.Merge(firstPaths, firstOptions.OutputPath, firstOptions.MaxLineBytes, CancellationToken.None);
        merger.Merge(secondPaths, secondOptions.OutputPath, secondOptions.MaxLineBytes, CancellationToken.None);
        Assert.Equal("1. A\n2. A\n3. Z\n", File.ReadAllText(firstOptions.OutputPath));
        Assert.Equal("1. B\n2. B\n3. B\n4. B\n", File.ReadAllText(secondOptions.OutputPath));
    }

    /// <summary>
    /// A failed builder cannot affect the state of a new operation.
    /// </summary>
    [Fact]
    public async Task NewBuilderCanBuildAfterFailure()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        var failure = new IOException("Injected diagnostic failure.");
        File.WriteAllText(options.InputPath, "bad\n");
        using (var failedWorkspace = new TempWorkspace(directory.Root))
        {
            using var failedBuilder = new SortedChunkBuilder(options, failedWorkspace,
                CancellationToken.None, _ => throw failure);
            var error = await Assert.ThrowsAsync<IOException>(() => failedBuilder.BuildAsync()
                .WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Same(failure, error);
        }

        File.WriteAllText(options.InputPath, "2. A\n1. A\n");
        using var workspace = new TempWorkspace(directory.Root);
        using var builder = new SortedChunkBuilder(options, workspace, CancellationToken.None);
        var result = await builder.BuildAsync().WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(new ChunkBuildResult(1, 2, 0), result);
        Assert.Single(Directory.GetFiles(workspace.RootPath));
        Assert.Equal("1. A\n2. A\n", File.ReadAllText(workspace.ChunkPath(0, 0)));
    }

    /// <summary>
    /// A completed or failed operation cannot be started again.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsRepeatedBuild(bool fail)
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllText(options.InputPath, fail ? "bad\n" : "1. A\n");
        using var workspace = new TempWorkspace(directory.Root);
        var failure = new IOException("Injected diagnostic failure.");
        using var builder = new SortedChunkBuilder(options, workspace,
            CancellationToken.None, _ => throw failure);

        if (fail)
        {
            var error = await Assert.ThrowsAsync<IOException>(() => builder.BuildAsync()
                .WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Same(failure, error);
        }
        else
        {
            var result = await builder.BuildAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(new ChunkBuildResult(1, 1, 0), result);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync()
            .WaitAsync(TimeSpan.FromSeconds(15)));
    }

    /// <summary>
    /// A worker-side reporting failure cancels the pipeline and preserves its cause.
    /// </summary>
    [Fact]
    public async Task ReportingFailureStopsEveryTask()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllLines(options.InputPath, Enumerable.Repeat("bad", 40_000));
        var failure = new IOException("Injected diagnostic failure.");
        var error = await Assert.ThrowsAsync<IOException>(() => new ExternalSorter()
            .SortAsync(options, report: message =>
            {
                if (message.StartsWith("Skipped ", StringComparison.Ordinal))
                    throw failure;
            }).WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Same(failure, error);
        Assert.False(File.Exists(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// A reader-side failure stops workers and preserves the original exception.
    /// </summary>
    [Fact]
    public async Task ReaderFailureStopsEveryTask()
    {
        using var directory = new TestDirectory();
        var options = Options(directory) with { ChunkLines = 1000 };
        File.WriteAllLines(options.InputPath,
            Enumerable.Repeat("1. valid", 40_000).Append(new string('x', options.MaxLineBytes + 1)));
        var failure = new IOException("Injected reader diagnostic failure.");
        var error = await Assert.ThrowsAsync<IOException>(() => new ExternalSorter()
            .SortAsync(options, report: message =>
            {
                if (message.StartsWith("Skipped ", StringComparison.Ordinal))
                    throw failure;
            }).WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Same(failure, error);
        Assert.False(File.Exists(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// Cancellation from worker diagnostics leaves no partial output or temporary files.
    /// </summary>
    [Fact]
    public async Task CancelsDuringWorkerParsing()
    {
        using var directory = new TestDirectory();
        var options = Options(directory);
        File.WriteAllLines(options.InputPath, Enumerable.Repeat("bad", 40_000));
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new ExternalSorter()
            .SortAsync(options, stop.Token, message =>
            {
                if (message.StartsWith("Skipped ", StringComparison.Ordinal))
                    stop.Cancel();
            }).WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.False(File.Exists(options.OutputPath));
        directory.AssertClean();
    }

    /// <summary>
    /// Small limits exercise boundaries without large fixtures.
    /// </summary>
    private static SortOptions Options(TestDirectory directory) => new()
    {
        InputPath = directory.FilePath("input.txt"),
        OutputPath = directory.FilePath("output.txt"),
        TempDirectory = directory.Root,
        ChunkLines = 2,
        MaxLineBytes = 16,
        MaxMergeFiles = 2,
        Workers = 2
    };
}
