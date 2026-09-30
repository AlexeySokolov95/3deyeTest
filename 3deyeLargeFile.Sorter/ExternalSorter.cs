using System.Diagnostics;
using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Coordinates chunk creation, merging, and output publication.
/// </summary>
public sealed class ExternalSorter
{
    /// <summary>
    /// Sorts a file from validated options using bounded portions.
    /// </summary>
    public async Task<SortResult> SortAsync(SortOptions options, CancellationToken cancellationToken = default,
        Action<string>? report = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        var inputBytes = new FileInfo(options.InputPath).Length;
        using var output = new OutputFile(options.OutputPath, report);
        using var workspace = new TempWorkspace(options.TempDirectory, report);
        report?.Invoke($"Building chunks of up to {options.ChunkLines:N0} lines with {options.Workers} worker(s); maximum files per merge {options.MaxMergeFiles}.");
        using var builder = new SortedChunkBuilder(options, workspace, cancellationToken, report);
        var chunks = await builder.BuildAsync();
        report?.Invoke($"Created {chunks.ChunkCount:N0} chunks from {chunks.RecordCount:N0} records.");
        var merger = new SortedChunkMerger(workspace, options.MaxMergeFiles, options.MaxLineBytes);
        var merged = await merger.MergeAsync(chunks, output.StagingPath, cancellationToken, report);

        var outputBytes = new FileInfo(output.StagingPath).Length;
        output.Publish(cancellationToken);

        return new SortResult(inputBytes, outputBytes, merged.RecordCount, chunks.SkippedRecords, chunks.ChunkCount,
            merged.MergePasses, workspace.PeakBytes, timer.Elapsed);
    }
}
