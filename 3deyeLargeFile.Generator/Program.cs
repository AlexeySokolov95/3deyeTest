using System.Globalization;
using LargeFileTools.Shared;

namespace LargeFileTools.Generation;

/// <summary>
/// Generator command-line entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Generator usage and defaults.
    /// </summary>
    private const string Help = """
        Generate a UTF-8 test file with positive integers and repeated text values.
        Usage: 3deyeLargeFile.Generator --output <path> --size <MB> [options]

          --seed <int>                Reproducible seed (random if omitted; printed on completion)
          --repeated-percent <int>    Minimum share of records in repeated groups (20; 1-100)
          --copies-per-text <int>     Intended occurrences per group, including the first (2; >= 2)
          --shuffle-buffer-size <MB>  Memory budget for records awaiting shuffling (8 MB)
          --min-text-length <int>     Minimum text length (16)
          --max-text-length <int>     Maximum text length (128)
          --help                     Show this help

        Sizes are positive whole numbers of MB (1 MB = 1024 * 1024 bytes), without suffixes.
        Output reaches the target size and contains at least one complete repeated group.
        A group larger than the target takes priority; otherwise rounding adds at most two records.
        Parent directories must exist.
        The output file must not already exist.
        The final group may be incomplete, but always contains at least two occurrences.
        Random text collisions are allowed and can increase repetitions beyond these settings.
        Records are shuffled within each buffer; repeated text is guaranteed.
        Estimated generation storage must fit 64 MB; runtime overhead is additional.
        Texts contain only random ASCII letters and spaces.
        """;

    /// <summary>
    /// Runs generation with shared CLI error handling.
    /// </summary>
    public static Task<int> Main(string[] args) => CliHost.RunAsync(args, Help, (token, report) =>
    {
        var options = GenerateOptions.Parse(args);
        var result = new FileGenerator().Generate(options, token, report);
        return Task.FromResult(string.Create(CultureInfo.InvariantCulture,
            $"Generated {result.Records} records, {result.Bytes} bytes in {result.Elapsed.TotalSeconds:F3} s. Seed: {options.Seed}."));
    });
}
