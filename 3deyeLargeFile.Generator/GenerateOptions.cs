using LargeFileTools.Shared;

namespace LargeFileTools.Generation;

/// <summary>
/// Validated generation settings.
/// </summary>
public sealed record GenerateOptions
{
    /// <summary>
    /// Destination file.
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Target bytes, with at least one complete repeated group.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Generation seed, randomly chosen when omitted.
    /// </summary>
    public int Seed { get; init; } = Random.Shared.Next();

    /// <summary>
    /// Minimum percentage of records belonging to repeated groups.
    /// </summary>
    public int RepeatedPercent { get; init; } = 20;

    /// <summary>
    /// Intended occurrences per completed group and minimum output record count.
    /// </summary>
    public int CopiesPerText { get; init; } = 2;

    /// <summary>
    /// Estimated string-storage budget for shuffling.
    /// </summary>
    public long ShuffleBufferBytes { get; init; } = ShuffledWriter.BufferBytes;

    /// <summary>
    /// Minimum generated text length.
    /// </summary>
    public int MinTextLength { get; init; } = 16;

    /// <summary>
    /// Maximum generated text length.
    /// </summary>
    public int MaxTextLength { get; init; } = 128;

    /// <summary>
    /// Parses the generator command line.
    /// </summary>
    public static GenerateOptions Parse(string[] args)
    {
        var cli = new CliArguments(args, "--output", "--size", "--seed", "--repeated-percent",
            "--copies-per-text", "--shuffle-buffer-size", "--min-text-length", "--max-text-length");
        var options = new GenerateOptions
        {
            OutputPath = cli.Required("--output"),
            SizeBytes = cli.Megabytes("--size"),
            Seed = cli.Integer("--seed", Random.Shared.Next()),
            RepeatedPercent = cli.Integer("--repeated-percent", 20, 1),
            CopiesPerText = cli.Integer("--copies-per-text", 2, 2),
            ShuffleBufferBytes = cli.Megabytes("--shuffle-buffer-size", 8),
            MinTextLength = cli.Integer("--min-text-length", 16, 1),
            MaxTextLength = cli.Integer("--max-text-length", 128, 1)
        };
        options.Validate();
        return options;
    }

    /// <summary>
    /// Checks the output path, repeat settings, and text bounds.
    /// </summary>
    public void Validate()
    {
        if (SizeBytes <= 0)
            throw new CliException("--size must be positive.");
        if (RepeatedPercent is < 1 or > 100)
            throw new CliException("--repeated-percent must be from 1 to 100.");
        if (CopiesPerText < 2)
            throw new CliException("--copies-per-text must be at least 2.");
        if (ShuffleBufferBytes <= 0)
            throw new CliException("--shuffle-buffer-size must be positive.");
        if (MinTextLength < 1 || MaxTextLength < MinTextLength)
            throw new CliException("Require 1 <= min-text-length <= max-text-length.");

        // Reserve the shuffle strings and their reference array alongside text construction.
        var textBytes = 5L * (40L + 2L * MaxTextLength);
        if (ShuffleBufferBytes > (64L * 1024 * 1024 - textBytes) / 2)
            throw new CliException("Text generation exceeds its 64 MB memory limit; reduce --shuffle-buffer-size or --max-text-length.");
        FilePaths.ValidateOutput(OutputPath);
    }
}
