using System.Text;
using LargeFileTools.Sorting;

namespace LargeFileTools.Tests.Sorting;

/// <summary>
/// Checks encoded ownership and worker-side parsing bounds.
/// </summary>
public sealed class RecordBatchTests
{
    /// <summary>
    /// Copies input bytes before the reader can reuse its storage.
    /// </summary>
    [Fact]
    public void OwnsEncodedLinesUntilParsing()
    {
        var batch = new RecordBatch(2);
        var bytes = Encoding.UTF8.GetBytes("2. Яблоко");
        Assert.True(batch.TryAdd(bytes, 7));
        Array.Fill(bytes, (byte)'x');
        Assert.True(batch.TryAdd("001. Apple"u8, 9));
        Assert.Empty(batch.Records);

        Assert.Equal(0, batch.Parse("input", CancellationToken.None, null));
        Assert.Equal(2, batch.Count);
        Assert.Equal(["2. Яблоко", "001. Apple"], batch.Records.Select(record => record.Line));
    }

    /// <summary>
    /// Empty lines occupy slots and retain their diagnostic location.
    /// </summary>
    [Fact]
    public void EnforcesLineLimitIncludingEmptyLines()
    {
        var batch = new RecordBatch(2);
        Assert.True(batch.TryAdd("1. a"u8, 1));
        Assert.True(batch.TryAdd([], 2));
        Assert.False(batch.TryAdd([], 3));
        var messages = new List<string>();
        Assert.Equal(1, batch.Parse("input", CancellationToken.None, messages.Add));
        Assert.Equal(1, batch.Count);
        Assert.Equal("1. a", batch.Records[0].Line);
        Assert.Contains("input: record 2:", Assert.Single(messages));
    }

    /// <summary>
    /// Short and long lines fill the same number of slots.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(100_000)]
    public void CountsLinesIndependentlyOfTheirLength(int textLength)
    {
        var batch = new RecordBatch(2);
        var line = "1. " + new string('Я', textLength);
        var bytes = Encoding.UTF8.GetBytes(line);

        Assert.True(batch.TryAdd(bytes, 1));
        Assert.True(batch.TryAdd(bytes, 2));
        Assert.False(batch.TryAdd("2. x"u8, 3));

        Assert.Equal(0, batch.Parse("input", CancellationToken.None, null));
        Assert.Equal(2, batch.Count);
        Assert.All(batch.Records, record => Assert.Equal(line, record.Line));
    }

    /// <summary>
    /// Invalid byte sequences reach the worker and retain physical source numbers.
    /// </summary>
    [Fact]
    public void ParsesAndReportsInvalidRecordsOnDemand()
    {
        var batch = new RecordBatch(6);
        Assert.True(batch.TryAdd(new byte[] { 49, 46, 32, 0xFF }, 3));
        Assert.True(batch.TryAdd("0. zero"u8, 5));
        Assert.True(batch.TryAdd("1. bad\rcr"u8, 6));
        Assert.True(batch.TryAdd([], 8));
        Assert.True(batch.TryAdd("-1. negative"u8, 10));
        Assert.True(batch.TryAdd("9223372036854775807. valid"u8, 11));
        var messages = new List<string>();

        Assert.Equal(5, batch.Parse("input", CancellationToken.None, messages.Add));
        Assert.Equal(1, batch.Count);
        Assert.Equal(long.MaxValue, batch.Records[0].Number);
        Assert.Equal(5, messages.Count);
        foreach (var number in new[] { 3, 5, 6, 8, 10 })
            Assert.Contains(messages, message => message.Contains($"input: record {number}:"));
        Assert.Contains("invalid UTF-8", messages[0]);
        Assert.Contains("bare CR", messages[2]);
    }

    /// <summary>
    /// Cancellation during error reporting stops before decoding the next record.
    /// </summary>
    [Fact]
    public void CancelsDuringParsing()
    {
        var batch = new RecordBatch(2);
        Assert.True(batch.TryAdd("bad"u8, 1));
        Assert.True(batch.TryAdd("1. a"u8, 2));
        using var stop = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => batch.Parse("input", stop.Token, _ => stop.Cancel()));
        Assert.Equal(0, batch.Count);
    }
}
