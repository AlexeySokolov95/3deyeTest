using System.Diagnostics;
using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Merges sorted chunks, checks record counts, and removes consumed files.
/// </summary>
public sealed class SortedChunkMerger
{
    /// <summary>
    /// Caller-owned temporary storage and disk measurements.
    /// </summary>
    private readonly TempWorkspace workspace;

    /// <summary>
    /// Maximum input files per merge group.
    /// </summary>
    private readonly int maxMergeFiles;

    /// <summary>
    /// Maximum encoded record size.
    /// </summary>
    private readonly int maxLineBytes;

    /// <summary>
    /// Merges individual groups of sorted files.
    /// </summary>
    private readonly ChunkGroupMerger merger = new();

    /// <summary>
    /// Uses caller-owned storage and validated merge limits.
    /// </summary>
    public SortedChunkMerger(TempWorkspace workspace, int maxMergeFiles, int maxLineBytes)
    {
        this.workspace = workspace;
        this.maxMergeFiles = maxMergeFiles;
        this.maxLineBytes = maxLineBytes;
    }

    /// <summary>
    /// Writes the merged staging file and removes its consumed chunks.
    /// </summary>
    public async Task<MergeResult> MergeAsync(ChunkBuildResult chunks, string outputPath,
        CancellationToken cancellationToken, Action<string>? report = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentPass = 0;
        var remainingChunks = chunks.ChunkCount;
        while (remainingChunks > maxMergeFiles)
        {
            remainingChunks = MergeIntermediatePass(currentPass, remainingChunks, cancellationToken, report);

            currentPass++;
        }

        var mergePasses = currentPass;
        var recordCount = 0L;
        if (remainingChunks == 0)
        {
            await CreateEmptyOutputAsync(outputPath);
        }
        else if (remainingChunks == 1)
        {
            await CopySingleChunkAsync(currentPass, outputPath, cancellationToken);
            recordCount = chunks.RecordCount;
        }
        else
        {
            recordCount = MergeFinalChunks(currentPass, remainingChunks, outputPath, cancellationToken, report);
            mergePasses++;
        }

        EnsureRecordCount(chunks.RecordCount, recordCount);
        workspace.TrackStaging(outputPath);
        DeleteTemporaryChunks(currentPass, remainingChunks);
        return new MergeResult(recordCount, mergePasses);
    }

    /// <summary>
    /// Merges an intermediate level and returns the remaining chunk count.
    /// </summary>
    private long MergeIntermediatePass(int currentPass, long chunkCount,
        CancellationToken cancellationToken, Action<string>? report)
    {
        var nextCount = 0L;
        var timer = Stopwatch.StartNew();
        for (var first = 0L; first < chunkCount; first += maxMergeFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = GetChunkPaths(currentPass, first, chunkCount);
            var merged = workspace.ChunkPath(currentPass + 1, nextCount++);
            merger.Merge(group, merged, maxLineBytes, cancellationToken);
            workspace.TrackChunk(merged);
            foreach (var path in group)
                workspace.DeleteChunk(path);
            if (timer.Elapsed >= TimeSpan.FromSeconds(2))
            {
                report?.Invoke($"Merge pass {currentPass + 1}: consumed {Math.Min(first + maxMergeFiles, chunkCount):N0}/{chunkCount:N0} chunks.");
                timer.Restart();
            }
        }
        return nextCount;
    }

    /// <summary>
    /// Creates an empty staging file.
    /// </summary>
    private static async Task CreateEmptyOutputAsync(string outputPath)
    {
        await using var writer = TextFile.CreateWriter(outputPath);
    }

    /// <summary>
    /// Copies the sole remaining chunk with bounded buffering.
    /// </summary>
    private async Task CopySingleChunkAsync(int currentPass, string outputPath, CancellationToken cancellationToken)
    {
        var source = workspace.ChunkPath(currentPass, 0);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, RecordReader.BufferBytes, cancellationToken);
    }

    /// <summary>
    /// Merges the remaining chunks into the staging file and returns the record count.
    /// </summary>
    private long MergeFinalChunks(int currentPass, long chunkCount, string outputPath,
        CancellationToken cancellationToken, Action<string>? report)
    {
        report?.Invoke($"Final merge of {chunkCount:N0} chunks.");
        var chunkPaths = GetChunkPaths(currentPass, 0, chunkCount);
        return merger.Merge(chunkPaths, outputPath, maxLineBytes, cancellationToken);
    }

    /// <summary>
    /// Rejects a merged result with missing or extra records.
    /// </summary>
    private static void EnsureRecordCount(long expected, long actual)
    {
        if (actual != expected)
            throw new IOException("Record count changed during merging.");
    }

    /// <summary>
    /// Removes the chunks consumed by the final output operation.
    /// </summary>
    private void DeleteTemporaryChunks(int pass, long count)
    {
        for (var i = 0L; i < count; i++)
            workspace.DeleteChunk(workspace.ChunkPath(pass, i));
    }

    /// <summary>
    /// Materializes only the paths for the current merge group.
    /// </summary>
    private string[] GetChunkPaths(int pass, long first, long count)
    {
        var size = (int)Math.Min(maxMergeFiles, count - first);
        var paths = new string[size];
        for (var i = 0; i < size; i++)
            paths[i] = workspace.ChunkPath(pass, first + i);
        return paths;
    }
}
