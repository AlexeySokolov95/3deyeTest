using System.Runtime.InteropServices;

namespace LargeFileTools.Generation;

/// <summary>
/// Writes records in shuffled batches with bounded string storage.
/// </summary>
public sealed class ShuffledWriter
{
    /// <summary>
    /// Default budget for retained record strings.
    /// </summary>
    public const int BufferBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Caller-owned destination.
    /// </summary>
    private readonly TextWriter writer;

    /// <summary>
    /// Random source used only for shuffling.
    /// </summary>
    private readonly Random random;

    /// <summary>
    /// Maximum estimated storage for retained strings.
    /// </summary>
    private readonly long bufferBytes;

    /// <summary>
    /// Records awaiting the next flush.
    /// </summary>
    private readonly List<string> lines = [];

    /// <summary>
    /// Estimated storage occupied by retained strings.
    /// </summary>
    private long bufferedBytes;

    /// <summary>
    /// Creates a buffer without taking ownership of the destination.
    /// </summary>
    public ShuffledWriter(TextWriter writer, Random random, long bufferBytes = BufferBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferBytes, 1);
        this.writer = writer;
        this.random = random;
        this.bufferBytes = bufferBytes;
    }

    /// <summary>
    /// Buffers a record, flushing the previous batch when necessary.
    /// </summary>
    public void WriteLine(string line, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = 32L + 2L * line.Length;
        if (bufferedBytes + bytes > bufferBytes)
        {
            Flush(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (bytes > bufferBytes)
        {
            writer.WriteLine(line);
            return;
        }
        lines.Add(line);
        bufferedBytes += bytes;
    }

    /// <summary>
    /// Shuffles and writes all pending records, then releases their references.
    /// </summary>
    public void Flush(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        random.Shuffle(CollectionsMarshal.AsSpan(lines));
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteLine(line);
        }
        lines.Clear();
        bufferedBytes = 0;
    }
}
