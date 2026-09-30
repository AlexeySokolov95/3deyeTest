using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Owns bounded encoded lines and their worker-decoded records.
/// </summary>
internal sealed class RecordBatch
{
    /// <summary>
    /// Owned line bytes and their physical source numbers.
    /// </summary>
    private readonly EncodedLine[] lines;

    /// <summary>
    /// Number of encoded lines.
    /// </summary>
    private int lineCount;

    /// <summary>
    /// Reserves space for at most the specified number of lines.
    /// </summary>
    internal RecordBatch(int maxLines)
    {
        lines = new EncodedLine[maxLines];
    }

    /// <summary>
    /// Decoded records, allocated only by the worker.
    /// </summary>
    internal TextRecord[] Records { get; private set; } = [];

    /// <summary>
    /// Number of successfully decoded records.
    /// </summary>
    internal int Count { get; private set; }

    /// <summary>
    /// Copies a line if the portion has a free slot.
    /// </summary>
    internal bool TryAdd(ReadOnlySpan<byte> line, long recordNumber)
    {
        if (lineCount == lines.Length)
            return false;
        lines[lineCount++] = new EncodedLine(line.ToArray(), recordNumber);
        return true;
    }

    /// <summary>
    /// Decodes on the worker, reporting invalid records and returning their count.
    /// </summary>
    internal long Parse(string source, CancellationToken cancellationToken, Action<string>? report)
    {
        Records = new TextRecord[lineCount];
        var skipped = 0L;
        for (var i = 0; i < lineCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[i];
            try
            {
                Records[Count] = RecordFormat.Parse(line.Bytes, source, line.RecordNumber);
                Count++;
            }
            catch (RecordDataException error)
            {
                skipped++;
                report?.Invoke($"Skipped {error.Message}");
            }
        }
        return skipped;
    }

    /// <summary>
    /// Keeps a line's bytes with its diagnostic location.
    /// </summary>
    /// <param name="Bytes">
    /// Encoded contents without the terminator.
    /// </param>
    /// <param name="RecordNumber">
    /// One-based physical source line.
    /// </param>
    private readonly record struct EncodedLine(byte[] Bytes, long RecordNumber);
}
