using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

namespace LargeFileTools.Verification;

/// <summary>
/// Independently checks benchmark output without loading whole files.
/// </summary>
public static class Program
{
    /// <summary>
    /// Checks ordering, record count, and a multiset fingerprint.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: 3deyeLargeFile.Verifier <input-file> <sorted-file>");
            return 2;
        }
        try
        {
            var timer = Stopwatch.StartNew();
            using var source = OpenReader(args[0]);
            using var sorted = OpenReader(args[1]);
            var count = await VerifyAsync(source, args[0], sorted, args[1]);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Verified {count} records: sorted output and matching SHA-256 multiset fingerprint in {timer.Elapsed.TotalSeconds:F3} s."));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Checks caller-owned readers, cancelling both scans after the first failure.
    /// </summary>
    internal static async Task<long> VerifyAsync(TextReader sourceReader, string sourcePath,
        TextReader sortedReader, string sortedPath, CancellationToken cancellationToken = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ExceptionDispatchInfo? firstError = null;
        var fail = (Exception error) =>
        {
            Interlocked.CompareExchange(ref firstError, ExceptionDispatchInfo.Capture(error), null);
            stop.Cancel();
        };

        var source = StartScan(sourceReader, sourcePath, false, stop.Token, fail);
        var sorted = StartScan(sortedReader, sortedPath, true, stop.Token, fail);
        await Task.WhenAll(source, sorted);
        firstError?.Throw();
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Result != sorted.Result)
            throw new InvalidDataException("Record count or multiset fingerprint differs.");
        return source.Result.Count;
    }

    /// <summary>
    /// Opens a file before either background scan starts.
    /// </summary>
    private static StreamReader OpenReader(string path) =>
        new(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

    /// <summary>
    /// Captures a scan failure before cancelling its peer.
    /// </summary>
    private static Task<Fingerprint> StartScan(TextReader reader, string path, bool requireSorted,
        CancellationToken cancellationToken, Action<Exception> fail) => Task.Run(() =>
    {
        try
        {
            return Scan(reader, path, requireSorted, cancellationToken);
        }
        catch (Exception ex)
        {
            fail(ex);
            return default;
        }
    });

    /// <summary>
    /// Uses an independent reader and parser to scan logical records.
    /// </summary>
    private static Fingerprint Scan(TextReader reader, string path, bool requireSorted, CancellationToken cancellationToken)
    {
        var encoding = new UTF8Encoding(false, true);
        var encoded = new byte[1024];
        Span<byte> digest = stackalloc byte[32];
        string? previousLine = null;
        var previousOffset = 0;
        var previousNumber = 0L;
        var count = 0L;
        var a = 0UL;
        var b = 0UL;
        var c = 0UL;
        var d = 0UL;
        var progress = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = reader.ReadLine();
            if (line is null)
                break;
            var separator = line.IndexOf(". ", StringComparison.Ordinal);
            if (separator <= 0)
                throw new InvalidDataException($"{path}: invalid record {count + 1}.");
            var digits = line.AsSpan(0, separator);
            if (digits.ContainsAnyExceptInRange('0', '9') ||
                !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
                throw new InvalidDataException($"{path}: invalid number at record {count + 1}.");
            if (requireSorted && previousLine is not null)
            {
                var order = line.AsSpan(separator + 2).CompareTo(previousLine.AsSpan(previousOffset), StringComparison.InvariantCulture);
                if (order < 0 || (order == 0 && number < previousNumber))
                    throw new InvalidDataException($"{path}: out of order at record {count + 1}.");
            }

            var bytes = encoding.GetByteCount(line);
            if (encoded.Length < bytes)
                Array.Resize(ref encoded, bytes);
            encoding.GetBytes(line, encoded);
            SHA256.HashData(encoded.AsSpan(0, bytes), digest);
            // Modular sums are independent of record order and retain multiplicity.
            unchecked
            {
                a += BinaryPrimitives.ReadUInt64LittleEndian(digest);
                b += BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]);
                c += BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]);
                d += BinaryPrimitives.ReadUInt64LittleEndian(digest[24..]);
            }
            count++;
            if (requireSorted)
            {
                previousLine = line;
                previousOffset = separator + 2;
                previousNumber = number;
            }
            if ((count & 1_048_575) == 0 && progress.Elapsed >= TimeSpan.FromSeconds(5))
            {
                Console.Error.WriteLine($"{Path.GetFileName(path)}: checked {count:N0} records.");
                progress.Restart();
            }
        }
        return new Fingerprint(count, a, b, c, d);
    }

    /// <summary>
    /// Order-independent evidence of record preservation, not a mathematical proof.
    /// </summary>
    /// <param name="Count">
    /// Record count.
    /// </param>
    /// <param name="A">
    /// First modular digest sum.
    /// </param>
    /// <param name="B">
    /// Second modular digest sum.
    /// </param>
    /// <param name="C">
    /// Third modular digest sum.
    /// </param>
    /// <param name="D">
    /// Fourth modular digest sum.
    /// </param>
    private readonly record struct Fingerprint(long Count, ulong A, ulong B, ulong C, ulong D);
}
