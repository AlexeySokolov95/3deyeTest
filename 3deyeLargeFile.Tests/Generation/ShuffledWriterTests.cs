using System.Globalization;
using LargeFileTools.Generation;

namespace LargeFileTools.Tests.Generation;

/// <summary>
/// Checks bounded shuffling, record preservation, and interrupted writes.
/// </summary>
public sealed class ShuffledWriterTests
{
    /// <summary>
    /// A partial buffer is shuffled without losing duplicate records.
    /// </summary>
    [Fact]
    public void ShufflesAndPreservesEveryOccurrence()
    {
        using var output = new StringWriter();
        var writer = new ShuffledWriter(output, new Random(42));
        var lines = Enumerable.Range(1, 50).Select(i => $"{i}. text").Append("1. text").ToArray();
        foreach (var line in lines)
            writer.WriteLine(line);
        Assert.Equal(string.Empty, output.ToString());
        writer.Flush();
        var actual = ReadLines(output);
        Assert.False(lines.SequenceEqual(actual));
        Assert.Equal(lines.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        writer.Flush();
        Assert.Equal(actual, ReadLines(output));
    }

    /// <summary>
    /// A full buffer is written before accepting the next record.
    /// </summary>
    [Fact]
    public void FlushesAtTheMemoryLimit()
    {
        using var output = new StringWriter();
        var writer = new ShuffledWriter(output, new Random(42), bufferBytes: 80);
        writer.WriteLine("1. a");
        writer.WriteLine("2. b");
        Assert.Equal(string.Empty, output.ToString());
        writer.WriteLine("3. c");
        Assert.Equal(["1. a", "2. b"], ReadLines(output).Order(StringComparer.Ordinal));
        writer.Flush();
        Assert.Equal(["1. a", "2. b", "3. c"], ReadLines(output).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Records larger than the buffer are written directly after pending records.
    /// </summary>
    [Fact]
    public void WritesOversizedRecordsWithoutRetainingThem()
    {
        using var output = new StringWriter();
        var writer = new ShuffledWriter(output, new Random(42), bufferBytes: 80);
        var oversized = "2. " + new string('x', 1000);
        writer.WriteLine("1. a");
        writer.WriteLine(oversized);
        Assert.Equal(["1. a", oversized], ReadLines(output));
        writer.WriteLine("3. c");
        writer.Flush();
        Assert.Equal(["1. a", oversized, "3. c"], ReadLines(output));
    }

    /// <summary>
    /// Repeated flushes release old records and preserve the final partial buffer.
    /// </summary>
    [Fact]
    public void ReusesBufferWithoutDuplicatingPreviousBatches()
    {
        using var output = new StringWriter();
        var writer = new ShuffledWriter(output, new Random(42), bufferBytes: 100);
        var lines = Enumerable.Range(1, 101).Select(i => $"{i}. a").ToArray();
        foreach (var line in lines)
            writer.WriteLine(line);
        Assert.Equal(100, ReadLines(output).Length);
        writer.Flush();
        Assert.Equal(lines.Order(StringComparer.Ordinal), ReadLines(output).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Cancellation stops writing remaining buffered records.
    /// </summary>
    [Fact]
    public void CancelsDuringFlush()
    {
        using var stop = new CancellationTokenSource();
        using var output = new CancellingWriter(stop, 3);
        var writer = new ShuffledWriter(output, new Random(42));
        for (var i = 1; i <= 20; i++)
            writer.WriteLine($"{i}. a");
        Assert.Throws<OperationCanceledException>(() => writer.Flush(stop.Token));
        Assert.Equal(3, output.RecordsWritten);
    }

    /// <summary>
    /// Cancellation while flushing also prevents a subsequent oversized write.
    /// </summary>
    [Fact]
    public void CancelsBeforeWritingOversizedRecord()
    {
        using var stop = new CancellationTokenSource();
        using var output = new CancellingWriter(stop, 1);
        var writer = new ShuffledWriter(output, new Random(42), bufferBytes: 80);
        writer.WriteLine("1. a");
        Assert.Throws<OperationCanceledException>(() => writer.WriteLine("2. " + new string('x', 1000), stop.Token));
        Assert.Equal(1, output.RecordsWritten);
    }

    /// <summary>
    /// I/O errors propagate unchanged to the file generator.
    /// </summary>
    [Fact]
    public void PropagatesWriteFailure()
    {
        var failure = new IOException("Injected write failure.");
        using var output = new FailingWriter(failure);
        var writer = new ShuffledWriter(output, new Random(42));
        writer.WriteLine("1. a");
        Assert.Same(failure, Assert.Throws<IOException>(() => writer.Flush()));
    }

    /// <summary>
    /// An empty flush does not write records or take ownership of the destination.
    /// </summary>
    [Fact]
    public void LeavesCallerWriterOpen()
    {
        using var output = new StringWriter();
        var writer = new ShuffledWriter(output, new Random(42));
        writer.Flush();
        output.WriteLine("caller");
        Assert.Equal(["caller"], ReadLines(output));
    }

    /// <summary>
    /// The string-storage budget must be positive.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidBudget(int bytes)
    {
        using var output = new StringWriter();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShuffledWriter(output, new Random(42), bytes));
    }

    /// <summary>
    /// Reads captured records independently of platform line endings.
    /// </summary>
    private static string[] ReadLines(StringWriter output)
    {
        using var reader = new StringReader(output.ToString());
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines.ToArray();
    }

    /// <summary>
    /// Cancels after a selected number of writes.
    /// </summary>
    private sealed class CancellingWriter(CancellationTokenSource stop, int limit) : StringWriter(CultureInfo.InvariantCulture)
    {
        /// <summary>
        /// Records accepted before cancellation.
        /// </summary>
        public int RecordsWritten { get; private set; }

        /// <summary>
        /// Writes one record and requests cancellation at the limit.
        /// </summary>
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (++RecordsWritten == limit)
                stop.Cancel();
        }
    }

    /// <summary>
    /// Injects an output error without filesystem timing dependencies.
    /// </summary>
    private sealed class FailingWriter(IOException failure) : StringWriter(CultureInfo.InvariantCulture)
    {
        /// <summary>
        /// Throws the original failure.
        /// </summary>
        public override void WriteLine(string? value) => throw failure;
    }
}
