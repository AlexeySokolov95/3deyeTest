using LargeFileTools.Sorting;

namespace LargeFileTools.Tests.Sorting;

/// <summary>
/// Checks merge results, consumed-file cleanup, and peak disk accounting.
/// </summary>
public sealed class SortedChunkMergerTests
{
    /// <summary>
    /// Empty, single, and multiple chunks finish without exposing merge state.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(5, 3)]
    public async Task MergesAndRemovesConsumedChunks(int chunkCount, int expectedPasses)
    {
        using var directory = new TestDirectory();
        using var workspace = new TempWorkspace(directory.Root);
        var output = directory.FilePath("merged.txt");
        for (var i = 0; i < chunkCount; i++)
        {
            var path = workspace.ChunkPath(0, i);
            File.WriteAllText(path, $"{i + 1}. A\n{i + 1}. Z\n");
            workspace.TrackChunk(path);
        }

        var chunks = new ChunkBuildResult(chunkCount, 2L * chunkCount, 0);
        var merger = new SortedChunkMerger(workspace, maxMergeFiles: 2, maxLineBytes: 128);
        var result = await merger.MergeAsync(chunks, output, CancellationToken.None);

        var expected = Enumerable.Range(1, chunkCount).Select(i => $"{i}. A\n")
            .Concat(Enumerable.Range(1, chunkCount).Select(i => $"{i}. Z\n"));
        Assert.Equal(string.Concat(expected), File.ReadAllText(output));
        Assert.Equal(chunks.RecordCount, result.RecordCount);
        Assert.Equal(expectedPasses, result.MergePasses);
        Assert.Empty(Directory.GetFiles(workspace.RootPath));
        Assert.Equal(2 * new FileInfo(output).Length, workspace.PeakBytes);
    }
}
