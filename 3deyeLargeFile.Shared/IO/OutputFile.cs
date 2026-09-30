namespace LargeFileTools.Shared;

/// <summary>
/// Publishes a completed file without exposing partial output.
/// </summary>
public sealed class OutputFile : IDisposable
{
    /// <summary>
    /// Final destination path.
    /// </summary>
    private readonly string destination;

    /// <summary>
    /// Receives cleanup diagnostics.
    /// </summary>
    private readonly Action<string>? report;

    /// <summary>
    /// Whether staging data has already been published.
    /// </summary>
    private bool published;

    /// <summary>
    /// Chooses a staging path beside the validated destination.
    /// </summary>
    public OutputFile(string destination, Action<string>? report = null)
    {
        this.destination = FilePaths.ValidateOutput(destination);
        this.report = report;
        StagingPath = Path.Combine(Path.GetDirectoryName(this.destination)!, $".3deye-{Guid.NewGuid():N}.tmp");
    }

    /// <summary>
    /// Unique path to be created by the writer.
    /// </summary>
    public string StagingPath { get; }

    /// <summary>
    /// Renames the completed staging file on the destination filesystem.
    /// </summary>
    public void Publish(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(StagingPath, destination);
        published = true;
    }

    /// <summary>
    /// Removes unpublished staging data without masking processing errors.
    /// </summary>
    public void Dispose()
    {
        if (published)
            return;
        try
        {
            File.Delete(StagingPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report?.Invoke($"Could not remove staging file '{StagingPath}': {ex.Message}");
        }
    }
}
