using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DesktopFences.Core
{
    public static class DesktopMouseHook
    {
        public static event Action? OnDesktopDoubleClick;

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;

        private static IntPtr _hookID = IntPtr.Zero;
        private static LowLevelMouseProc? _proc;
        private static long _lastClickTicks = 0;
        private static readonly int _doubleClickTime = (int)GetDoubleClickTime();

        public static void Start()
        {
            if (_hookID == IntPtr.Zero)
            {
                _proc = HookCallback;
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                if (curModule != null)
                {
                    _hookID = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                }
            }
        }

        public static void Stop()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
                _proc = null;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                long currentTicks = Environment.TickCount64;

                if (currentTicks - _lastClickTicks <= _doubleClickTime)
                {
                    // A double click occurred! Let's check what is under the mouse.
                    IntPtr hwndUnderMouse = WindowFromPoint(hookStruct.pt);

                    if (IsDesktopWindow(hwndUnderMouse))
                    {
                        // Fire the event on the main UI thread
                        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            OnDesktopDoubleClick?.Invoke();
                        }, DispatcherPriority.Normal);
                    }
                    _lastClickTicks = 0; // Reset
                }
                else
                {
                    _lastClickTicks = currentTicks;
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // NEW: Hierarchy crawler. Checks the clicked window AND its parents.
        private static bool IsDesktopWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;

            IntPtr currentHwnd = hwnd;
            int maxDepth = 10; // Safety limit to prevent infinite loops
            int depth = 0;

            while (currentHwnd != IntPtr.Zero && depth < maxDepth)
            {
                char[] classNameChars = new char[256];
                int length = GetClassName(currentHwnd, classNameChars, classNameChars.Length);
                string className = new string(classNameChars, 0, length);

                // "Progman" and "WorkerW" are the Windows Desktop host layers
                if (className == "Progman" || className == "WorkerW")
                    return true;

                // Move up the tree to check the parent window
                currentHwnd = GetAncestor(currentHwnd, 1); // 1 = GA_PARENT
                depth++;
            }

            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

        // NEW: Windows API to climb the visual tree
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    }
}