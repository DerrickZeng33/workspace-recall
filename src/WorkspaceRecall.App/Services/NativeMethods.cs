using System.Runtime.InteropServices;
using System.Text;
using WorkspaceRecall.App.Models;

namespace WorkspaceRecall.App.Services;

internal static class NativeMethods
{
    internal const uint MonitorDefaultToNearest = 2;
    internal const int GwOwner = 4;
    internal const int GwlExStyle = -20;
    internal const long WsExToolWindow = 0x00000080L;
    internal const int DwmwaCloaked = 14;
    internal const int SwHide = 0;
    internal const int SwShownormal = 1;
    internal const int SwShowminimized = 2;
    internal const int SwShowmaximized = 3;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal static readonly nint HwndTop = nint.Zero;

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    internal delegate bool MonitorEnumProc(
        nint hMonitor,
        nint hdcMonitor,
        ref Rect lprcMonitor,
        nint dwData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal PixelRect ToPixelRect() =>
            new(Left, Top, Math.Max(0, Right - Left), Math.Max(0, Bottom - Top));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        internal int Length;
        internal int Flags;
        internal int ShowCmd;
        internal Point MinPosition;
        internal Point MaxPosition;
        internal Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        internal int Size;
        internal Rect Monitor;
        internal Rect WorkArea;
        internal uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hWnd, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(nint hWnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(
        nint hWnd,
        ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PrintWindow(nint hWnd, nint targetDc, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(
        nint hMonitor,
        ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clipRect,
        MonitorEnumProc callback,
        nint data);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        nint hWnd,
        int attribute,
        out int value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        nint hWnd,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(nint hWnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint hWnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    internal static string ReadWindowText(nint hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return "";
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    internal static string ReadClassName(nint hWnd)
    {
        var builder = new StringBuilder(256);
        GetClassName(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    internal static IReadOnlyList<(nint Handle, int ProcessId, string Title)> EnumerateVisibleWindows()
    {
        var windows = new List<(nint, int, string)>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var title = ReadWindowText(hWnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out var processId);
            windows.Add((hWnd, unchecked((int)processId), title));
            return true;
        }, nint.Zero);
        return windows;
    }
}
