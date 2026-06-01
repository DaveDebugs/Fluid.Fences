using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DesktopFences
{
    internal static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnregisterHotKey(IntPtr hWnd, int id);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetDC(IntPtr hwnd);

        [LibraryImport("user32.dll")]
        internal static partial int ReleaseDC(IntPtr hwnd, IntPtr hDC);

        [LibraryImport("gdi32.dll")]
        internal static partial uint GetPixel(IntPtr hDC, int x, int y);

        internal enum AccentState { ACCENT_DISABLED = 0, ACCENT_ENABLE_GRADIENT = 1, ACCENT_ENABLE_TRANSPARENTGRADIENT = 2, ACCENT_ENABLE_BLURBEHIND = 3, ACCENT_ENABLE_ACRYLICBLURBEHIND = 4, ACCENT_INVALID_STATE = 5 }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy { public AccentState AccentState; public uint AccentFlags; public uint GradientColor; public uint AnimationId; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData { public WindowCompositionAttribute Attribute; public IntPtr Data; public int SizeOfData; }

        internal enum WindowCompositionAttribute { WCA_ACCENT_POLICY = 19 }
        internal enum DWMWINDOWATTRIBUTE { DWMWA_WINDOW_CORNER_PREFERENCE = 33 }
        internal enum DWM_WINDOW_CORNER_PREFERENCE { DWMWCP_DEFAULT = 0, DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3 }

        [LibraryImport("dwmapi.dll")]
        internal static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [LibraryImport("user32.dll")]
        internal static partial int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WM_SYSCOMMAND = 0x0112;
        internal const int SC_SIZE = 0xF000;
        internal const int HWND_BOTTOM = 1;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const int WM_WINDOWPOSCHANGING = 0x0046;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static partial IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ReleaseCapture();

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS { public IntPtr hwnd; public IntPtr hwndInsertAfter; public int x; public int y; public int cx; public int cy; public uint flags; }

        [GeneratedComInterface, Guid("bcc18b79-ba16-442f-80c4-8a59c30d463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal partial interface IShellItemImageFactory { void GetImage(in NativeSize size, int flags, out IntPtr phbm); }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeSize { public int Width; public int Height; }

        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, in Guid riid, out IShellItemImageFactory ppv);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

        internal const int SPI_GETDESKWALLPAPER = 0x0073;
        internal const int SPI_SETDESKWALLPAPER = 0x0014;
        internal const int WM_SETTINGCHANGE = 0x001A;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(int uiAction, int uiParam, System.Text.StringBuilder pvParam, int fWinIni);
    }
}