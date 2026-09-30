using LargeFileTools.Generation;
using LargeFileTools.Shared;

namespace LargeFileTools.Tests.Generation;

/// <summary>
/// Checks record distribution, size boundaries, and lazy generation.
/// </summary>
public sealed class RecordSourceTests
{
    /// <summary>
    /// A fractional repetition quota still produces one complete group.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(10, 2)]
    [InlineData(1000, 2)]
    [InlineData(1, 20)]
    [InlineData(1000, 20)]
    public void GuaranteesGroupForTinyTargets(int size, int copies)
    {
        var options = Options() with { SizeBytes = size, RepeatedPercent = 1, CopiesPerText = copies };
        var lines = new RecordSource(options).Generate().ToArray();
        var counts = lines.Select(Text).GroupBy(text => text, StringComparer.InvariantCulture)
            .Select(group => group.Count()).ToArray();
        Assert.True(lines.Length * options.RepeatedPercent / 100.0 < 1);
        Assert.Equal(copies, Assert.Single(counts, count => count > 1));
        Assert.True(lines.Sum(line => RecordFormat.Encoding.GetByteCount(line) + 1L) >= size);
    }

    /// <summary>
    /// The stream ends exactly at a fitting group, or continues when bytes are missing.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void StopsAtGroupAndByteBoundaries(int offset)
    {
        var options = Options() with { SizeBytes = 1, RepeatedPercent = 1 };
        var pair = new RecordSource(options).Generate().ToArray();
        var pairBytes = pair.Sum(line => RecordFormat.Encoding.GetByteCount(line) + 1L);
        var lines = new RecordSource(options with { SizeBytes = pairBytes + offset }).Generate().ToArray();
        Assert.Equal(offset <= 0 ? 2 : 3, lines.Length);
        Assert.Equal(pair, lines.Take(2));
        if (offset > 0)
            Assert.NotEqual(Text(pair[0]), Text(lines[2]));
    }

    /// <summary>
    /// Default-length texts meet the minimum share with only pair-rounding excess.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 20)]
    [InlineData(20, 2)]
    [InlineData(80, 2)]
    [InlineData(80, 20)]
    [InlineData(100, 3)]
    [InlineData(100, 1000)]
    public void ProducesRequestedDistribution(int percent, int copies)
    {
        var options = Options() with { RepeatedPercent = percent, CopiesPerText = copies };
        var counts = new RecordSource(options).Generate().Select(Text)
            .GroupBy(text => text, StringComparer.InvariantCulture)
            .Select(group => group.Count()).ToArray();
        Assert.All(counts, count => Assert.InRange(count, 1, copies));
        Assert.Contains(copies, counts);
        Assert.True(counts.Count(count => count > 1 && count < copies) <= 1);
        var repeated = counts.Where(count => count > 1).Sum();
        Assert.InRange(repeated - counts.Sum() * percent / 100.0, 0, 2);
    }

    /// <summary>
    /// A byte boundary on a new group still emits its second occurrence and meets the minimum share.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 5)]
    [InlineData(20, 2)]
    [InlineData(100, 5)]
    public void FinishesNewGroupPairAtByteBoundary(int percent, int copies)
    {
        var options = Options() with { RepeatedPercent = percent, CopiesPerText = copies };
        var sample = new RecordSource(options).Generate().Take(1000).ToArray();
        var groups = sample.Select((line, index) => (Text: Text(line), Index: index))
            .GroupBy(item => item.Text, StringComparer.Ordinal).Where(group => group.Count() > 1).ToArray();
        var boundaryIndex = groups[1].First().Index;
        var size = sample.Take(boundaryIndex + 1).Sum(line => RecordFormat.Encoding.GetByteCount(line) + 1L);

        var lines = new RecordSource(options with { SizeBytes = size }).Generate().ToArray();
        Assert.Equal(sample.Take(boundaryIndex + 2), lines);
        Assert.Equal(Text(lines[^2]), Text(lines[^1]));
        var repeated = lines.Select(Text).GroupBy(text => text, StringComparer.InvariantCulture)
            .Where(group => group.Count() > 1).Sum(group => group.Count());
        Assert.True(repeated * 100L >= lines.LongLength * percent);
    }

    /// <summary>
    /// Each repeated group finishes before the next begins.
    /// </summary>
    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    public void CompletesOneGroupAtATime(int percent)
    {
        var source = new RecordSource(Options() with { RepeatedPercent = percent, CopiesPerText = 3 });
        var texts = source.Generate().Take(120).Select(Text).ToArray();
        var positions = texts.Select((text, index) => (Text: text, Index: index))
            .GroupBy(item => item.Text, StringComparer.Ordinal).Where(group => group.Count() > 1).ToArray();
        Assert.Equal(120 * percent / 100 / 3, positions.Length);
        Assert.All(positions, group => Assert.Equal(3, group.Count()));
        for (var i = 1; i < positions.Length; i++)
            Assert.True(positions[i - 1].Last().Index < positions[i].First().Index);
        if (percent < 100)
            Assert.Contains(positions, group => group.Last().Index - group.First().Index > 2);
    }

    /// <summary>
    /// Huge sizes and copy counts support partial consumption without materialization.
    /// </summary>
    [Fact]
    public void StreamsHugeFilesAndGroupsLazily()
    {
        var source = new RecordSource(Options() with { SizeBytes = long.MaxValue, CopiesPerText = int.MaxValue });
        var texts = source.Generate().Take(10_000).Select(Text).ToArray();
        Assert.Equal(10_000, texts.Length);
        Assert.Single(texts.Distinct(StringComparer.InvariantCulture));
    }

    /// <summary>
    /// Short random texts may collide across groups and exceed the intended copy count.
    /// </summary>
    [Fact]
    public void AllowsRandomTextCollisions()
    {
        var source = new RecordSource(Options() with { RepeatedPercent = 100, MinTextLength = 1, MaxTextLength = 1 });
        var texts = source.Generate().Take(2000).Select(Text).ToArray();
        Assert.Equal(2000, texts.Length);
        Assert.All(texts, text => Assert.Single(text));
        var groups = texts.GroupBy(text => text, StringComparer.InvariantCulture).ToArray();
        Assert.InRange(groups.Length, 1, 53);
        Assert.Contains(groups, group => group.Count() > 2);
        Assert.All(groups, group => Assert.Equal(0, group.Count() % 2));
    }

    /// <summary>
    /// Every character is a random letter or space within the full configured length range.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 8)]
    [InlineData(16, 128)]
    [InlineData(128, 128)]
    public void RespectsAlphabetAndLengthBounds(int minimum, int maximum)
    {
        var source = new RecordSource(Options() with { MinTextLength = minimum, MaxTextLength = maximum });
        var texts = source.Generate().Take(5000).Select(Text).ToArray();
        Assert.All(texts, text =>
        {
            Assert.InRange(text.Length, minimum, maximum);
            Assert.All(text, character => Assert.True(char.IsAsciiLetter(character) || character == ' '));
        });
        Assert.Contains(texts, text => text.Length == minimum);
        Assert.Contains(texts, text => text.Length == maximum);
    }

    /// <summary>
    /// The size includes numbers, delimiters, and LF, with at most a pair across the byte target.
    /// </summary>
    [Fact]
    public void CountsCompleteEncodedRecords()
    {
        var options = Options() with { SizeBytes = 4096, MinTextLength = 1, MaxTextLength = 25 };
        var lines = new RecordSource(options).Generate().ToArray();
        var bytes = lines.Sum(line => RecordFormat.Encoding.GetByteCount(line) + 1L);
        Assert.InRange(bytes, options.SizeBytes, options.SizeBytes + 2L * (options.MaxTextLength + 12) - 1);
        Assert.True(bytes - lines.TakeLast(2).Sum(line => RecordFormat.Encoding.GetByteCount(line) + 1L) < options.SizeBytes);
        Assert.All(lines, line => Assert.InRange(RecordFormat.Parse(line, "generated", 1).Number, 1, 999_999_999));
    }

    /// <summary>
    /// Every enumeration starts from its seed and has independent repetition state.
    /// </summary>
    [Fact]
    public void ReproducesStreamWithSameSeed()
    {
        var options = Options() with { RepeatedPercent = 80, CopiesPerText = 20 };
        var source = new RecordSource(options);
        var expected = source.Generate().Take(2000).ToArray();
        Assert.Equal(expected, source.Generate().Take(2000));
        Assert.Equal(expected, new RecordSource(options).Generate().Take(2000));
        Assert.False(expected.SequenceEqual(new RecordSource(options with { Seed = 43 }).Generate().Take(2000)));
    }

    /// <summary>
    /// Cancellation interrupts both new and partially consumed streams.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void HonorsCancellation(int consumed)
    {
        using var stop = new CancellationTokenSource();
        using var records = new RecordSource(Options()).Generate(stop.Token).GetEnumerator();
        for (var i = 0; i < consumed; i++)
            Assert.True(records.MoveNext());
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => records.MoveNext());
    }

    /// <summary>
    /// Extracts text using the shared format validator.
    /// </summary>
    private static string Text(string line) => RecordFormat.Parse(line, "generated", 1).Text.ToString();

    /// <summary>
    /// Settings for an in-memory source with no output side effects.
    /// </summary>
    private static GenerateOptions Options() => new() { OutputPath = "unused.txt", SizeBytes = 2 << 20, Seed = 42 };
}
