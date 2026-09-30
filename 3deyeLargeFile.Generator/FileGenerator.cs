using System.Diagnostics;
using LargeFileTools.Shared;

namespace LargeFileTools.Generation;

/// <summary>
/// Streams repeatable test data to a safely published file.
/// </summary>
public sealed class FileGenerator
{
    /// <summary>
    /// Minimum time between progress reports.
    /// </summary>
    private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Generates a file from validated options and returns output measurements.
    /// </summary>
    public GenerationResult Generate(GenerateOptions options, CancellationToken cancellationToken = default,
        Action<string>? report = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        var source = new RecordSource(options);
        using var output = new OutputFile(options.OutputPath, report);
        var bytes = 0L;
        var records = 0L;
        var lastReport = TimeSpan.Zero;
        report?.Invoke("Generating test data.");
        using (var writer = TextFile.CreateWriter(output.StagingPath))
        {
            var shuffled = new ShuffledWriter(writer, new Random(options.Seed), options.ShuffleBufferBytes);
            foreach (var line in source.Generate(cancellationToken))
            {
                shuffled.WriteLine(line, cancellationToken);
                bytes = checked(bytes + line.Length + 1L);
                records++;
                if (timer.Elapsed - lastReport >= ProgressReportInterval)
                {
                    lastReport = timer.Elapsed;
                    report?.Invoke($"Generated {bytes:N0} bytes, {records:N0} records.");
                }
            }
            shuffled.Flush(cancellationToken);
        }
        output.Publish(cancellationToken);
        return new GenerationResult(bytes, records, timer.Elapsed);
    }
}
