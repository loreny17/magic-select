using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace MagicSelect
{
    static class Native
    {
        public const int WH_MOUSE_LL = 14;
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;

        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int CS_DROPSHADOW = 0x00020000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int retLen);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        public static void EnableDpiAwareness()
        {
            try
            {
                // Per-monitor v2: every coordinate (hook, windows, screen capture) is in physical pixels.
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); } catch { }
        }

        public static float ScaleAt(Point p)
        {
            try
            {
                POINT pt; pt.X = p.X; pt.Y = p.Y;
                IntPtr mon = MonitorFromPoint(pt, 2);
                uint dx, dy;
                if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch { }
            return 1f;
        }

        public static void ForceForeground(IntPtr hwnd)
        {
            IntPtr fg = GetForegroundWindow();
            uint pid;
            uint fgThread = GetWindowThreadProcessId(fg, out pid);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            if (attached) AttachThreadInput(me, fgThread, false);
        }

        static bool? selfElevated;

        static bool IsElevated(IntPtr process, out bool ok)
        {
            ok = false;
            IntPtr token;
            if (!OpenProcessToken(process, 0x0008, out token)) return false;
            try
            {
                int elevated, len;
                if (!GetTokenInformation(token, 20, out elevated, 4, out len)) return false;
                ok = true;
                return elevated != 0;
            }
            finally { CloseHandle(token); }
        }

        /// <summary>True when the process runs elevated and we don't: our replayed clicks would be blocked there.</summary>
        public static bool IsMoreElevatedThanUs(uint pid)
        {
            if (selfElevated == null)
            {
                bool ok;
                selfElevated = IsElevated(GetCurrentProcess(), out ok);
            }
            if (selfElevated.Value) return false;
            IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return true;
            try
            {
                bool ok;
                bool elevated = IsElevated(h, out ok);
                return !ok || elevated;
            }
            finally { CloseHandle(h); }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);
        [DllImport("user32.dll")]
        static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);

        /// <summary>
        /// True when a game (or any app) is running fullscreen: exclusive D3D fullscreen,
        /// or a borderless window covering the whole monitor.
        /// </summary>
        public static bool IsFullscreenAppActive()
        {
            try
            {
                int state;
                if (SHQueryUserNotificationState(out state) == 0 && state == 3) // QUNS_RUNNING_D3D_FULL_SCREEN
                    return true;

                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == GetShellWindow()) return false;
                var cls = new System.Text.StringBuilder(64);
                GetClassName(fg, cls, cls.Capacity);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;

                RECT r;
                if (!GetWindowRect(fg, out r)) return false;
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(MonitorFromWindow(fg, 2), ref mi)) return false;
                var m = mi.rcMonitor;
                // A normal maximized window still has a title bar; fullscreen games don't.
                const int WS_CAPTION = 0x00C00000;
                if ((GetWindowLong(fg, -16) & WS_CAPTION) == WS_CAPTION) return false;
                return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
            }
            catch { return false; }
        }

        public static void RoundCorners(IntPtr hwnd, Color border)
        {
            try
            {
                int round = 2; // DWMWCP_ROUND (Windows 11)
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);
                int colorRef = border.R | (border.G << 8) | (border.B << 16);
                DwmSetWindowAttribute(hwnd, 34, ref colorRef, 4);
            }
            catch { }
        }
    }
}
