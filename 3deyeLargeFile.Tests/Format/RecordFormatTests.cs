using System.Globalization;
using LargeFileTools.Shared;
using LargeFileTools.Sorting;

namespace LargeFileTools.Tests.Format;

/// <summary>
/// Checks parsing boundaries and sort semantics.
/// </summary>
public sealed class RecordFormatTests
{
    /// <summary>
    /// Accepts valid numbers while retaining the original record.
    /// </summary>
    [Theory]
    [InlineData("1. Apple", 1, "Apple")]
    [InlineData("9223372036854775807. x", long.MaxValue, "x")]
    [InlineData("001. ", 1, "")]
    [InlineData("000000000000000000000000000000001.  a. b ", 1, " a. b ")]
    public void PreservesValidRecords(string line, long number, string text)
    {
        var record = RecordFormat.Parse(line, "input", 7);
        Assert.Equal(line, record.Line);
        Assert.Equal(number, record.Number);
        Assert.Equal(text, record.Text.ToString());
    }

    /// <summary>
    /// Rejects malformed numbers, separators, and embedded terminators.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("1.Apple")]
    [InlineData("+1. Apple")]
    [InlineData(" 1. Apple")]
    [InlineData("1 . Apple")]
    [InlineData("-. Apple")]
    [InlineData("-1. Apple")]
    [InlineData("-9223372036854775808. Apple")]
    [InlineData("0. Apple")]
    [InlineData("000. Apple")]
    [InlineData("-0. Apple")]
    [InlineData("1.2. Apple")]
    [InlineData("9223372036854775808. Apple")]
    [InlineData("-9223372036854775809. Apple")]
    [InlineData("١. Apple")]
    [InlineData("1. a\rb")]
    [InlineData("1. a\nb")]
    public void RejectsInvalidRecords(string line)
    {
        var error = Assert.Throws<RecordDataException>(() => RecordFormat.Parse(line, "input", 7));
        Assert.Equal(7, error.RecordNumber);
        Assert.Contains("input: record 7:", error.Message);
    }

    /// <summary>
    /// Uses invariant case-sensitive ordering regardless of the current culture.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void OrdersTextBeforeNumbers(string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var input = new[] { "2. a", "9223372036854775807. A", "1. A", "1. Z" };
            var records = input.Select(line => RecordFormat.Parse(line, "input", 1)).ToArray();
            Array.Sort(records, RecordComparer.Instance);
            Assert.Equal(["2. a", "1. A", "9223372036854775807. A", "1. Z"],
                records.Select(record => record.Line));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// Canonical Unicode equivalents use the numeric secondary key.
    /// </summary>
    [Fact]
    public void ComparesEquivalentUnicodeTextNumerically()
    {
        var left = RecordFormat.Parse("2. é", "input", 1);
        var right = RecordFormat.Parse("1. e\u0301", "input", 2);
        Assert.True(RecordComparer.Instance.Compare(left, right) > 0);
    }

    /// <summary>
    /// Different numeric spellings can have equal sort keys.
    /// </summary>
    [Fact]
    public void TreatsLeadingZerosAsNumericTies()
    {
        var left = RecordFormat.Parse("001. x", "input", 1);
        var right = RecordFormat.Parse("1. x", "input", 2);
        Assert.Equal(0, RecordComparer.Instance.Compare(left, right));
    }
}
