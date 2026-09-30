namespace LargeFileTools.Sorting;

/// <summary>
/// Counts from the completed chunk-building phase.
/// </summary>
/// <param name="ChunkCount">
/// Number of contiguous chunk identifiers.
/// </param>
/// <param name="RecordCount">
/// Number of valid source records.
/// </param>
/// <param name="SkippedRecords">
/// Number of invalid source records skipped.
/// </param>
public sealed record ChunkBuildResult(long ChunkCount, long RecordCount, long SkippedRecords);
