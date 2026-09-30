namespace LargeFileTools.Shared;

/// <summary>
/// Validates paths before any output is created.
/// </summary>
public static class FilePaths
{
    /// <summary>
    /// Validates an output path and its parent directory.
    /// </summary>
    public static string ValidateOutput(string path)
    {
        var fullPath = FullPath(path);
        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
            throw new CliException($"Output directory does not exist: {Path.GetDirectoryName(fullPath)}.");
        if (Directory.Exists(fullPath))
            throw new CliException($"Output is a directory: {fullPath}.");
        if (File.Exists(fullPath))
            throw new CliException($"Output file already exists: {fullPath}.");
        return fullPath;
    }

    /// <summary>
    /// Resolves existing symbolic links in every path component.
    /// </summary>
    public static string ResolveLinks(string path)
    {
        var fullPath = FullPath(path);
        var current = Path.GetPathRoot(fullPath)!;
        foreach (var component in fullPath[current.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
                current = info.ResolveLinkTarget(true)!.FullName;
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>
    /// Rejects an input path reused as the destination.
    /// </summary>
    public static void RequireDifferent(string input, string output)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(ResolveLinks(input), ResolveLinks(output), comparison) ||
            (OperatingSystem.IsWindows() && File.Exists(input) && File.Exists(output) &&
             WindowsFileIdentity.AreSame(input, output)))
            throw new CliException("Input and output must be different files.");
    }

    /// <summary>
    /// Converts malformed path arguments into CLI diagnostics.
    /// </summary>
    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CliException($"Invalid path: {ex.Message}");
        }
    }
}
