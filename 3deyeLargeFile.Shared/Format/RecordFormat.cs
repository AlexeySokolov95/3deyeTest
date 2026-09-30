using System.Globalization;
using System.Text;

namespace LargeFileTools.Shared;

/// <summary>
/// Defines the shared text-file format.
/// </summary>
public static class RecordFormat
{
    /// <summary>
    /// UTF-8 without a BOM and with strict error detection.
    /// </summary>
    public static readonly UTF8Encoding Encoding = new(false, true);

    /// <summary>
    /// Decodes and parses a UTF-8 record without its line terminator.
    /// </summary>
    public static TextRecord Parse(ReadOnlySpan<byte> bytes, string source, long recordNumber)
    {
        if (bytes.Contains((byte)'\r'))
            throw new RecordDataException(source, recordNumber, "a bare CR is not an accepted line terminator.");

        string line;
        try
        {
            line = Encoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new RecordDataException(source, recordNumber, "invalid UTF-8 encoding.");
        }
        return Parse(line, source, recordNumber);
    }

    /// <summary>
    /// Parses a record without changing its contents.
    /// </summary>
    public static TextRecord Parse(string line, string source, long recordNumber)
    {
        var separator = line.IndexOf(". ", StringComparison.Ordinal);
        if (separator <= 0)
            throw new RecordDataException(source, recordNumber, "expected '<Number>. <String>'.");

        var number = line.AsSpan(0, separator);
        foreach (var digit in number)
        {
            if (digit is < '0' or > '9')
                throw new RecordDataException(source, recordNumber, "the number must contain only ASCII digits.");
        }

        if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            throw new RecordDataException(source, recordNumber, "the number must be between 1 and 9223372036854775807.");

        if (line.AsSpan(separator + 2).IndexOfAny('\r', '\n') >= 0)
            throw new RecordDataException(source, recordNumber, "embedded line terminators are not allowed.");

        return new TextRecord(line, value, separator + 2);
    }

    /// <summary>
    /// Formats a generated record using invariant number notation.
    /// </summary>
    public static string Format(long number, string text) =>
        string.Concat(number.ToString(CultureInfo.InvariantCulture), ". ", text);
}
