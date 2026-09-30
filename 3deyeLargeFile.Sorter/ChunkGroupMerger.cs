using LargeFileTools.Shared;

namespace LargeFileTools.Sorting;

/// <summary>
/// Merges one bounded group of sorted files.
/// </summary>
public sealed class ChunkGroupMerger
{
    /// <summary>
    /// Writes a merged file and returns its record count.
    /// </summary>
    public long Merge(IReadOnlyList<string> inputPaths, string outputPath, int maxLineBytes,
        CancellationToken cancellationToken)
    {
        var readers = new List<RecordReader>(inputPaths.Count);
        var queue = new PriorityQueue<int, TextRecord>(inputPaths.Count, RecordComparer.Instance);
        try
        {
            for (var i = 0; i < inputPaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reader = new RecordReader(inputPaths[i], maxLineBytes);
                readers.Add(reader);
                var record = reader.Read(cancellationToken);
                if (record is not null)
                    queue.Enqueue(i, record.Value);
            }

            var records = 0L;
            using var writer = TextFile.CreateWriter(outputPath);
            while (queue.TryDequeue(out var index, out var record))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteLine(record.Line);
                records++;
                var next = readers[index].Read(cancellationToken);
                if (next is not null)
                    queue.Enqueue(index, next.Value);
            }
            return records;
        }
        finally
        {
            foreach (var reader in readers)
                reader.Dispose();
        }
    }
}
