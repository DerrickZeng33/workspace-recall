using System.Diagnostics;
using System.Runtime.InteropServices;
using WorkspaceRecall.App.Models;

namespace WorkspaceRecall.App.Services;

public sealed class WindowCaptureService
{
    private static readonly HashSet<string> IgnoredWindowClasses =
    [
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    ];

    private readonly DocumentPathResolver _documentPathResolver;

    public WindowCaptureService(DocumentPathResolver? documentPathResolver = null)
    {
        _documentPathResolver = documentPathResolver ?? new DocumentPathResolver();
    }

    public WorkspaceLayout Capture(nint ownWindowHandle)
    {
        var displays = CaptureDisplays();
        var windows = CaptureWindows(ownWindowHandle, displays);
        _documentPathResolver.Resolve(windows);

        return new WorkspaceLayout
        {
            CapturedAt = DateTimeOffset.Now,
            Displays = displays,
            Windows = windows
        };
    }

    public static List<DisplaySnapshot> CaptureDisplays()
    {
        var displays = new List<DisplaySnapshot>();
        NativeMethods.EnumDisplayMonitors(
            nint.Zero,
            nint.Zero,
            (nint monitor, nint hdc, ref NativeMethods.Rect monitorRect, nint data) =>
            {
                var info = new NativeMethods.MonitorInfoEx
                {
                    Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
                    DeviceName = ""
                };
                if (NativeMethods.GetMonitorInfo(monitor, ref info))
                {
                    displays.Add(new DisplaySnapshot(
                        info.DeviceName,
                        info.Monitor.ToPixelRect(),
                        info.WorkArea.ToPixelRect(),
                        (info.Flags & 1) == 1));
                }

                return true;
            },
            nint.Zero);

        return displays
            .OrderBy(display => display.Bounds.Y)
            .ThenBy(display => display.Bounds.X)
            .ToList();
    }

    private static List<CapturedWindow> CaptureWindows(
        nint ownWindowHandle,
        IReadOnlyList<DisplaySnapshot> displays)
    {
        var windows = new List<CapturedWindow>();
        var ownProcessId = Environment.ProcessId;
        var zOrder = 0;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            var currentZOrder = zOrder++;
            if (!ShouldIncludeWindow(hWnd, ownWindowHandle, ownProcessId))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out var rawProcessId);
            var processId = unchecked((int)rawProcessId);
            var title = NativeMethods.ReadWindowText(hWnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            var placement = new NativeMethods.WindowPlacement
            {
                Length = Marshal.SizeOf<NativeMethods.WindowPlacement>()
            };
            if (!NativeMethods.GetWindowPlacement(hWnd, ref placement))
            {
                return true;
            }

            var bounds = placement.NormalPosition.ToPixelRect();
            if (bounds.Width < 120 || bounds.Height < 80)
            {
                return true;
            }

            var display = FindDisplay(hWnd, displays);
            var processDetails = ReadProcessDetails(processId);
            var state = placement.ShowCmd switch
            {
                NativeMethods.SwShowmaximized => SavedWindowState.Maximized,
                NativeMethods.SwShowminimized => SavedWindowState.Minimized,
                _ => SavedWindowState.Normal
            };

            windows.Add(new CapturedWindow
            {
                ProcessId = processId,
                ProcessName = processDetails.ProcessName,
                ApplicationName = processDetails.ApplicationName,
                ExecutablePath = processDetails.ExecutablePath,
                WindowTitle = title,
                Bounds = bounds,
                State = state,
                DisplayDeviceName = display?.DeviceName ?? "",
                PlacementLabel = DescribePlacement(bounds, state, display, displays),
                ZOrder = currentZOrder,
                WindowHandle = hWnd,
                DetectionDetail = "This app did not expose a verified file path."
            });
            return true;
        }, nint.Zero);

        return windows;
    }

    private static bool ShouldIncludeWindow(nint hWnd, nint ownWindowHandle, int ownProcessId)
    {
        if (hWnd == ownWindowHandle || !NativeMethods.IsWindowVisible(hWnd))
        {
            return false;
        }

        if (NativeMethods.GetWindow(hWnd, NativeMethods.GwOwner) != nint.Zero)
        {
            return false;
        }

        if ((NativeMethods.GetWindowLong(hWnd, NativeMethods.GwlExStyle) &
             NativeMethods.WsExToolWindow) != 0)
        {
            return false;
        }

        var className = NativeMethods.ReadClassName(hWnd);
        if (IgnoredWindowClasses.Contains(className))
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(
                hWnd,
                NativeMethods.DwmwaCloaked,
                out var cloaked,
                sizeof(int)) == 0 &&
            cloaked != 0)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
        return processId != 0 && processId != ownProcessId;
    }

    private static DisplaySnapshot? FindDisplay(
        nint hWnd,
        IReadOnlyList<DisplaySnapshot> displays)
    {
        var monitor = NativeMethods.MonitorFromWindow(
            hWnd,
            NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfoEx
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
            DeviceName = ""
        };
        if (monitor != nint.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return displays.FirstOrDefault(display =>
                string.Equals(
                    display.DeviceName,
                    info.DeviceName,
                    StringComparison.OrdinalIgnoreCase));
        }

        return displays.FirstOrDefault(display => display.IsPrimary);
    }

    private static (string ProcessName, string ApplicationName, string ExecutablePath)
        ReadProcessDetails(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var executablePath = process.MainModule?.FileName ?? "";
            var applicationName = "";
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                applicationName =
                    FileVersionInfo.GetVersionInfo(executablePath).FileDescription ?? "";
            }

            if (string.IsNullOrWhiteSpace(applicationName))
            {
                applicationName = HumanizeProcessName(process.ProcessName);
            }

            return (process.ProcessName, applicationName, executablePath);
        }
        catch
        {
            return ("unknown", "Unknown application", "");
        }
    }

    private static string HumanizeProcessName(string processName) =>
        processName.ToLowerInvariant() switch
        {
            "excel" => "Microsoft Excel",
            "winword" => "Microsoft Word",
            "acad" => "AutoCAD",
            "revit" => "Autodesk Revit",
            "code" => "Visual Studio Code",
            "explorer" => "File Explorer",
            _ => processName
        };

    public static string DescribePlacement(
        PixelRect bounds,
        SavedWindowState state,
        DisplaySnapshot? display,
        IReadOnlyList<DisplaySnapshot> displays)
    {
        if (display is null)
        {
            return "Unknown display";
        }

        var displayIndex = 0;
        for (var index = 0; index < displays.Count; index++)
        {
            if (ReferenceEquals(displays[index], display) ||
                displays[index] == display)
            {
                displayIndex = index + 1;
                break;
            }
        }
        var prefix = $"Display {Math.Max(1, displayIndex)}";
        if (state == SavedWindowState.Maximized)
        {
            return $"{prefix} · Maximized";
        }

        var workArea = display.WorkArea;
        var widthRatio = bounds.Width / (double)Math.Max(1, workArea.Width);
        var heightRatio = bounds.Height / (double)Math.Max(1, workArea.Height);
        var centerX = bounds.X + bounds.Width / 2.0;
        var workCenterX = workArea.X + workArea.Width / 2.0;

        if (widthRatio >= 0.9 && heightRatio >= 0.85)
        {
            return $"{prefix} · Maximized";
        }

        if (widthRatio is >= 0.35 and <= 0.65 && heightRatio >= 0.75)
        {
            return centerX <= workCenterX
                ? $"{prefix} · Left half"
                : $"{prefix} · Right half";
        }

        return $"{prefix} · Custom";
    }
}
