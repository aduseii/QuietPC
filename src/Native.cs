using System;
using System.Runtime.InteropServices;

namespace QuietPC
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const int ProcessPowerThrottling = 4;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 1;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PowerThrottlingState info, int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);

        /// <summary>PID of the window you're currently using, or 0.</summary>
        public static int ForegroundPid()
        {
            IntPtr w = GetForegroundWindow();
            if (w == IntPtr.Zero) return 0;
            uint pid;
            GetWindowThreadProcessId(w, out pid);
            return (int)pid;
        }

        /// <summary>Windows power throttling (EcoQoS). on=false hands control back to Windows.</summary>
        public static bool SetEco(int pid, bool on)
        {
            IntPtr h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                var s = new PowerThrottlingState
                {
                    Version = 1,
                    ControlMask = on ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
                    StateMask = on ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
                };
                return SetProcessInformation(h, ProcessPowerThrottling, ref s, Marshal.SizeOf(typeof(PowerThrottlingState)));
            }
            finally { CloseHandle(h); }
        }

        /// <summary>Moves a process's idle memory out of RAM; it pages back in when used.</summary>
        public static bool Trim(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try { return EmptyWorkingSet(h); }
            finally { CloseHandle(h); }
        }
    }
}
