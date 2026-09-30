using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Validated external-sort settings.
/// </summary>
public sealed record SortOptions
{
    /// <summary>
    /// Source file.
    /// </summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Destination file.
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Maximum source lines in one portion.
    /// </summary>
    public int ChunkLines { get; init; } = 100_000;

    /// <summary>
    /// Workers that parse, sort, and write portions.
    /// </summary>
    public int Workers { get; init; } = Math.Min(2, Environment.ProcessorCount);

    /// <summary>
    /// Parent of the run's temporary directory.
    /// </summary>
    public string TempDirectory { get; init; } = Path.GetTempPath();

    /// <summary>
    /// Maximum files merged at once.
    /// </summary>
    public int MaxMergeFiles { get; init; } = 32;

    /// <summary>
    /// Maximum UTF-8 bytes per record, excluding its terminator.
    /// </summary>
    public int MaxLineBytes { get; init; } = 1 << 20;

    /// <summary>
    /// Parses the sorter command line.
    /// </summary>
    public static SortOptions Parse(string[] args)
    {
        var cli = new CliArguments(args, "--input", "--output", "--chunk-lines", "--workers",
            "--temp-dir", "--max-merge-files", "--max-line-size");
        var maxLine = cli.Megabytes("--max-line-size", 1);
        if (maxLine >= int.MaxValue)
            throw new CliException("--max-line-size must not exceed 2047 MB.");
        var options = new SortOptions
        {
            InputPath = cli.Required("--input"),
            OutputPath = cli.Required("--output"),
            ChunkLines = cli.Integer("--chunk-lines", 100_000, 1),
            Workers = cli.Integer("--workers", Math.Min(2, Environment.ProcessorCount), 1),
            TempDirectory = cli.Get("--temp-dir", Path.GetTempPath()),
            MaxMergeFiles = cli.Integer("--max-merge-files", 32, 2),
            MaxLineBytes = (int)maxLine
        };
        options.Validate();
        return options;
    }

    /// <summary>
    /// Checks file paths and resource settings before processing.
    /// </summary>
    public void Validate()
    {
        if (ChunkLines <= 0 || ChunkLines > Array.MaxLength)
            throw new CliException($"--chunk-lines must be between 1 and {Array.MaxLength}.");
        if (Workers <= 0 || MaxMergeFiles < 2 || MaxLineBytes <= 0 || MaxLineBytes >= Array.MaxLength)
            throw new CliException("Require positive workers and line size within the array limit, and max-merge-files >= 2.");
        if (!File.Exists(InputPath))
            throw new CliException($"Input file does not exist: {InputPath}.");
        if (!Directory.Exists(TempDirectory))
            throw new CliException($"Temporary directory does not exist: {TempDirectory}.");
        FilePaths.RequireDifferent(InputPath, OutputPath);
        FilePaths.ValidateOutput(OutputPath);
    }
}
