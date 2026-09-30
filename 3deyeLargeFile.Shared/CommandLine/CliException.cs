namespace LargeFileTools.Shared;

/// <summary>
/// An invalid command-line argument or option combination.
/// </summary>
public sealed class CliException(string message) : Exception(message);
