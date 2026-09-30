using System.Globalization;
using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Sorter command-line entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Sorter usage and defaults.
    /// </summary>
    private const string Help = """
        Sort a UTF-8 file by case-sensitive InvariantCulture text, then positive Int64 number.
        Usage: 3deyeLargeFile.Sorter --input <path> --output <path> [options]

          --chunk-lines <int>       Maximum lines per portion (100000)
          --workers <int>           Parsing/sorting workers (up to 2 by default)
          --temp-dir <path>         Temporary parent directory (system temp)
          --max-merge-files <int>    Maximum files merged at once (32)
          --max-line-size <MB>      Maximum UTF-8 record size (1 MB)
          --help                    Show this help

        Sizes are positive whole numbers of MB (1 MB = 1024 * 1024 bytes), without suffixes.
        Memory use depends on line lengths and worker count. Directories must exist.
        The output file must not already exist.
        Invalid input records are skipped and logged to stderr.
        """;

    /// <summary>
    /// Runs sorting with shared CLI error handling.
    /// </summary>
    public static Task<int> Main(string[] args) => CliHost.RunAsync(args, Help, async (token, report) =>
    {
        var result = await new ExternalSorter().SortAsync(SortOptions.Parse(args), token, report);
        return string.Create(CultureInfo.InvariantCulture,
            $"Sorted {result.Records} records; skipped {result.SkippedRecords}; {result.InputBytes} input bytes, {result.OutputBytes} output bytes; {result.InitialChunks} chunks, {result.MergePasses} merge passes; peak temporary bytes {result.PeakTemporaryBytes}; {result.Elapsed.TotalSeconds:F3} s.");
    });
}
