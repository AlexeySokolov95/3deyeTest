namespace LargeFileTools.Tests;

/// <summary>
/// Isolates test files in an owned temporary directory.
/// </summary>
public sealed class TestDirectory : IDisposable
{
    /// <summary>
    /// Creates the test directory.
    /// </summary>
    public TestDirectory() => Directory.CreateDirectory(Root);

    /// <summary>
    /// Owned temporary root.
    /// </summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"3deye-tests-{Guid.NewGuid():N}");

    /// <summary>
    /// Builds a file path within the test root.
    /// </summary>
    public string FilePath(string name) => Path.Combine(Root, name);

    /// <summary>
    /// Asserts that operation-owned temporary files were removed.
    /// </summary>
    public void AssertClean()
    {
        Assert.Empty(Directory.GetDirectories(Root, "3deye-sort-*"));
        Assert.Empty(Directory.GetFiles(Root, ".3deye-*.tmp"));
    }

    /// <summary>
    /// Deletes only this test's files.
    /// </summary>
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
