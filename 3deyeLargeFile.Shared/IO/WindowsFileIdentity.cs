using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace LargeFileTools.Shared;

/// <summary>
/// Compares existing Windows files independently of their path spelling.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsFileIdentity
{
    /// <summary>
    /// Checks the volume and file identifiers of two open files.
    /// </summary>
    public static bool AreSame(string first, string second)
    {
        using var firstHandle = Open(first);
        using var secondHandle = Open(second);
        return Read(firstHandle, first) == Read(secondHandle, second);
    }

    /// <summary>
    /// Opens an existing file without excluding other readers.
    /// </summary>
    private static SafeFileHandle Open(string path) =>
        File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// Reads identity metadata or fails before any output is created.
    /// </summary>
    private static Identity Read(SafeFileHandle handle, string path)
    {
        const int fileIdInfo = 18;
        if (!GetFileInformationByHandleEx(handle, fileIdInfo, out var identity, (uint)Marshal.SizeOf<Identity>()))
            throw new IOException($"Cannot determine file identity: {path}.", new Win32Exception(Marshal.GetLastWin32Error()));
        return identity;
    }

    /// <summary>
    /// Retrieves FILE_ID_INFO from a Windows file handle.
    /// </summary>
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out Identity information, uint bufferSize);

    /// <summary>
    /// Native FILE_ID_INFO layout with a 128-bit file identifier.
    /// </summary>
    /// <param name="Volume">
    /// Volume serial number.
    /// </param>
    /// <param name="Low">
    /// First identifier half.
    /// </param>
    /// <param name="High">
    /// Second identifier half.
    /// </param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Identity(ulong Volume, ulong Low, ulong High);
}
