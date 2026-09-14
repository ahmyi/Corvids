using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Corvids.Services;

/// <summary>Reads another process's current working directory. Returns null when it cannot be determined.</summary>
public static class ProcessCwd
{
    public static string? TryGet(int pid)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows(pid);
            if (OperatingSystem.IsLinux()) return File.ResolveLinkTarget($"/proc/{pid}/cwd", true)?.FullName;
            if (OperatingSystem.IsMacOS()) return ReadMac(pid);
        }
        catch
        {
            // access denied, process gone, 32-bit target, etc.
        }

        return null;
    }

    // Windows: PEB -> RTL_USER_PROCESS_PARAMETERS -> CurrentDirectory.DosPath (x64 layout only).

    private const int ProcessQueryInformation = 0x0400;
    private const int ProcessVmRead = 0x0010;
    private const int PebProcessParametersOffset = 0x20;
    private const int ParamsCurrentDirectoryOffset = 0x38;

    private static string? ReadWindows(int pid)
    {
        if (IntPtr.Size != 8) return null;

        var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero) return null;

        try
        {
            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0)
                return null;

            var parameters = ReadPointer(handle, info.PebBaseAddress + PebProcessParametersOffset);
            if (parameters == IntPtr.Zero) return null;

            var dosPath = parameters + ParamsCurrentDirectoryOffset;
            var length = ReadUInt16(handle, dosPath);
            var buffer = ReadPointer(handle, dosPath + 8);
            if (length == 0 || buffer == IntPtr.Zero) return null;

            var bytes = new byte[length];
            if (!ReadProcessMemory(handle, buffer, bytes, bytes.Length, out var read) || read != bytes.Length)
                return null;

            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static IntPtr ReadPointer(IntPtr process, IntPtr address)
    {
        var bytes = new byte[8];
        return ReadProcessMemory(process, address, bytes, 8, out _) ? (IntPtr)BitConverter.ToInt64(bytes) : IntPtr.Zero;
    }

    private static ushort ReadUInt16(IntPtr process, IntPtr address)
    {
        var bytes = new byte[2];
        return ReadProcessMemory(process, address, bytes, 2, out _) ? BitConverter.ToUInt16(bytes) : (ushort)0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2A;
        public IntPtr Reserved2B;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass,
        ref ProcessBasicInformation info, int infoLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size,
        out int bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    // macOS: lsof prints "n<path>" for the cwd descriptor.

    private static string? ReadMac(int pid)
    {
        var psi = new ProcessStartInfo("lsof") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-a", "-p", pid.ToString(), "-d", "cwd", "-Fn" }) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output.Split('\n').FirstOrDefault(l => l.StartsWith('n'))?[1..].Trim();
    }
}
