using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Orders records by case-sensitive invariant text, then number.
/// </summary>
public sealed class RecordComparer : IComparer<TextRecord>
{
    /// <summary>
    /// Stateless comparer shared by sorting and merging.
    /// </summary>
    public static readonly RecordComparer Instance = new();

    /// <summary>
    /// Compares keys without allocating substrings.
    /// </summary>
    public int Compare(TextRecord x, TextRecord y)
    {
        var textOrder = x.Text.CompareTo(y.Text, StringComparison.InvariantCulture);
        return textOrder != 0 ? textOrder : x.Number.CompareTo(y.Number);
    }
}
