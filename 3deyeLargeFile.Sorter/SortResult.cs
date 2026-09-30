namespace LargeFileTools.Sorting;

/// <summary>
/// Measurements from a successfully published sort.
/// </summary>
/// <param name="InputBytes">
/// Source file size.
/// </param>
/// <param name="OutputBytes">
/// Normalized output file size.
/// </param>
/// <param name="Records">
/// Preserved record count.
/// </param>
/// <param name="SkippedRecords">
/// Invalid source records omitted from the output.
/// </param>
/// <param name="InitialChunks">
/// Files produced by the sorting workers.
/// </param>
/// <param name="MergePasses">
/// Merge levels, excluding a single-chunk copy.
/// </param>
/// <param name="PeakTemporaryBytes">
/// Peak completed-chunk and staging disk usage.
/// </param>
/// <param name="Elapsed">
/// Total operation time.
/// </param>
public sealed record SortResult(long InputBytes, long OutputBytes, long Records, long SkippedRecords, long InitialChunks,
    int MergePasses, long PeakTemporaryBytes, TimeSpan Elapsed);
