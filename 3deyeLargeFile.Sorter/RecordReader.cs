using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Reads parsed UTF-8 records with bounded line storage.
/// </summary>
public sealed class RecordReader : IDisposable
{
    /// <summary>
    /// Default read-ahead buffer size.
    /// </summary>
    public const int BufferBytes = LineReader.BufferBytes;

    /// <summary>
    /// Reader of physical byte lines.
    /// </summary>
    private readonly LineReader reader;

    /// <summary>
    /// Diagnostic source name.
    /// </summary>
    private readonly string source;

    /// <summary>
    /// Reports skipped records; null keeps parsing strict.
    /// </summary>
    private readonly Action<RecordDataException>? reportInvalid;

    /// <summary>
    /// Skipped records that passed the encoded size check.
    /// </summary>
    private long invalidRecords;

    /// <summary>
    /// Opens a sequential reader, optionally skipping invalid records.
    /// </summary>
    public RecordReader(string path, int maxLineBytes, Action<RecordDataException>? reportInvalid = null)
    {
        reader = new LineReader(path, maxLineBytes, reportInvalid);
        source = path;
        this.reportInvalid = reportInvalid;
    }

    /// <summary>
    /// Reads from a stream, including streams with short reads.
    /// </summary>
    public RecordReader(Stream stream, string source, int maxLineBytes, bool leaveOpen = false,
        int bufferBytes = BufferBytes, Action<RecordDataException>? reportInvalid = null)
    {
        reader = new LineReader(stream, source, maxLineBytes, leaveOpen, bufferBytes, reportInvalid);
        this.source = source;
        this.reportInvalid = reportInvalid;
    }

    /// <summary>
    /// Number of successfully parsed records.
    /// </summary>
    public long RecordsRead { get; private set; }

    /// <summary>
    /// Number of invalid records skipped.
    /// </summary>
    public long RecordsSkipped => reader.SkippedLines + invalidRecords;

    /// <summary>
    /// Reads one record or returns null at clean end of input.
    /// </summary>
    public TextRecord? Read(CancellationToken cancellationToken = default)
    {
        while (reader.Read(cancellationToken))
        {
            try
            {
                var record = RecordFormat.Parse(reader.Line, source, reader.LineNumber);
                RecordsRead++;
                return record;
            }
            catch (RecordDataException error) when (reportInvalid is not null)
            {
                invalidRecords++;
                reportInvalid(error);
            }
        }
        return null;
    }

    /// <summary>
    /// Closes an owned input stream.
    /// </summary>
    public void Dispose() => reader.Dispose();
}
