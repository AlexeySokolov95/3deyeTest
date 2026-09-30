using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Reads bounded byte lines without decoding their contents.
/// </summary>
internal sealed class LineReader : IDisposable
{
    /// <summary>
    /// Default read-ahead buffer size.
    /// </summary>
    internal const int BufferBytes = 64 * 1024;

    /// <summary>
    /// Owned or borrowed input stream.
    /// </summary>
    private readonly Stream stream;

    /// <summary>
    /// Diagnostic source name.
    /// </summary>
    private readonly string source;

    /// <summary>
    /// Maximum encoded bytes before the terminator.
    /// </summary>
    private readonly int maxLineBytes;

    /// <summary>
    /// Whether the caller owns the stream.
    /// </summary>
    private readonly bool leaveOpen;

    /// <summary>
    /// Reports oversized lines; null keeps reading strict.
    /// </summary>
    private readonly Action<RecordDataException>? reportInvalid;

    /// <summary>
    /// Read-ahead storage.
    /// </summary>
    private readonly byte[] buffer;

    /// <summary>
    /// Accumulated bytes of the current line.
    /// </summary>
    private byte[] line = [];

    /// <summary>
    /// Next unread byte in the read-ahead buffer.
    /// </summary>
    private int position;

    /// <summary>
    /// Valid bytes in the read-ahead buffer.
    /// </summary>
    private int available;

    /// <summary>
    /// Bytes in the current line.
    /// </summary>
    private int lineLength;

    /// <summary>
    /// Whether initial BOM detection is complete.
    /// </summary>
    private bool started;

    /// <summary>
    /// Opens a sequential reader, optionally skipping oversized lines.
    /// </summary>
    internal LineReader(string path, int maxLineBytes, Action<RecordDataException>? reportInvalid = null)
        : this(OpenInput(path, maxLineBytes), path, maxLineBytes, reportInvalid: reportInvalid)
    {
    }

    /// <summary>
    /// Reads from a stream, including streams with short reads.
    /// </summary>
    internal LineReader(Stream stream, string source, int maxLineBytes, bool leaveOpen = false,
        int bufferBytes = BufferBytes, Action<RecordDataException>? reportInvalid = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLineBytes, int.MaxValue - 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferBytes, 3);
        this.stream = stream;
        this.source = source;
        this.maxLineBytes = maxLineBytes;
        this.leaveOpen = leaveOpen;
        this.reportInvalid = reportInvalid;
        buffer = new byte[bufferBytes];
    }

    /// <summary>
    /// One-based physical line number, including skipped lines.
    /// </summary>
    internal long LineNumber { get; private set; }

    /// <summary>
    /// Number of oversized lines skipped.
    /// </summary>
    internal long SkippedLines { get; private set; }

    /// <summary>
    /// Current line bytes, valid until the next read.
    /// </summary>
    internal ReadOnlySpan<byte> Line => line.AsSpan(0, lineLength);

    /// <summary>
    /// Reads a line without its terminator, or returns false at EOF.
    /// </summary>
    internal bool Read(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            lineLength = 0;
            var lineEnded = false;
            try
            {
                if (!ReadLine(cancellationToken, out lineEnded))
                    return false;
                LineNumber++;
                return true;
            }
            catch (RecordDataException error) when (reportInvalid is not null)
            {
                LineNumber++;
                SkippedLines++;
                reportInvalid(error);
                if (!lineEnded)
                    SkipLine(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Finds the next line boundary without interpreting encoded text.
    /// </summary>
    private bool ReadLine(CancellationToken cancellationToken, out bool lineEnded)
    {
        lineEnded = false;
        cancellationToken.ThrowIfCancellationRequested();
        if (!started)
        {
            available = stream.ReadAtLeast(buffer, 3, throwOnEndOfStream: false);
            if (available >= 3 && buffer.AsSpan(0, 3).SequenceEqual("\uFEFF"u8))
                position = 3;
            started = true;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position == available)
            {
                available = stream.Read(buffer);
                position = 0;
                if (available == 0)
                {
                    lineEnded = true;
                    if (lineLength == 0)
                        return false;
                    Finish(hasNewline: false);
                    return true;
                }
            }

            var remaining = buffer.AsSpan(position, available - position);
            var newline = remaining.IndexOf((byte)'\n');
            var fragment = newline < 0 ? remaining : remaining[..newline];
            Append(fragment);
            position += fragment.Length;
            if (newline >= 0)
            {
                position++;
                lineEnded = true;
                Finish(hasNewline: true);
                return true;
            }
        }
    }

    /// <summary>
    /// Discards an oversized line using only the read-ahead buffer.
    /// </summary>
    private void SkipLine(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position == available)
            {
                available = stream.Read(buffer);
                position = 0;
                if (available == 0)
                    return;
            }

            var newline = buffer.AsSpan(position, available - position).IndexOf((byte)'\n');
            if (newline >= 0)
            {
                position += newline + 1;
                return;
            }
            position = available;
        }
    }

    /// <summary>
    /// Appends bytes without allowing oversized allocation.
    /// </summary>
    private void Append(ReadOnlySpan<byte> fragment)
    {
        if (fragment.IsEmpty)
            return;
        var required = (long)lineLength + fragment.Length;
        // One extra byte is allowed only for CR immediately before LF.
        if (required > maxLineBytes + 1L || (required > maxLineBytes && fragment[^1] != '\r'))
            throw OversizedLine();
        if (required > line.Length)
        {
            var capacity = (int)Math.Min(maxLineBytes + 1L, Math.Max(required, Math.Max(256L, 2L * line.Length)));
            Array.Resize(ref line, capacity);
        }
        fragment.CopyTo(line.AsSpan(lineLength));
        lineLength += fragment.Length;
    }

    /// <summary>
    /// Excludes CRLF from the byte limit while retaining bare CR.
    /// </summary>
    private void Finish(bool hasNewline)
    {
        if (hasNewline && lineLength > 0 && line[lineLength - 1] == '\r')
            lineLength--;
        if (lineLength > maxLineBytes)
            throw OversizedLine();
    }

    /// <summary>
    /// Creates a diagnostic for the current physical line.
    /// </summary>
    private RecordDataException OversizedLine() =>
        new(source, LineNumber + 1, $"record exceeds {maxLineBytes} UTF-8 bytes.");

    /// <summary>
    /// Validates the bound before acquiring a file handle.
    /// </summary>
    private static FileStream OpenInput(string path, int maxLineBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLineBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLineBytes, int.MaxValue - 1);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
    }

    /// <summary>
    /// Closes an owned input stream.
    /// </summary>
    public void Dispose()
    {
        if (!leaveOpen)
            stream.Dispose();
    }
}
