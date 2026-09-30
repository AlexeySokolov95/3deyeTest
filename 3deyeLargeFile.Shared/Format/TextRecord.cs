namespace LargeFileTools.Shared;

/// <summary>
/// A parsed record retaining its original spelling.
/// </summary>
/// <param name="Line">
/// Original contents without the line terminator.
/// </param>
/// <param name="Number">
/// Numeric sort key.
/// </param>
/// <param name="TextOffset">
/// Start of the text sort key.
/// </param>
public readonly record struct TextRecord(string Line, long Number, int TextOffset)
{
    /// <summary>
    /// The text sort key without allocating a substring.
    /// </summary>
    public ReadOnlySpan<char> Text => Line.AsSpan(TextOffset);

    /// <summary>
    /// Conservative size of the retained string, including alignment.
    /// </summary>
    public long StringBytes => 32L + 2L * Line.Length;
}
