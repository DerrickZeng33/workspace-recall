using System.Text.Json.Serialization;
using System.Text;

namespace WorkspaceRecall.App.Models;

public sealed record PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public sealed record DisplaySnapshot(
    string DeviceName,
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary);

public enum DetectionKind
{
    ExactPath,
    CommandLine,
    UserConfirmed,
    ProgramOnly,
    NeedsFile
}

public enum CapturedWindowStatus
{
    FileIdentified,
    ProgramOnly,
    NeedsReview,
    Excluded
}

public enum SavedWindowState
{
    Normal,
    Maximized,
    Minimized
}

public sealed class CapturedWindow
{
    private static readonly HashSet<string> UnsafeDocumentExtensions =
    [
        ".appref-ms",
        ".application",
        ".bat",
        ".cmd",
        ".com",
        ".cpl",
        ".dll",
        ".exe",
        ".gadget",
        ".hta",
        ".jar",
        ".js",
        ".jse",
        ".lnk",
        ".msi",
        ".msp",
        ".mst",
        ".pl",
        ".ps1",
        ".psd1",
        ".psm1",
        ".py",
        ".pyw",
        ".rb",
        ".reg",
        ".scr",
        ".sh",
        ".sys",
        ".url",
        ".vbe",
        ".vbs",
        ".wsf",
        ".wsh"
    ];

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string ApplicationName { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public string? FilePath { get; set; }
    public string? PreviewImagePath { get; set; }
    public DetectionKind Detection { get; set; } = DetectionKind.NeedsFile;
    public string DetectionDetail { get; set; } = "";
    public PixelRect Bounds { get; set; } = new(0, 0, 800, 600);
    public SavedWindowState State { get; set; }
    public string DisplayDeviceName { get; set; } = "";
    public string PlacementLabel { get; set; } = "";
    public int ZOrder { get; set; }
    public bool Excluded { get; set; }

    [JsonIgnore]
    public nint WindowHandle { get; set; }

    [JsonIgnore]
    public string DisplayFileName =>
        string.IsNullOrWhiteSpace(FilePath)
            ? DeriveFileHint(WindowTitle)
            : Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar));

    [JsonIgnore]
    public string DisplayApplicationName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ApplicationName))
            {
                return ApplicationName.Equals(
                    ProcessName,
                    StringComparison.OrdinalIgnoreCase)
                    ? HumanizeApplicationName(ApplicationName)
                    : ApplicationName;
            }

            if (!string.IsNullOrWhiteSpace(ProcessName))
            {
                return HumanizeApplicationName(ProcessName);
            }

            return DeriveApplicationHint(WindowTitle);
        }
    }

    [JsonIgnore]
    public bool HasUsablePath =>
        !string.IsNullOrWhiteSpace(FilePath) &&
        (Directory.Exists(FilePath) ||
         File.Exists(FilePath) && IsAllowedDocumentPath(FilePath));

    [JsonIgnore]
    public bool HasUsableExecutable =>
        !string.IsNullOrWhiteSpace(ExecutablePath) &&
        File.Exists(ExecutablePath) &&
        Path.GetExtension(ExecutablePath).Equals(
            ".exe",
            StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedDocumentPath(string path) =>
        !UnsafeDocumentExtensions.Contains(
            Path.GetExtension(path).ToLowerInvariant());

    [JsonIgnore]
    public CapturedWindowStatus Status => Excluded
        ? CapturedWindowStatus.Excluded
        : HasUsablePath
            ? CapturedWindowStatus.FileIdentified
            : Detection == DetectionKind.ProgramOnly && HasUsableExecutable
                ? CapturedWindowStatus.ProgramOnly
                : CapturedWindowStatus.NeedsReview;

    [JsonIgnore]
    public bool IsRestoreReady =>
        Status is CapturedWindowStatus.FileIdentified or CapturedWindowStatus.ProgramOnly;

    [JsonIgnore]
    public string StatusLabel => Status switch
    {
        CapturedWindowStatus.FileIdentified => "File identified",
        CapturedWindowStatus.ProgramOnly => "Program only",
        CapturedWindowStatus.Excluded => "Excluded",
        _ => "Needs review"
    };

    [JsonIgnore]
    public string DetectionLabel => Detection switch
    {
        DetectionKind.ExactPath => "Exact path",
        DetectionKind.CommandLine => "Command line",
        DetectionKind.UserConfirmed => "User confirmed",
        DetectionKind.ProgramOnly => "Program only",
        _ => "Needs review"
    };

    private static string DeriveFileHint(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Unknown file";
        }

        var separators = new[] { " - ", " — ", " | " };
        var first = separators
            .Select(separator => title.Split(separator, StringSplitOptions.TrimEntries)[0])
            .OrderBy(value => value.Length)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? title : first;
    }

    private static string DeriveApplicationHint(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Unknown program";
        }

        foreach (var separator in new[] { " - ", " — ", " | " })
        {
            var segments = title.Split(
                separator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length > 1)
            {
                return segments[^1];
            }
        }

        return title;
    }

    private static string HumanizeApplicationName(string value)
    {
        var name = value.Trim();
        foreach (var suffix in new[] { ".WinUI", ".exe" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        var result = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            if (current is '.' or '_' or '-')
            {
                if (result.Length > 0 && result[^1] != ' ')
                {
                    result.Append(' ');
                }

                continue;
            }

            if (index > 0 &&
                char.IsUpper(current) &&
                (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])))
            {
                result.Append(' ');
            }

            result.Append(current);
        }

        return result.ToString().Trim();
    }
}

public sealed class WorkspaceLayout
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Default Layout";
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public List<DisplaySnapshot> Displays { get; set; } = [];
    public List<CapturedWindow> Windows { get; set; } = [];

    [JsonIgnore]
    public int FileIdentifiedCount => Windows.Count(window =>
        window.Status == CapturedWindowStatus.FileIdentified);

    [JsonIgnore]
    public int ProgramOnlyCount => Windows.Count(window =>
        window.Status == CapturedWindowStatus.ProgramOnly);

    [JsonIgnore]
    public int NeedsReviewCount => Windows.Count(window =>
        window.Status == CapturedWindowStatus.NeedsReview);

    [JsonIgnore]
    public int ExcludedCount => Windows.Count(window =>
        window.Status == CapturedWindowStatus.Excluded);

    [JsonIgnore]
    public int RestorableCount => Windows.Count(window => window.IsRestoreReady);

    [JsonIgnore]
    public int NeedsAttentionCount => NeedsReviewCount;

    public void ApplyRememberedDecisions(WorkspaceLayout? previousLayout)
    {
        if (previousLayout is null)
        {
            return;
        }

        var programOnlyExecutables = previousLayout.Windows
            .Where(window =>
                !window.Excluded &&
                window.Detection == DetectionKind.ProgramOnly &&
                !string.IsNullOrWhiteSpace(window.ExecutablePath))
            .Select(window => window.ExecutablePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var window in Windows.Where(window =>
                     window.Detection == DetectionKind.NeedsFile &&
                     !window.HasUsablePath &&
                     programOnlyExecutables.Contains(window.ExecutablePath)))
        {
            window.Detection = DetectionKind.ProgramOnly;
            window.DetectionDetail =
                "Previously confirmed by the user as a program-only window.";
        }
    }
}
