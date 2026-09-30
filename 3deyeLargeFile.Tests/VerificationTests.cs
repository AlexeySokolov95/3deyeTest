using VerifierProgram = LargeFileTools.Verification.Program;

namespace LargeFileTools.Tests;

/// <summary>
/// Checks coordinated cancellation without large files or throughput assumptions.
/// </summary>
public sealed class VerificationTests
{
    /// <summary>
    /// The independent verifier follows invariant ordering, including numeric ties.
    /// </summary>
    [Fact]
    public async Task AcceptsInvariantOrder()
    {
        using var source = new StringReader("3. Z\n2. A\n1. a\n2. é\n1. e\u0301\n");
        using var sorted = new StringReader("1. a\n2. A\n1. e\u0301\n2. é\n3. Z\n");
        Assert.Equal(5, await VerifierProgram.VerifyAsync(source, "input", sorted, "output"));
    }

    /// <summary>
    /// An ordinally sorted file can violate invariant ordering.
    /// </summary>
    [Fact]
    public async Task RejectsOrdinalOrder()
    {
        using var source = new StringReader("1. a\n2. A\n");
        using var sorted = new StringReader("2. A\n1. a\n");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            VerifierProgram.VerifyAsync(source, "input", sorted, "output"));
        Assert.Contains("out of order", error.Message);
    }

    /// <summary>
    /// Benchmark verification requires valid positive integers.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("9223372036854775808")]
    public async Task RejectsInvalidNumbers(string number)
    {
        using var source = new StringReader($"{number}. a\n");
        using var sorted = new StringReader("1. a\n");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            VerifierProgram.VerifyAsync(source, "input", sorted, "output"));
        Assert.Equal("input: invalid number at record 1.", error.Message);
    }

    /// <summary>
    /// A failure in either scan stops its peer and retains the original exception.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelsPeerAfterReadFailure(bool failSource)
    {
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var peerStarted = new ManualResetEventSlim();
        using var peer = new RepeatingReader(peerStarted, watchdog.Token);
        var failure = new IOException("Injected read failure.");
        using var broken = new FailingReader(peerStarted, watchdog.Token, failure);

        var actual = await Assert.ThrowsAsync<IOException>(() => VerifierProgram.VerifyAsync(
            failSource ? broken : peer, "input", failSource ? peer : broken, "output", watchdog.Token));

        Assert.Same(failure, actual);
        Assert.False(watchdog.IsCancellationRequested, "The peer scan required watchdog cancellation.");
        Assert.True(peer.RecordsRead > 0);
    }

    /// <summary>
    /// Parsing and ordering errors cancel an already running peer.
    /// </summary>
    [Theory]
    [InlineData(true, "bad\n", "input: invalid record 1.")]
    [InlineData(false, "2. Z\n1. A\n", "output: out of order at record 2.")]
    public async Task CancelsPeerAfterDataFailure(bool failSource, string text, string expectedError)
    {
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var peerStarted = new ManualResetEventSlim();
        using var peer = new RepeatingReader(peerStarted, watchdog.Token);
        using var broken = new GatedReader(text, peerStarted, watchdog.Token);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => VerifierProgram.VerifyAsync(
            failSource ? broken : peer, "input", failSource ? peer : broken, "output", watchdog.Token));

        Assert.Equal(expectedError, error.Message);
        Assert.False(watchdog.IsCancellationRequested, "The peer scan required watchdog cancellation.");
        Assert.True(peer.RecordsRead > 0);
    }

    /// <summary>
    /// Produces records until cancellation, with a watchdog against broken implementations.
    /// </summary>
    private sealed class RepeatingReader(ManualResetEventSlim started, CancellationToken watchdog) : TextReader
    {
        /// <summary>
        /// Records consumed before verification finished.
        /// </summary>
        public long RecordsRead { get; private set; }

        /// <summary>
        /// Signals that the peer is active before returning each valid record.
        /// </summary>
        public override string ReadLine()
        {
            watchdog.ThrowIfCancellationRequested();
            RecordsRead++;
            started.Set();
            return "1. valid";
        }
    }

    /// <summary>
    /// Fails only after its peer has begun reading.
    /// </summary>
    private sealed class FailingReader(ManualResetEventSlim peerStarted, CancellationToken watchdog, IOException failure) : TextReader
    {
        /// <summary>
        /// Injects the original I/O exception after both scans start.
        /// </summary>
        public override string ReadLine()
        {
            peerStarted.Wait(watchdog);
            throw failure;
        }
    }

    /// <summary>
    /// Defers invalid test data until its peer has begun reading.
    /// </summary>
    private sealed class GatedReader(string text, ManualResetEventSlim peerStarted, CancellationToken watchdog) : StringReader(text)
    {
        /// <summary>
        /// Reads a test record after both scans start.
        /// </summary>
        public override string? ReadLine()
        {
            peerStarted.Wait(watchdog);
            return base.ReadLine();
        }
    }
}
