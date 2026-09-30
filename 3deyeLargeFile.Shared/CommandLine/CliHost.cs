namespace LargeFileTools.Shared;

/// <summary>
/// Handles cancellation, diagnostics, and process exit codes.
/// </summary>
public static class CliHost
{
    /// <summary>
    /// Runs a command with shared failure behavior.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, string help,
        Func<CancellationToken, Action<string>, Task<string>> execute)
    {
        if (args is ["--help"])
        {
            Console.WriteLine(help);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        var handler = new ConsoleCancelEventHandler((_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        });
        Console.CancelKeyPress += handler;
        try
        {
            var summary = await execute(cancellation.Token, Console.Error.WriteLine);
            Console.WriteLine(summary);
            return 0;
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (RecordDataException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}
