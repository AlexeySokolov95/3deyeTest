namespace LargeFileTools.Generation;

/// <summary>
/// Measurements from a completed generation.
/// </summary>
/// <param name="Bytes">
/// Actual output bytes.
/// </param>
/// <param name="Records">
/// Written records.
/// </param>
/// <param name="Elapsed">
/// Total operation time.
/// </param>
public sealed record GenerationResult(long Bytes, long Records, TimeSpan Elapsed);
