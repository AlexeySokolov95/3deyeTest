namespace LargeFileTools.Sorting;

/// <summary>
/// Counts from a completed chunk merge.
/// </summary>
/// <param name="RecordCount">
/// Preserved record count.
/// </param>
/// <param name="MergePasses">
/// Merge levels, excluding a single-chunk copy.
/// </param>
public sealed record MergeResult(long RecordCount, int MergePasses);
