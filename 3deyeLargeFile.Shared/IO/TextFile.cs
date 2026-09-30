namespace LargeFileTools.Shared;

/// <summary>
/// Creates consistently buffered UTF-8 output files.
/// </summary>
public static class TextFile
{
    /// <summary>
    /// Writer character-buffer size.
    /// </summary>
    public const int WriterCharacters = 16 * 1024;

    /// <summary>
    /// Creates a new file with strict UTF-8 encoding and LF endings.
    /// </summary>
    public static StreamWriter CreateWriter(string path)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1, FileOptions.SequentialScan);
        return new StreamWriter(stream, RecordFormat.Encoding, WriterCharacters) { NewLine = "\n" };
    }
}
