namespace LargeFileTools.Shared;

/// <summary>
/// Invalid data in a numbered input record.
/// </summary>
public sealed class RecordDataException : Exception
{
    /// <summary>
    /// Identifies the source and the invalid record.
    /// </summary>
    public RecordDataException(string source, long recordNumber, string reason)
        : base($"{source}: record {recordNumber}: {reason}")
    {
        RecordNumber = recordNumber;
    }

    /// <summary>
    /// One-based record number.
    /// </summary>
    public long RecordNumber { get; }
}
