using System.Diagnostics;
using WorkspaceRecall.App.Models;

namespace WorkspaceRecall.App.Services;

public sealed record RestoreSummary(
    int Requested,
    int OpenedAndPositioned,
    int OpenedWithoutPosition,
    IReadOnlyList<string> Errors);

public sealed record WindowLaunchPlan(
    CapturedWindow Window,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

public sealed record WindowMatchCandidate(
    nint Handle,
    int ProcessId,
    string Title,
    string ProcessName);

public sealed class WindowRestoreService
{
    public async Task<RestoreSummary> RestoreAsync(
        WorkspaceLayout layout,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = BuildLaunchPlan(layout);
        var errors = new List<string>();
        var launches = new List<LaunchAttempt>();

        foreach (var item in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Opening {item.Window.DisplayFileName}…");
            try
            {
                var process = Launch(item);
                launches.Add(new LaunchAttempt(item.Window, process?.Id));
            }
            catch (Exception exception)
            {
                errors.Add($"{item.Window.DisplayFileName}: {exception.Message}");
            }
        }

        var claimedHandles = new HashSet<nint>();
        var claimGate = new object();
        var locateTasks = launches.Select(async launch =>
        {
            var handle = await FindTargetWindowAsync(
                launch.Window,
                launch.StartedProcessId,
                claimedHandles,
                claimGate,
                cancellationToken);
            return (launch.Window, Handle: handle);
        });
        var located = await Task.WhenAll(locateTasks);

        var currentDisplays = WindowCaptureService.CaptureDisplays();
        var positioned = 0;
        var openedWithoutPosition = 0;
        foreach (var locatedWindow in located.OrderByDescending(item => item.Window.ZOrder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (locatedWindow.Handle == nint.Zero)
            {
                openedWithoutPosition++;
                errors.Add(
                    $"{locatedWindow.Window.DisplayFileName}: opened, but its window was not identified for placement.");
                continue;
            }

            progress?.Report($"Positioning {locatedWindow.Window.DisplayFileName}…");
            if (ApplyPlacement(
                    locatedWindow.Handle,
                    locatedWindow.Window,
                    layout.Displays,
                    currentDisplays))
            {
                positioned++;
            }
            else
            {
                openedWithoutPosition++;
                errors.Add(
                    $"{locatedWindow.Window.DisplayFileName}: its window rejected the saved placement.");
            }
        }

        progress?.Report(
            errors.Count == 0
                ? $"Restored {positioned} windows."
                : $"Restored {positioned} windows with {errors.Count} notice(s).");
        return new RestoreSummary(plan.Count, positioned, openedWithoutPosition, errors);
    }

    public static IReadOnlyList<WindowLaunchPlan> BuildLaunchPlan(
        WorkspaceLayout layout) =>
        layout.Windows
            .Where(window => window.IsRestoreReady)
            .OrderByDescending(window => window.ZOrder)
            .Select(BuildLaunch)
            .ToList();

    private static WindowLaunchPlan BuildLaunch(CapturedWindow window)
    {
        if (window.Status == CapturedWindowStatus.ProgramOnly)
        {
            return new WindowLaunchPlan(
                window,
                window.ExecutablePath,
                [],
                Path.GetDirectoryName(window.ExecutablePath) ?? "");
        }

        var documentPath = window.FilePath
            ?? throw new InvalidOperationException("No file path was saved.");
        if (!window.HasUsablePath)
        {
            throw new InvalidOperationException(
                "The saved document path is missing or unsafe to launch.");
        }

        return !string.IsNullOrWhiteSpace(window.ExecutablePath) &&
               File.Exists(window.ExecutablePath)
            ? new WindowLaunchPlan(
                window,
                window.ExecutablePath,
                [documentPath],
                Path.GetDirectoryName(documentPath) ?? "")
            : new WindowLaunchPlan(
                window,
                documentPath,
                [],
                Path.GetDirectoryName(documentPath) ?? "");
    }

    private static Process? Launch(WindowLaunchPlan launch)
    {
        var startInfo = new ProcessStartInfo(launch.FileName)
        {
            UseShellExecute = true,
            WorkingDirectory = launch.WorkingDirectory
        };
        foreach (var argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo);
    }

    private static async Task<nint> FindTargetWindowAsync(
        CapturedWindow savedWindow,
        int? startedProcessId,
        HashSet<nint> claimedHandles,
        object claimGate,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var windows = NativeMethods.EnumerateVisibleWindows()
                .Select(candidate => new WindowMatchCandidate(
                    candidate.Handle,
                    candidate.ProcessId,
                    candidate.Title,
                    ReadProcessName(candidate.ProcessId)))
                .ToList();
            nint handle;
            lock (claimGate)
            {
                handle = SelectTargetWindow(
                    windows,
                    savedWindow,
                    startedProcessId,
                    claimedHandles);
                if (handle != nint.Zero)
                {
                    claimedHandles.Add(handle);
                }
            }

            if (handle != nint.Zero)
            {
                return handle;
            }

            await Task.Delay(400, cancellationToken);
        }

        return nint.Zero;
    }

    public static nint SelectTargetWindow(
        IReadOnlyList<WindowMatchCandidate> windows,
        CapturedWindow savedWindow,
        int? startedProcessId,
        IReadOnlySet<nint> claimedHandles)
    {
        var fileName = Path.GetFileName(
            savedWindow.FilePath?.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
        var fileStem = Path.GetFileNameWithoutExtension(fileName) ?? "";
        var processName = string.IsNullOrWhiteSpace(savedWindow.ProcessName)
            ? Path.GetFileNameWithoutExtension(savedWindow.ExecutablePath)
            : savedWindow.ProcessName;
        var processMatches = windows
            .Where(candidate =>
                candidate.Handle != nint.Zero &&
                !claimedHandles.Contains(candidate.Handle) &&
                candidate.ProcessName.Equals(
                    processName,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        var titleMatch = processMatches.FirstOrDefault(candidate =>
            TitleMatches(candidate.Title, fileName, fileStem));
        if (titleMatch is not null)
        {
            return titleMatch.Handle;
        }

        if (startedProcessId.HasValue)
        {
            var startedProcessMatch = processMatches.FirstOrDefault(candidate =>
                candidate.ProcessId == startedProcessId.Value);
            if (startedProcessMatch is not null)
            {
                return startedProcessMatch.Handle;
            }
        }

        if (savedWindow.Detection != DetectionKind.ProgramOnly ||
            savedWindow.Excluded)
        {
            return nint.Zero;
        }

        var savedTitleMatch = processMatches.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(savedWindow.WindowTitle) &&
            candidate.Title.Equals(
                savedWindow.WindowTitle,
                StringComparison.OrdinalIgnoreCase));
        return savedTitleMatch?.Handle ??
               processMatches.FirstOrDefault()?.Handle ??
               nint.Zero;
    }

    private static string ReadProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static bool TitleMatches(
        string title,
        string fileName,
        string fileStem) =>
        (!string.IsNullOrWhiteSpace(fileName) &&
         title.Contains(fileName, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(fileStem) &&
         title.Contains(fileStem, StringComparison.OrdinalIgnoreCase));

    private static bool ApplyPlacement(
        nint handle,
        CapturedWindow savedWindow,
        IReadOnlyList<DisplaySnapshot> savedDisplays,
        IReadOnlyList<DisplaySnapshot> currentDisplays)
    {
        if (!NativeMethods.IsWindow(handle))
        {
            return false;
        }

        var targetBounds = MapBounds(
            savedWindow,
            savedDisplays,
            currentDisplays);
        NativeMethods.ShowWindowAsync(handle, NativeMethods.SwShownormal);
        var positioned = NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTop,
            targetBounds.X,
            targetBounds.Y,
            targetBounds.Width,
            targetBounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);

        var stateCommand = savedWindow.State switch
        {
            SavedWindowState.Maximized => NativeMethods.SwShowmaximized,
            SavedWindowState.Minimized => NativeMethods.SwShowminimized,
            _ => NativeMethods.SwShownormal
        };
        NativeMethods.ShowWindowAsync(handle, stateCommand);
        return positioned;
    }

    public static PixelRect MapBounds(
        CapturedWindow savedWindow,
        IReadOnlyList<DisplaySnapshot> savedDisplays,
        IReadOnlyList<DisplaySnapshot> currentDisplays)
    {
        var savedDisplay = savedDisplays.FirstOrDefault(display =>
                               display.DeviceName.Equals(
                                   savedWindow.DisplayDeviceName,
                                   StringComparison.OrdinalIgnoreCase))
                           ?? savedDisplays.FirstOrDefault(display => display.IsPrimary)
                           ?? savedDisplays.FirstOrDefault();
        var currentDisplay = currentDisplays.FirstOrDefault(display =>
                                 display.DeviceName.Equals(
                                     savedWindow.DisplayDeviceName,
                                     StringComparison.OrdinalIgnoreCase))
                             ?? currentDisplays.FirstOrDefault(display => display.IsPrimary)
                             ?? currentDisplays.FirstOrDefault();

        if (savedDisplay is null || currentDisplay is null)
        {
            return savedWindow.Bounds;
        }

        var relativeX =
            (savedWindow.Bounds.X - savedDisplay.WorkArea.X) /
            (double)Math.Max(1, savedDisplay.WorkArea.Width);
        var relativeY =
            (savedWindow.Bounds.Y - savedDisplay.WorkArea.Y) /
            (double)Math.Max(1, savedDisplay.WorkArea.Height);
        var relativeWidth =
            savedWindow.Bounds.Width /
            (double)Math.Max(1, savedDisplay.WorkArea.Width);
        var relativeHeight =
            savedWindow.Bounds.Height /
            (double)Math.Max(1, savedDisplay.WorkArea.Height);

        var width = Math.Clamp(
            (int)Math.Round(relativeWidth * currentDisplay.WorkArea.Width),
            240,
            currentDisplay.WorkArea.Width);
        var height = Math.Clamp(
            (int)Math.Round(relativeHeight * currentDisplay.WorkArea.Height),
            160,
            currentDisplay.WorkArea.Height);
        var x = (int)Math.Round(
            currentDisplay.WorkArea.X +
            relativeX * currentDisplay.WorkArea.Width);
        var y = (int)Math.Round(
            currentDisplay.WorkArea.Y +
            relativeY * currentDisplay.WorkArea.Height);
        x = Math.Clamp(
            x,
            currentDisplay.WorkArea.X,
            currentDisplay.WorkArea.Right - width);
        y = Math.Clamp(
            y,
            currentDisplay.WorkArea.Y,
            currentDisplay.WorkArea.Bottom - height);

        return new PixelRect(x, y, width, height);
    }

    private sealed record LaunchAttempt(
        CapturedWindow Window,
        int? StartedProcessId);
}
