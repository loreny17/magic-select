using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MagicSelect
{
    /// <summary>
    /// Global mouse hook. Holding the right button and dragging draws a selection;
    /// a plain right click (no drag) is replayed so context menus keep working.
    /// </summary>
    class MouseHook : IDisposable
    {
        enum State { Idle, Pending, Dragging, PassThrough }

        static readonly IntPtr Marker = new IntPtr(0x4D53454C); // tags the clicks we replay ourselves

        readonly Control ui;
        readonly Native.LowLevelProc proc;
        readonly Timer holdTimer;
        readonly Dictionary<uint, bool> ignoreCache = new Dictionary<uint, bool>();
        IntPtr hook;
        State state = State.Idle;
        Point down;
        Point lastMove;
        bool movePosted;

        public int Threshold = 10;
        public Func<bool> IsEnabled;
        public Func<string, bool> IsExcludedProcess;
        public Func<bool> SkipFullscreen;
        public Func<string, bool, bool> ExtraButton; // (button, isDown) -> swallow?
        public Action<Point> DragStarted;
        public Action<Point, Point> DragMoved;
        public Action<Point, Point> DragEnded;
        public Action<Point> LeftDown;

        public MouseHook(Control ui)
        {
            this.ui = ui;
            proc = Callback;
            holdTimer = new Timer();
            holdTimer.Interval = 650;
            holdTimer.Tick += OnHold;
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                throw new InvalidOperationException("Não foi possível instalar o gancho do mouse (erro " + Marshal.GetLastWin32Error() + ").");
        }

        public void ClearCache() { ignoreCache.Clear(); }

        void Post(MethodInvoker action)
        {
            if (ui.IsHandleCreated) ui.BeginInvoke(action);
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return Native.CallNextHookEx(hook, nCode, wParam, lParam);

            var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
            if (info.dwExtraInfo == Marker) return Native.CallNextHookEx(hook, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            var p = new Point(info.pt.X, info.pt.Y);

            // Middle / side buttons: may be the microphone button.
            if (msg == 0x207 || msg == 0x208 || msg == 0x20B || msg == 0x20C)
            {
                string button = msg <= 0x208 ? "Middle" : (((info.mouseData >> 16) & 0xFFFF) == 1 ? "X1" : "X2");
                bool isDown = msg == 0x207 || msg == 0x20B;
                if (ExtraButton != null && ExtraButton(button, isDown))
                    return new IntPtr(1);
                return Native.CallNextHookEx(hook, nCode, wParam, lParam);
            }

            switch (msg)
            {
                case Native.WM_LBUTTONDOWN:
                    if (LeftDown != null) Post(delegate { LeftDown(p); });
                    break;

                case Native.WM_RBUTTONDOWN:
                    Program.Log("rdown " + p + " state=" + state);
                    if (state == State.Idle && (IsEnabled == null || IsEnabled()) && !ShouldIgnore(info.pt))
                    {
                        state = State.Pending;
                        down = p;
                        holdTimer.Stop();
                        holdTimer.Start();
                        return new IntPtr(1); // swallow until we know whether it's a click or a drag
                    }
                    break;

                case Native.WM_MOUSEMOVE:
                    if (state == State.Pending &&
                        (Math.Abs(p.X - down.X) > Threshold || Math.Abs(p.Y - down.Y) > Threshold))
                    {
                        holdTimer.Stop();
                        state = State.Dragging;
                        Point start = down;
                        if (DragStarted != null) Post(delegate { DragStarted(start); });
                    }
                    if (state == State.Dragging)
                    {
                        lastMove = p;
                        if (!movePosted)
                        {
                            movePosted = true;
                            Post(delegate
                            {
                                movePosted = false;
                                if (state == State.Dragging && DragMoved != null) DragMoved(down, lastMove);
                            });
                        }
                    }
                    break;

                case Native.WM_RBUTTONUP:
                    if (state == State.Pending)
                    {
                        holdTimer.Stop();
                        state = State.Idle;
                        Post(delegate { SendRight(true, true); });
                        return new IntPtr(1);
                    }
                    if (state == State.Dragging)
                    {
                        state = State.Idle;
                        Point start = down;
                        if (DragEnded != null) Post(delegate { DragEnded(start, p); });
                        return new IntPtr(1);
                    }
                    if (state == State.PassThrough) state = State.Idle;
                    break;
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        // Right button held still for a while: hand the press back (press-and-hold menus, games, etc.).
        void OnHold(object sender, EventArgs e)
        {
            holdTimer.Stop();
            if (state != State.Pending) return;
            state = State.PassThrough;
            SendRight(true, false);
        }

        static void SendRight(bool press, bool release)
        {
            var list = new List<Native.INPUT>();
            if (press) list.Add(MakeInput(Native.MOUSEEVENTF_RIGHTDOWN));
            if (release) list.Add(MakeInput(Native.MOUSEEVENTF_RIGHTUP));
            var arr = list.ToArray();
            Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        static Native.INPUT MakeInput(uint flags)
        {
            var i = new Native.INPUT();
            i.type = 0; // INPUT_MOUSE
            i.mi.dwFlags = flags;
            i.mi.dwExtraInfo = Marker;
            return i;
        }

        bool ShouldIgnore(Native.POINT pt)
        {
            try
            {
                IntPtr hwnd = Native.WindowFromPoint(pt);
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return false;
                // (Our own windows are allowed too, so the "try it" text on the welcome screen works.)
                if (SkipFullscreen != null && SkipFullscreen() && Native.IsFullscreenAppActive())
                {
                    Program.Log("fullscreen app active: ignoring");
                    return true; // playing a game: leave the mouse alone
                }
                bool ignore;
                if (ignoreCache.TryGetValue(pid, out ignore)) return ignore;

                ignore = Native.IsMoreElevatedThanUs(pid);
                if (!ignore && IsExcludedProcess != null)
                {
                    string name = "";
                    try { using (var proc = Process.GetProcessById((int)pid)) name = proc.ProcessName; } catch { }
                    ignore = IsExcludedProcess(name);
                }
                Program.Log("pid " + pid + " ignore=" + ignore);
                if (ignoreCache.Count > 200) ignoreCache.Clear();
                ignoreCache[pid] = ignore;
                return ignore;
            }
            catch { return false; }
        }

        public void Dispose()
        {
            holdTimer.Dispose();
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
    }
}
