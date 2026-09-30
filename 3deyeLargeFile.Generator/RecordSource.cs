using LargeFileTools.Shared;

namespace LargeFileTools.Generation;

/// <summary>
/// Generates a bounded-memory record stream with at least one repeated group.
/// </summary>
public sealed class RecordSource
{
    /// <summary>
    /// Characters available for random texts.
    /// </summary>
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz ";

    /// <summary>
    /// Validated generation settings.
    /// </summary>
    private readonly GenerateOptions options;

    /// <summary>
    /// Stores settings without generating records.
    /// </summary>
    public RecordSource(GenerateOptions options)
    {
        this.options = options;
    }

    /// <summary>
    /// Streams records until the byte target, minimum group, and repeated share are satisfied.
    /// </summary>
    public IEnumerable<string> Generate(CancellationToken cancellationToken = default)
    {
        var random = new Random(options.Seed);
        var bytes = 0L;
        var records = 0L;
        var repeatedRecords = 0L;
        var repeatedText = string.Empty;
        var remainingCopiesInCurrentGroup = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentGroupNeedsSecondOccurrence = remainingCopiesInCurrentGroup == options.CopiesPerText - 1;
            if (bytes >= options.SizeBytes && repeatedRecords >= options.CopiesPerText && !currentGroupNeedsSecondOccurrence)
                yield break;

            // One complete group is the minimum quota, even for a tiny target or percentage.
            var useRepeatedGroup = repeatedRecords < options.CopiesPerText || currentGroupNeedsSecondOccurrence ||
                repeatedRecords * 100 < (records + 1) * options.RepeatedPercent;
            string text;
            if (useRepeatedGroup)
            {
                if (remainingCopiesInCurrentGroup == 0)
                {
                    //Create next group
                    repeatedText = CreateText(random, cancellationToken);
                    remainingCopiesInCurrentGroup = options.CopiesPerText;
                }
                text = repeatedText;
                remainingCopiesInCurrentGroup--;
                repeatedRecords++;
            }
            else
                text = CreateText(random, cancellationToken);

            var record = RecordFormat.Format(random.Next(1, 1_000_000_000), text);
            // Generated records are ASCII; the output LF adds one byte.
            bytes = checked(bytes + record.Length + 1L);
            records++;
            yield return record;
        }
    }

    /// <summary>
    /// Creates random ASCII text within the configured length bounds.
    /// </summary>
    private string CreateText(Random random, CancellationToken cancellationToken)
    {
        var length = random.Next(options.MinTextLength, options.MaxTextLength + 1);
        var characters = new char[length];
        for (var i = 0; i < length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            characters[i] = Alphabet[random.Next(Alphabet.Length)];
        }
        return new string(characters);
    }
}
