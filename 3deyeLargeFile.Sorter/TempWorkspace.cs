namespace LargeFileTools.Sorting;

/// <summary>
/// Owns one sort operation's temporary files and disk-usage measurements.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    /// <summary>
    /// Receives cleanup diagnostics.
    /// </summary>
    private readonly Action<string>? report;

    /// <summary>
    /// Total bytes in completed temporary files.
    /// </summary>
    private long currentBytes;

    /// <summary>
    /// Largest observed temporary and staging byte count.
    /// </summary>
    private long peakBytes;

    /// <summary>
    /// Creates an isolated directory under the supplied parent.
    /// </summary>
    public TempWorkspace(string parentDirectory, Action<string>? report = null)
    {
        this.report = report;
        if (!Directory.Exists(parentDirectory))
            throw new DirectoryNotFoundException(parentDirectory);
        RootPath = Path.Combine(Path.GetFullPath(parentDirectory), $"3deye-sort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>
    /// Directory owned exclusively by this operation.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Peak bytes measured at completed-chunk boundaries.
    /// </summary>
    public long PeakBytes => Interlocked.Read(ref peakBytes);

    /// <summary>
    /// Derives a chunk path without retaining a list of every chunk.
    /// </summary>
    public string ChunkPath(int pass, long index) =>
        Path.Combine(RootPath, $"pass-{pass:D4}-chunk-{index:D12}.txt");

    /// <summary>
    /// Accounts for a newly completed temporary chunk.
    /// </summary>
    public void TrackChunk(string path) => Observe(Interlocked.Add(ref currentBytes, new FileInfo(path).Length));

    /// <summary>
    /// Includes the completed destination staging file in the peak.
    /// </summary>
    public void TrackStaging(string path) => Observe(Interlocked.Read(ref currentBytes) + new FileInfo(path).Length);

    /// <summary>
    /// Deletes consumed chunks only after their replacement has closed.
    /// </summary>
    public void DeleteChunk(string path)
    {
        var length = new FileInfo(path).Length;
        File.Delete(path);
        Interlocked.Add(ref currentBytes, -length);
    }

    /// <summary>
    /// Updates the peak from concurrent chunk writers.
    /// </summary>
    private void Observe(long bytes)
    {
        long previous;
        do
        {
            previous = Interlocked.Read(ref peakBytes);
            if (previous >= bytes)
                return;
        } while (Interlocked.CompareExchange(ref peakBytes, bytes, previous) != previous);
    }

    /// <summary>
    /// Removes this operation's workspace without hiding its failure.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report?.Invoke($"Could not remove temporary directory '{RootPath}': {ex.Message}");
        }
    }
}
