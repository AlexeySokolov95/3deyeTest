using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Owns one chunk-building operation and its reader/worker pipeline.
/// </summary>
public sealed class SortedChunkBuilder : IDisposable
{
    /// <summary>
    /// Portions that may wait between the reader and workers.
    /// </summary>
    private const int QueueCapacity = 1;

    /// <summary>
    /// Validated source and worker settings.
    /// </summary>
    private readonly SortOptions options;

    /// <summary>
    /// Caller-owned temporary storage.
    /// </summary>
    private readonly TempWorkspace workspace;

    /// <summary>
    /// Caller-requested cancellation.
    /// </summary>
    private readonly CancellationToken cancellationToken;

    /// <summary>
    /// Receives progress and skipped-record diagnostics.
    /// </summary>
    private readonly Action<string>? report;

    /// <summary>
    /// Serializes diagnostics within this operation.
    /// </summary>
    private readonly object reportLock = new();

    /// <summary>
    /// Holds bounded portions awaiting workers.
    /// </summary>
    private readonly Channel<RecordBatch> channel;

    /// <summary>
    /// Stops the reader and workers on cancellation or failure.
    /// </summary>
    private readonly CancellationTokenSource stop;

    /// <summary>
    /// Preserves the first failure and its original stack.
    /// </summary>
    private ExceptionDispatchInfo? firstError;

    /// <summary>
    /// Allocates contiguous identifiers to nonempty chunks.
    /// </summary>
    private long chunkCount;

    /// <summary>
    /// Prevents starting this operation more than once.
    /// </summary>
    private int started;

    /// <summary>
    /// Creates independent queue, cancellation, and counters for this build.
    /// </summary>
    public SortedChunkBuilder(SortOptions options, TempWorkspace workspace,
        CancellationToken cancellationToken, Action<string>? report = null)
    {
        this.options = options;
        this.workspace = workspace;
        this.cancellationToken = cancellationToken;
        this.report = report;
        channel = Channel.CreateBounded<RecordBatch>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = options.Workers == 1
        });
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    /// <summary>
    /// Starts this operation once and waits for all reader and worker tasks.
    /// </summary>
    public async Task<ChunkBuildResult> BuildAsync()
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
            throw new InvalidOperationException("A chunk builder can only be started once.");

        var reader = Task.Run(ReadChunksAsync, CancellationToken.None);
        var workers = Enumerable.Range(0, options.Workers)
            .Select(_ => Task.Run(ProcessChunksAsync, CancellationToken.None))
            .ToArray();

        await Task.WhenAll(workers.Cast<Task>().Append(reader));
        firstError?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        return new ChunkBuildResult(chunkCount, workers.Sum(worker => worker.Result.Records),
            reader.Result + workers.Sum(worker => worker.Result.Skipped));
    }

    /// <summary>
    /// Reads bounded portions and closes the queue when reading ends.
    /// </summary>
    private async Task<long> ReadChunksAsync()
    {
        var token = stop.Token;
        try
        {
            using var reader = new LineReader(options.InputPath, options.MaxLineBytes,
                error => Report($"Skipped {error.Message}"));
            var pending = reader.Read(token);
            var progressTimer = Stopwatch.StartNew();
            var portions = 0L;
            while (pending)
            {
                token.ThrowIfCancellationRequested();
                var batch = new RecordBatch(options.ChunkLines);
                while (pending && batch.TryAdd(reader.Line, reader.LineNumber))
                {
                    pending = reader.Read(token);
                    if (progressTimer.Elapsed >= TimeSpan.FromSeconds(2))
                    {
                        Report($"Read {reader.LineNumber:N0} lines; submitted {portions:N0} portions.");
                        progressTimer.Restart();
                    }
                }

                await channel.Writer.WriteAsync(batch, token);
                portions++;
            }
            return reader.SkippedLines;
        }
        catch (Exception error)
        {
            Fail(error);
            return 0L;
        }
        finally
        {
            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Parses queued portions and writes their sorted valid records.
    /// </summary>
    private async Task<(long Records, long Skipped)> ProcessChunksAsync()
    {
        var token = stop.Token;
        var records = 0L;
        var skipped = 0L;
        try
        {
            while (await channel.Reader.WaitToReadAsync(token))
            {
                while (channel.Reader.TryRead(out var batch))
                {
                    token.ThrowIfCancellationRequested();
                    skipped += batch.Parse(options.InputPath, token, report is null ? null : Report);
                    if (batch.Count > 0)
                    {
                        var id = Interlocked.Increment(ref chunkCount) - 1;
                        SortAndWriteChunk(batch, id);
                        records += batch.Count;
                    }
                }
            }
        }
        catch (Exception error)
        {
            Fail(error);
        }
        return (records, skipped);
    }

    /// <summary>
    /// Sorts one portion and writes it as a temporary chunk.
    /// </summary>
    private void SortAndWriteChunk(RecordBatch batch, long id)
    {
        var token = stop.Token;
        Array.Sort(batch.Records, 0, batch.Count, RecordComparer.Instance);
        token.ThrowIfCancellationRequested();
        var path = workspace.ChunkPath(0, id);
        using (var writer = TextFile.CreateWriter(path))
        {
            for (var i = 0; i < batch.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                writer.WriteLine(batch.Records[i].Line);
            }
        }
        workspace.TrackChunk(path);
    }

    /// <summary>
    /// Preserves the first failure and stops all pipeline tasks.
    /// </summary>
    private void Fail(Exception error)
    {
        Interlocked.CompareExchange(ref firstError, ExceptionDispatchInfo.Capture(error), null);
        stop.Cancel();
        channel.Writer.TryComplete();
    }

    /// <summary>
    /// Serializes diagnostics from the reader and workers.
    /// </summary>
    private void Report(string message)
    {
        if (report is null)
            return;
        lock (reportLock)
            report(message);
    }

    /// <summary>
    /// Releases cancellation resources after all tasks have stopped.
    /// </summary>
    public void Dispose() => stop.Dispose();
}
