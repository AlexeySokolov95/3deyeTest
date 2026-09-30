using System.Text;
using LargeFileTools.Shared;
using LargeFileTools.Sorting;

namespace LargeFileTools.Tests.Format;

/// <summary>
/// Exercises byte boundaries, encoding validation, and line limits.
/// </summary>
public sealed class RecordReaderTests
{
    /// <summary>
    /// Accepts mixed endings and a final unterminated record.
    /// </summary>
    [Fact]
    public void HandlesBomAndFragmentedUnicode()
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFF1. Яблоко\r\n2. 😀\n3. ");
        using var stream = new FragmentedStream(bytes);
        using var reader = new RecordReader(stream, "memory", 128, bufferBytes: 3);
        Assert.Equal("1. Яблоко", reader.Read()!.Value.Line);
        Assert.Equal("2. 😀", reader.Read()!.Value.Line);
        Assert.Equal("3. ", reader.Read()!.Value.Line);
        Assert.Null(reader.Read());
        Assert.Equal(3, reader.RecordsRead);
    }

    /// <summary>
    /// Empty input and a BOM-only input contain no records.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("\uFEFF")]
    public void HandlesEmptyInput(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new RecordReader(stream, "memory", 10);
        Assert.Null(reader.Read());
    }

    /// <summary>
    /// The record limit counts UTF-8 bytes and excludes CRLF.
    /// </summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("")]
    public void AcceptsExactByteLimit(string terminator)
    {
        var errors = new List<RecordDataException>();
        using var stream = new FragmentedStream(Encoding.UTF8.GetBytes("1. é" + terminator));
        using var reader = new RecordReader(stream, "memory", 5, bufferBytes: 3, reportInvalid: errors.Add);
        Assert.Equal("1. é", reader.Read()!.Value.Line);
        Assert.Null(reader.Read());
        Assert.Empty(errors);
    }

    /// <summary>
    /// Recovers across invalid encoding, numbers, and oversized fragmented lines.
    /// </summary>
    [Theory]
    [InlineData(3, "\n")]
    [InlineData(7, "\r\n")]
    [InlineData(64, "\n")]
    [InlineData(RecordReader.BufferBytes, "\r\n")]
    public void SkipsInvalidRecordsWithPhysicalLineNumbers(int bufferBytes, string terminator)
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFFbad\n1. good\r\n")
            .Concat(new byte[] { 0xFF, 10 })
            .Concat(Encoding.UTF8.GetBytes("2. " + new string('x', 100_000) + terminator + "0. zero\n3. é\n\n-1. bad"))
            .ToArray();
        var errors = new List<RecordDataException>();
        using var stream = new MemoryStream(bytes);
        using var reader = new RecordReader(stream, "memory", 16, bufferBytes: bufferBytes, reportInvalid: errors.Add);
        Assert.Equal("1. good", reader.Read()!.Value.Line);
        Assert.Equal("3. é", reader.Read()!.Value.Line);
        Assert.Null(reader.Read());
        Assert.Null(reader.Read());
        Assert.Equal(2, reader.RecordsRead);
        Assert.Equal(6, reader.RecordsSkipped);
        Assert.Equal([1L, 3, 4, 5, 7, 8], errors.Select(error => error.RecordNumber));
        Assert.Contains("invalid UTF-8", errors[1].Message);
        Assert.Contains("exceeds 16", errors[2].Message);
    }

    /// <summary>
    /// Reports a final oversized record only once, with or without a terminator.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SkipsOversizedFinalRecord(string terminator)
    {
        var errors = new List<RecordDataException>();
        using var stream = new FragmentedStream(Encoding.UTF8.GetBytes("1. " + new string('x', 1000) + terminator));
        using var reader = new RecordReader(stream, "memory", 16, bufferBytes: 3, reportInvalid: errors.Add);
        Assert.Null(reader.Read());
        Assert.Null(reader.Read());
        Assert.Single(errors);
        Assert.Equal(1, reader.RecordsSkipped);
        Assert.Equal(0, reader.RecordsRead);
    }

    /// <summary>
    /// Rejects a bare CR without swallowing the following valid line.
    /// </summary>
    [Theory]
    [InlineData("1. bad\rcr\n2. ok", "2. ok")]
    [InlineData("1. bad\r", null)]
    [InlineData("1. x\r", null)]
    public void SkipsBareCarriageReturn(string text, string? expected)
    {
        var errors = new List<RecordDataException>();
        using var stream = new FragmentedStream(Encoding.UTF8.GetBytes(text));
        using var reader = new RecordReader(stream, "memory", 16, bufferBytes: 3, reportInvalid: errors.Add);
        Assert.Equal(expected, reader.Read()?.Line);
        Assert.Null(reader.Read());
        Assert.Single(errors);
        Assert.Contains("bare CR", errors[0].Message);
    }

    /// <summary>
    /// Cancellation interrupts draining a record that exceeds the line limit.
    /// </summary>
    [Fact]
    public void CancelsWhileSkippingOversizedRecord()
    {
        using var stop = new CancellationTokenSource();
        var reads = 0;
        using var stream = new ObservedStream(Encoding.UTF8.GetBytes(new string('x', 1000)), () =>
        {
            if (++reads == 4)
                stop.Cancel();
        });
        var errors = new List<RecordDataException>();
        using var reader = new RecordReader(stream, "memory", 16, bufferBytes: 8, reportInvalid: errors.Add);
        Assert.Throws<OperationCanceledException>(() => reader.Read(stop.Token));
        Assert.Single(errors);
        Assert.Equal(4, reads);
        Assert.True(stream.Position < stream.Length);
    }

    /// <summary>
    /// I/O failures while draining invalid data remain fatal.
    /// </summary>
    [Fact]
    public void PropagatesReadFailureWhileSkipping()
    {
        var failure = new IOException("Injected read failure.");
        var reads = 0;
        using var stream = new ObservedStream(Encoding.UTF8.GetBytes(new string('x', 1000)), () =>
        {
            if (++reads == 4)
                throw failure;
        });
        var errors = new List<RecordDataException>();
        using var reader = new RecordReader(stream, "memory", 16, bufferBytes: 8, reportInvalid: errors.Add);
        Assert.Same(failure, Assert.Throws<IOException>(() => reader.Read()));
        Assert.Single(errors);
    }

    /// <summary>
    /// Stops at oversized records before consuming their full contents.
    /// </summary>
    [Fact]
    public void RejectsOversizedLineEarly()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1. " + new string('x', 1_000_000)));
        using var reader = new RecordReader(stream, "memory", 16, leaveOpen: true);
        Assert.Throws<RecordDataException>(() => reader.Read());
        Assert.True(stream.Position < stream.Length);
    }

    /// <summary>
    /// Does not replace invalid UTF-8 with replacement characters.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 49, 46, 32, 0xC0, 0xAF, 10 })]
    [InlineData(new byte[] { 49, 46, 32, 0xF0, 0x9F })]
    public void RejectsInvalidUtf8(byte[] bytes)
    {
        using var stream = new FragmentedStream(bytes);
        using var reader = new RecordReader(stream, "memory", 64, bufferBytes: 3);
        Assert.Contains("UTF-8", Assert.Throws<RecordDataException>(() => reader.Read()).Message);
    }

    /// <summary>
    /// Reports blank records and bare carriage returns.
    /// </summary>
    [Theory]
    [InlineData("1. good\n\n", 2)]
    [InlineData("\n", 1)]
    [InlineData("1. bad\r", 1)]
    [InlineData("1. a\rb\n", 1)]
    public void ReportsInvalidRecordNumber(string text, long recordNumber)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new RecordReader(stream, "memory", 64);
        var error = Assert.Throws<RecordDataException>(() =>
        {
            while (reader.Read() is not null) { }
        });
        Assert.Equal(recordNumber, error.RecordNumber);
    }

    /// <summary>
    /// Honors cancellation between records.
    /// </summary>
    [Fact]
    public void CancelsReading()
    {
        using var stream = new MemoryStream("1. a\n2. b\n"u8.ToArray());
        using var reader = new RecordReader(stream, "memory", 64);
        Assert.NotNull(reader.Read());
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => reader.Read(stop.Token));
    }

    /// <summary>
    /// Simulates a stream that returns one byte per read.
    /// </summary>
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        /// <summary>
        /// Splits span reads at arbitrary encoded-byte boundaries.
        /// </summary>
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);

        /// <summary>
        /// Splits array reads at arbitrary encoded-byte boundaries.
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
    }

    /// <summary>
    /// Injects cancellation or failure at a selected read boundary.
    /// </summary>
    private sealed class ObservedStream(byte[] bytes, Action onRead) : MemoryStream(bytes)
    {
        /// <summary>
        /// Runs the callback before reading the next buffer.
        /// </summary>
        public override int Read(Span<byte> buffer)
        {
            onRead();
            return base.Read(buffer);
        }
    }
}
