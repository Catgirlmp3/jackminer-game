using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace JackMiner;

/// <summary>
/// Owns the optional resource-penalty process and its descendants for the
/// lifetime of the application. Closing the Job Object terminates the group.
/// </summary>
public sealed class PenaltyProcessHost : IDisposable
{
    private readonly object _sync = new();
    private SafeJobHandle? _job;
    private Process? _process;
    private bool _disposed;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _process is { HasExited: false };
        }
    }

    public event Action<string>? OutputReceived;
    public event Action<int?>? Exited;

    /// <summary>
    /// Starts an executable that has already passed package and binary integrity checks.
    /// Every argument is supplied separately to avoid command-line quoting ambiguities.
    /// </summary>
    public void StartVerified(string executablePath, IReadOnlyList<string> arguments, string workingDirectory, int cpuLimitPercent)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is { HasExited: false })
                throw new InvalidOperationException("The resource process is already running.");
            if (!Path.IsPathFullyQualified(executablePath) || !File.Exists(executablePath))
                throw new FileNotFoundException("The verified executable was not found.", executablePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Directory.Exists(workingDirectory)
                    ? workingDirectory
                    : Path.GetDirectoryName(executablePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            var job = CreateKillOnCloseJob();
            SetCpuRate(job, cpuLimitPercent);
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
            process.Exited += (_, _) =>
            {
                int? code = null;
                try { code = process.ExitCode; } catch (InvalidOperationException) { }
                Exited?.Invoke(code);
            };

            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("Windows did not start the resource process.");
                if (!AssignProcessToJobObject(job, process.Handle))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Could not attach the resource process to JackMiner's process group.");

                _job?.Dispose();
                _job = job;
                _process?.Dispose();
                _process = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                process.Dispose();
                job.Dispose();
                throw;
            }
        }
    }

    /// <summary>Applies a Windows Job Object hard CPU-rate cap to the complete child process group.</summary>
    public void SetCpuLimit(int percentage)
    {
        lock (_sync)
        {
            if (_job is null || _job.IsInvalid || _job.IsClosed)
                throw new InvalidOperationException("The resource process group is not active.");
            SetCpuRate(_job, percentage);
        }
    }

    private static void SetCpuRate(SafeJobHandle job, int percentage)
    {
        if (percentage is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentage), "CPU cap must be between 1% and 100%.");
        var info = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = 0x00000001 | 0x00000004,
            CpuRate = checked((uint)(percentage * 100))
        };
        var size = Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (!SetInformationJobObject(job, 15, pointer, (uint)size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not apply the requested hard CPU cap.");
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _job?.Dispose();
            _job = null;
            if (_process is not { HasExited: false } process) return;
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _job?.Dispose();
                _job = null;
                if (_process is { HasExited: false } process)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    try { process.WaitForExit(5000); } catch { }
                }
            }
            finally
            {
                _process?.Dispose();
                _process = null;
            }
        }
    }

    private static SafeJobHandle CreateKillOnCloseJob()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the process group.");

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = 0x00002000 }
        };
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (!SetInformationJobObject(job, 9, pointer, (uint)size))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not configure automatic child-process cleanup.");
            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle job, int infoClass, IntPtr jobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(ownsHandle: true) { }
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
    private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
