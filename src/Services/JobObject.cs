using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Corvids.Services;

/// <summary>
/// Windows Job Object that owns a process and everything it spawns. Terminating the job kills the whole tree,
/// including grandchildren that re-parented themselves (Git Bash / MSYS exec emulation does this), which
/// Process.Kill(entireProcessTree) cannot follow. Returns null on other platforms.
/// </summary>
public sealed class JobObject : IDisposable
{
    private const uint LimitKillOnJobClose = 0x2000;
    private const int ExtendedLimitInformationClass = 9;

    private IntPtr _handle;

    private JobObject(IntPtr handle) => _handle = handle;

    public static JobObject? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) return null;

        var info = new ExtendedLimitInformation
        {
            BasicLimitInformation = { LimitFlags = LimitKillOnJobClose },
        };
        if (!SetInformationJobObject(handle, ExtendedLimitInformationClass, ref info,
                (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            CloseHandle(handle);
            return null;
        }

        return new JobObject(handle);
    }

    public bool Assign(Process process) =>
        _handle != IntPtr.Zero && AssignProcessToJobObject(_handle, process.Handle);

    private const int JobObjectBasicProcessIdList = 3;

    /// <summary>PIDs currently in the job (the app process and everything it spawned).</summary>
    public IReadOnlyList<int> GetProcessIds()
    {
        var pids = new List<int>();
        if (_handle == IntPtr.Zero) return pids;

        const int capacity = 1024;
        var size = 8 + IntPtr.Size * capacity; // two uints + ULONG_PTR[capacity]
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (QueryInformationJobObject(_handle, JobObjectBasicProcessIdList, buffer, size, out _))
            {
                var count = Marshal.ReadInt32(buffer, 4); // NumberOfProcessIdsInList
                for (var i = 0; i < count && i < capacity; i++)
                    pids.Add((int)Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size));
            }
        }
        catch
        {
            // best effort
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pids;
    }

    public void Terminate()
    {
        if (_handle != IntPtr.Zero) TerminateJobObject(_handle, 1);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        CloseHandle(_handle); // KILL_ON_JOB_CLOSE also reaps anything still alive
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass,
        ref ExtendedLimitInformation info, uint infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, int length,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
