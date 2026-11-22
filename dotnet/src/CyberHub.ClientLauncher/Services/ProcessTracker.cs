using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using CyberHub.Shared.Services;

namespace CyberHub.ClientLauncher.Services;

public sealed class WindowsProcessTracker : IProcessTracker
{
    private const uint Th32csSnapProcess = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public bool FileExists(string path) => File.Exists(path);

    public TrackedProcessHandle? StartProcess(ProcessStartInfo startInfo)
    {
        using Process? proc = Process.Start(startInfo);
        if (proc is null)
        {
            return null;
        }

        return new TrackedProcessHandle(proc.Id, proc.ProcessName);
    }

    public IReadOnlyList<int> GetChildProcessIds(int parentPid)
    {
        var children = new List<int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return children;
        }

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return children;
            }

            do
            {
                if ((int)entry.th32ParentProcessID == parentPid && entry.th32ProcessID != 0)
                {
                    children.Add((int)entry.th32ProcessID);
                }
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return children;
    }

    public bool IsProcessRunning(int pid)
    {
        try
        {
            using Process proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public IReadOnlyList<int> GetProcessesByName(string processName)
    {
        string clean = Path.GetFileNameWithoutExtension(processName.Trim());
        if (string.IsNullOrWhiteSpace(clean))
        {
            return Array.Empty<int>();
        }

        Process[] matches = Process.GetProcessesByName(clean);
        try
        {
            return matches.Where(p => !p.HasExited).Select(p => p.Id).ToList();
        }
        finally
        {
            foreach (Process p in matches)
            {
                p.Dispose();
            }
        }
    }
}
