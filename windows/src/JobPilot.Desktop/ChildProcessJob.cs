using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace JobPilot.Desktop;

/// <summary>
/// A Windows job object that kills the server when the window's process ends for any reason (including a crash or
/// "End task"), so no orphan server keeps the port and the database open.
/// </summary>
internal sealed class ChildProcessJob : IDisposable
{
    private readonly SafeJobHandle _job;

    public ChildProcessJob()
    {
        _job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (_job.IsInvalid)
        {
            throw new InvalidOperationException($"CreateJobObject failed ({Marshal.GetLastPInvokeError()}).");
        }

        var limits = new NativeMethods.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation { LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose },
        };
        var size = Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);
            if (!NativeMethods.SetInformationJobObject(_job, NativeMethods.JobObjectExtendedLimitInformationClass, buffer, (uint)size))
            {
                throw new InvalidOperationException($"SetInformationJobObject failed ({Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Add(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        // Best effort: if it fails, the server is still stopped when the window closes normally.
        NativeMethods.AssignProcessToJobObject(_job, process.SafeHandle);
    }

    public void Dispose() => _job.Dispose();

    internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private static class NativeMethods
    {
        public const int JobObjectExtendedLimitInformationClass = 9;
        public const uint JobObjectLimitKillOnJobClose = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        public struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeJobHandle CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(SafeJobHandle job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
