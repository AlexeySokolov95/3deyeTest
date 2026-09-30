using LargeFileTools.Shared;
using LargeFileTools.Sorting;

namespace LargeFileTools.Tests.Sorting;

/// <summary>
/// Checks merge boundaries and handle cleanup on failures.
/// </summary>
public sealed class ChunkGroupMergerTests
{
    /// <summary>
    /// Merges overlapping chunks and preserves duplicates from each reader.
    /// </summary>
    [Fact]
    public void MergesEmptyAndOverlappingChunks()
    {
        using var directory = new TestDirectory();
        var first = directory.FilePath("first.txt");
        var second = directory.FilePath("second.txt");
        var empty = directory.FilePath("empty.txt");
        var output = directory.FilePath("output.txt");
        File.WriteAllText(first, "1. A\n2. A\n1. Z\n");
        File.WriteAllText(second, "9. a\n2. A\n3. A\n");
        File.WriteAllBytes(empty, []);
        var count = new ChunkGroupMerger().Merge([first, empty, second], output, 128, CancellationToken.None);
        Assert.Equal(6, count);
        Assert.Equal("9. a\n1. A\n2. A\n2. A\n3. A\n1. Z\n", File.ReadAllText(output));
    }

    /// <summary>
    /// Closes previously opened readers when another input cannot be opened.
    /// </summary>
    [Fact]
    public void ReleasesReadersAfterOpenFailure()
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        File.WriteAllText(input, "1. A\n");
        Assert.Throws<FileNotFoundException>(() => new ChunkGroupMerger().Merge(
            [input, directory.FilePath("missing.txt")], directory.FilePath("output.txt"), 128, CancellationToken.None));
        using var exclusive = new FileStream(input, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanWrite);
    }

    /// <summary>
    /// A parse failure releases both input and partially written output handles.
    /// </summary>
    [Fact]
    public void ReleasesHandlesAfterLateParseFailure()
    {
        using var directory = new TestDirectory();
        var input = directory.FilePath("input.txt");
        var output = directory.FilePath("output.txt");
        File.WriteAllText(input, "1. A\nbad\n");
        Assert.Throws<RecordDataException>(() => new ChunkGroupMerger().Merge([input], output, 128, CancellationToken.None));
        using var inputLease = new FileStream(input, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var outputLease = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(inputLease.CanWrite && outputLease.CanWrite);
    }
}
