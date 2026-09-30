using System.Text;
using LargeFileTools.Shared;
using LargeFileTools.Sorting;

namespace LargeFileTools.Tests.Format;

/// <summary>
/// Checks byte framing before worker decoding.
/// </summary>
public sealed class LineReaderTests
{
    /// <summary>
    /// Frames invalid UTF-8 and numbers without interpreting their contents.
    /// </summary>
    [Fact]
    public void ReadsEncodedLinesWithoutParsing()
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFFbad\r\n0. zero\n")
            .Concat(new byte[] { 49, 46, 32, 0xFF, 10 })
            .Concat(Encoding.UTF8.GetBytes("1. bare\r"))
            .ToArray();
        using var stream = new FragmentedStream(bytes);
        using var reader = new LineReader(stream, "input", 64, bufferBytes: 3);
        var expected = new[] { "bad"u8.ToArray(), "0. zero"u8.ToArray(), new byte[] { 49, 46, 32, 0xFF }, "1. bare\r"u8.ToArray() };
        foreach (var line in expected)
        {
            Assert.True(reader.Read());
            Assert.Equal(line, reader.Line.ToArray());
        }
        Assert.False(reader.Read());
        Assert.Equal(4, reader.LineNumber);
        Assert.Equal(0, reader.SkippedLines);
    }

    /// <summary>
    /// Preserves physical numbers after an oversized line and an empty line.
    /// </summary>
    [Fact]
    public void RetainsLineNumbersAcrossDiscardedInput()
    {
        using var stream = new FragmentedStream(Encoding.UTF8.GetBytes("1. a\n" + new string('x', 100) + "\r\n\n2. é"));
        var errors = new List<RecordDataException>();
        using var reader = new LineReader(stream, "input", 8, bufferBytes: 3, reportInvalid: errors.Add);
        Assert.True(reader.Read());
        Assert.Equal(1, reader.LineNumber);
        Assert.True(reader.Read());
        Assert.Equal(3, reader.LineNumber);
        Assert.True(reader.Line.IsEmpty);
        Assert.True(reader.Read());
        Assert.Equal(4, reader.LineNumber);
        Assert.Equal("2. é", Encoding.UTF8.GetString(reader.Line));
        Assert.False(reader.Read());
        Assert.Equal(2, Assert.Single(errors).RecordNumber);
        Assert.Equal(1, reader.SkippedLines);
    }

    /// <summary>
    /// Splits multibyte characters and terminators across individual reads.
    /// </summary>
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        /// <summary>
        /// Supplies one byte at a time.
        /// </summary>
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);
    }
}
