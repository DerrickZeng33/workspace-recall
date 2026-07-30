using System.Runtime.InteropServices;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkspaceRecall.App.Models;

namespace WorkspaceRecall.App.Services;

public sealed class DocumentPathResolver
{
    private static readonly Regex LineSuffixPattern =
        new(@":\d+(?::\d+)?$", RegexOptions.Compiled);
    private readonly List<string> _adapterDiagnostics = [];

    public IReadOnlyList<string> AdapterDiagnostics => _adapterDiagnostics;

    public void Resolve(IReadOnlyList<CapturedWindow> windows)
    {
        var adapterMatches = new Dictionary<nint, Resolution>();
        ResolveExcel(adapterMatches);
        ResolveWord(adapterMatches);
        ResolveAutoCad(adapterMatches);
        ResolveRevit(windows, adapterMatches);

        foreach (var window in windows)
        {
            if (adapterMatches.TryGetValue(window.WindowHandle, out var adapterMatch) &&
                IsExistingPath(adapterMatch.Path))
            {
                Apply(window, adapterMatch);
                continue;
            }

            var sameProcessMatches = adapterMatches
                .Where(match => windows.Any(candidate =>
                    candidate.WindowHandle == match.Key &&
                    candidate.ProcessId == window.ProcessId))
                .Select(match => match.Value)
                .DistinctBy(match => match.Path)
                .ToList();
            var sameProcessWindows = windows.Count(candidate =>
                candidate.ProcessId == window.ProcessId);
            if (sameProcessMatches.Count == 1 &&
                sameProcessWindows == 1 &&
                IsExistingPath(sameProcessMatches[0].Path))
            {
                Apply(window, sameProcessMatches[0]);
                continue;
            }

            var titlePath = TryGetExistingPathFromTitle(window.WindowTitle);
            if (titlePath is not null)
            {
                Apply(window, new Resolution(
                    titlePath,
                    DetectionKind.ExactPath,
                    "Verified from the full path shown by the window."));
                continue;
            }

            var commandLine = TryReadProcessCommandLine(window.ProcessId);
            var commandLinePaths = string.IsNullOrWhiteSpace(commandLine)
                ? []
                : ExtractExistingPaths(commandLine, window.ExecutablePath);
            var commandLinePath = ChooseBestPath(commandLinePaths, window.WindowTitle);
            if (commandLinePath is not null)
            {
                Apply(window, new Resolution(
                    commandLinePath,
                    DetectionKind.CommandLine,
                    "Verified existing path found in the program launch command."));
                continue;
            }

            window.Detection = DetectionKind.NeedsFile;
            window.DetectionDetail =
                "This app did not expose a verified file path. Choose the file once or exclude it.";
        }
    }

    public static IReadOnlyList<string> ExtractExistingPaths(
        string commandLine,
        string? executablePath = null)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var arguments = SplitCommandLine(commandLine);
        var paths = new List<string>();
        for (var index = 1; index < arguments.Count; index++)
        {
            AddCandidate(arguments[index], executablePath, paths);

            var joined = arguments[index];
            for (var endIndex = index + 1; endIndex < arguments.Count; endIndex++)
            {
                joined += " " + arguments[endIndex];
                AddCandidate(joined, executablePath, paths);
            }
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddCandidate(
        string argument,
        string? executablePath,
        ICollection<string> paths)
    {
        var candidate = NormalizeArgument(argument);
        if (candidate is null ||
            (!string.IsNullOrWhiteSpace(executablePath) &&
             PathEquals(candidate, executablePath)))
        {
            return;
        }

        if (IsExistingPath(candidate))
        {
            paths.Add(Path.GetFullPath(candidate));
        }
    }

    private void ResolveExcel(IDictionary<nint, Resolution> matches)
    {
        object? application = null;
        object? windows = null;
        try
        {
            application = TryGetActiveObject("Excel.Application");
            if (application is null)
            {
                return;
            }

            dynamic excel = application;
            windows = excel.Windows;
            dynamic excelWindows = windows;
            var count = Convert.ToInt32(excelWindows.Count);
            for (var index = 1; index <= count; index++)
            {
                object? excelWindow = null;
                object? workbook = null;
                try
                {
                    excelWindow = excelWindows.Item(index);
                    dynamic window = excelWindow;
                    workbook = window.Parent;
                    dynamic book = workbook;
                    var path = Convert.ToString(book.FullName);
                    var handle = new nint(Convert.ToInt64(window.Hwnd));
                    if (IsExistingPath(path))
                    {
                        matches[handle] = new Resolution(
                            path!,
                            DetectionKind.ExactPath,
                            "Read from Microsoft Excel.");
                    }
                }
                catch (Exception exception)
                {
                    _adapterDiagnostics.Add(
                        $"Excel window {index}: {exception.GetType().Name}: {exception.Message}");
                }
                finally
                {
                    ReleaseComObject(workbook);
                    ReleaseComObject(excelWindow);
                }
            }
        }
        catch (Exception exception)
        {
            _adapterDiagnostics.Add(
                $"Excel adapter: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(application);
        }
    }

    private void ResolveWord(IDictionary<nint, Resolution> matches)
    {
        object? application = null;
        object? windows = null;
        try
        {
            application = TryGetActiveObject("Word.Application");
            if (application is null)
            {
                return;
            }

            dynamic word = application;
            windows = word.Windows;
            dynamic wordWindows = windows;
            var count = Convert.ToInt32(wordWindows.Count);
            for (var index = 1; index <= count; index++)
            {
                object? wordWindow = null;
                object? document = null;
                try
                {
                    wordWindow = wordWindows.Item(index);
                    dynamic window = wordWindow;
                    document = window.Document;
                    dynamic doc = document;
                    var path = Convert.ToString(doc.FullName);
                    var handle = new nint(Convert.ToInt64(window.Hwnd));
                    if (IsExistingPath(path))
                    {
                        matches[handle] = new Resolution(
                            path!,
                            DetectionKind.ExactPath,
                            "Read from Microsoft Word.");
                    }
                }
                catch (Exception exception)
                {
                    _adapterDiagnostics.Add(
                        $"Word window {index}: {exception.GetType().Name}: {exception.Message}");
                }
                finally
                {
                    ReleaseComObject(document);
                    ReleaseComObject(wordWindow);
                }
            }
        }
        catch (Exception exception)
        {
            _adapterDiagnostics.Add(
                $"Word adapter: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(application);
        }
    }

    private void ResolveAutoCad(IDictionary<nint, Resolution> matches)
    {
        foreach (var programmaticId in new[]
                 {
                     "AutoCAD.Application",
                     "AutoCAD.Application.25.1"
                 })
        {
            object? application = null;
            object? document = null;
            try
            {
                application = TryGetActiveObject(programmaticId);
                if (application is null)
                {
                    continue;
                }

                dynamic autoCad = application;
                document = autoCad.ActiveDocument;
                dynamic activeDocument = document;
                var path = Convert.ToString(activeDocument.FullName);
                var handle = new nint(Convert.ToInt64(autoCad.HWND));
                if (IsExistingPath(path))
                {
                    matches[handle] = new Resolution(
                        path!,
                        DetectionKind.ExactPath,
                        "Read from AutoCAD ActiveDocument.");
                }

                return;
            }
            catch (Exception exception)
            {
                _adapterDiagnostics.Add(
                    $"AutoCAD {programmaticId}: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                ReleaseComObject(document);
                ReleaseComObject(application);
            }
        }
    }

    private static void ResolveRevit(
        IReadOnlyList<CapturedWindow> windows,
        IDictionary<nint, Resolution> matches)
    {
        var bridgePath = Path.Combine(
            PrivateDataDirectory.DefaultPath,
            "revit-active.json");
        if (!File.Exists(bridgePath))
        {
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<RevitBridgeState>(
                File.ReadAllText(bridgePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (state is null ||
                string.IsNullOrWhiteSpace(state.RequestId) ||
                !IsExistingPath(state.DocumentPath) ||
                DateTimeOffset.UtcNow - state.UpdatedAtUtc > TimeSpan.FromSeconds(30))
            {
                return;
            }

            var revitWindow = windows.FirstOrDefault(window =>
                window.ProcessId == state.ProcessId &&
                window.ProcessName.Equals("Revit", StringComparison.OrdinalIgnoreCase));
            if (revitWindow is not null)
            {
                matches[revitWindow.WindowHandle] = new Resolution(
                    state.DocumentPath!,
                    DetectionKind.ExactPath,
                    "Read from the Space Recorder Revit helper.");
            }
        }
        catch
        {
            // A stale or partially-written bridge file must not break capture.
        }
        finally
        {
            try
            {
                File.Delete(bridgePath);
            }
            catch
            {
                // A later capture can safely replace the stale response.
            }
        }
    }

    public static string? TryReadProcessCommandLine(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            using var results = searcher.Get();
            foreach (ManagementObject result in results)
            {
                return Convert.ToString(result["CommandLine"]);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? TryGetExistingPathFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var candidates = new List<string> { title.Trim() };
        foreach (var separator in new[] { " - ", " — ", " | " })
        {
            var current = title;
            while (current.Contains(separator, StringComparison.Ordinal))
            {
                current = current[..current.LastIndexOf(separator, StringComparison.Ordinal)].Trim();
                candidates.Add(current);
            }
        }

        return candidates.FirstOrDefault(candidate =>
            Path.IsPathFullyQualified(candidate) && IsExistingPath(candidate));
    }

    private static string? ChooseBestPath(
        IReadOnlyList<string> paths,
        string windowTitle)
    {
        if (paths.Count == 0)
        {
            return null;
        }

        var titleMatch = paths.FirstOrDefault(path =>
            windowTitle.Contains(
                Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),
                StringComparison.OrdinalIgnoreCase));
        return titleMatch ?? paths[0];
    }

    private static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var argumentCount);
        if (argv == nint.Zero)
        {
            return [];
        }

        try
        {
            var arguments = new List<string>(argumentCount);
            for (var index = 0; index < argumentCount; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(argv, index * nint.Size);
                arguments.Add(Marshal.PtrToStringUni(argumentPointer) ?? "");
            }

            return arguments;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    private static string? NormalizeArgument(string argument)
    {
        var candidate = argument.Trim().Trim('"');
        if (candidate.StartsWith("--", StringComparison.Ordinal) &&
            candidate.Contains('='))
        {
            candidate = candidate[(candidate.IndexOf('=') + 1)..].Trim('"');
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            candidate = uri.LocalPath;
        }

        if (!IsExistingPath(candidate))
        {
            var withoutLineSuffix = LineSuffixPattern.Replace(candidate, "");
            if (IsExistingPath(withoutLineSuffix))
            {
                candidate = withoutLineSuffix;
            }
        }

        return Path.IsPathFullyQualified(candidate) ? candidate : null;
    }

    private static bool IsExistingPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (File.Exists(path) || Directory.Exists(path));

    private static bool PathEquals(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void Apply(CapturedWindow window, Resolution resolution)
    {
        window.FilePath = resolution.Path;
        window.Detection = resolution.Kind;
        window.DetectionDetail = resolution.Detail;
    }

    private object? TryGetActiveObject(string programmaticId)
    {
        try
        {
            ClsidFromProgId(programmaticId, out var classId);
            GetActiveObject(ref classId, nint.Zero, out var instance);
            return instance;
        }
        catch (Exception exception)
        {
            _adapterDiagnostics.Add(
                $"{programmaticId} ROT lookup: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                Marshal.FinalReleaseComObject(value);
            }
            catch
            {
                // The object may already have been released by its owning collection.
            }
        }
    }

    [DllImport(
        "ole32.dll",
        EntryPoint = "CLSIDFromProgID",
        CharSet = CharSet.Unicode,
        PreserveSig = false)]
    private static extern void ClsidFromProgId(string programmaticId, out Guid classId);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid classId,
        nint reserved,
        [MarshalAs(UnmanagedType.Interface)] out object instance);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
        out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);

    private sealed record Resolution(
        string Path,
        DetectionKind Kind,
        string Detail);

    private sealed record RevitBridgeState(
        string RequestId,
        int ProcessId,
        long MainWindowHandle,
        string? DocumentPath,
        DateTimeOffset UpdatedAtUtc);
}
