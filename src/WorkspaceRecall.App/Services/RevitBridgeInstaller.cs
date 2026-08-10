using System.Security;
using System.Text.Json;

namespace WorkspaceRecall.App.Services;

public sealed class RevitBridgeInstaller
{
    private const string AddInId = "15DA7F3E-79A2-47A4-BE32-76E0BC8A6E9C";
    private readonly string _dataDirectory;
    private readonly string _manifestPath;
    private readonly string _bundledAddInPath;
    private readonly string _installedAddInPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RevitBridgeInstaller(
        string? dataDirectory = null,
        string? manifestPath = null,
        string? bundledAddInPath = null)
    {
        _dataDirectory = dataDirectory ?? PrivateDataDirectory.DefaultPath;
        _manifestPath = manifestPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk",
            "Revit",
            "Addins",
            "2026",
            "WorkspaceRecall.addin");
        _bundledAddInPath = bundledAddInPath ?? Path.Combine(
            AppContext.BaseDirectory,
            "RevitAddin",
            "WorkspaceRecall.RevitAddin.OnDemand.dll");
        _installedAddInPath = Path.Combine(
            _dataDirectory,
            "RevitAddin",
            "WorkspaceRecall.RevitAddin.dll");
    }

    public string RequestPath => Path.Combine(
        _dataDirectory,
        "revit-request.json");

    public string StatePath => Path.Combine(
        _dataDirectory,
        "revit-active.json");

    public bool IsAvailable => File.Exists(_bundledAddInPath);

    public bool IsEnabled =>
        File.Exists(_manifestPath) &&
        File.Exists(_installedAddInPath);

    public bool TryEnable(out string status)
    {
        if (!IsAvailable)
        {
            status = "The optional Revit helper is not bundled in this build.";
            return false;
        }

        try
        {
            PrivateDataDirectory.EnsureSecure(_dataDirectory);
            var installedDirectory = Path.GetDirectoryName(_installedAddInPath)!;
            Directory.CreateDirectory(installedDirectory);
            File.Copy(
                _bundledAddInPath,
                _installedAddInPath,
                overwrite: true);

            var manifestDirectory = Path.GetDirectoryName(_manifestPath)!;
            Directory.CreateDirectory(manifestDirectory);
            var escapedAssemblyPath = SecurityElement.Escape(_installedAddInPath);
            var manifest = $"""
                <?xml version="1.0" encoding="utf-8" standalone="no"?>
                <RevitAddIns>
                  <AddIn Type="Application">
                    <Name>Space Recorder</Name>
                    <Assembly>{escapedAssemblyPath}</Assembly>
                    <AddInId>{AddInId}</AddInId>
                    <FullClassName>WorkspaceRecall.RevitAddin.RevitBridgeApplication</FullClassName>
                    <VendorId>WRCL</VendorId>
                    <VendorDescription>Space Recorder on-demand local document path bridge</VendorDescription>
                  </AddIn>
                </RevitAddIns>
                """;
            File.WriteAllText(_manifestPath, manifest);
            status = "Revit integration enabled. Restart Revit if it is already open.";
            return true;
        }
        catch (Exception exception)
        {
            status = $"Revit integration could not be enabled: {exception.Message}";
            return false;
        }
    }

    public bool TryDisable(out string status)
    {
        try
        {
            DeleteIfPresent(_manifestPath);
            DeleteIfPresent(RequestPath);
            DeleteIfPresent(RequestPath + ".tmp");
            DeleteIfPresent(StatePath);
            DeleteIfPresent(_installedAddInPath);
            var installedDirectory = Path.GetDirectoryName(_installedAddInPath)!;
            if (Directory.Exists(installedDirectory) &&
                !Directory.EnumerateFileSystemEntries(installedDirectory).Any())
            {
                Directory.Delete(installedDirectory);
            }

            status = "Revit integration disabled. Restart Revit if it is already open.";
            return true;
        }
        catch (Exception exception)
        {
            status = $"Revit integration could not be fully removed: {exception.Message}";
            return false;
        }
    }

    public async Task<bool> RequestSnapshotAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return false;
        }

        PrivateDataDirectory.EnsureSecure(_dataDirectory);
        DeleteIfPresent(StatePath);
        var request = new RevitBridgeRequest(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow);
        var temporaryRequestPath = RequestPath + ".tmp";
        File.WriteAllText(
            temporaryRequestPath,
            JsonSerializer.Serialize(request, _jsonOptions));
        File.Move(temporaryRequestPath, RequestPath, overwrite: true);

        var deadline = DateTimeOffset.UtcNow + timeout;
        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryReadMatchingState(request.RequestId))
                {
                    return true;
                }

                await Task.Delay(100, cancellationToken);
            }

            return false;
        }
        finally
        {
            DeleteIfPresent(RequestPath);
            DeleteIfPresent(temporaryRequestPath);
        }
    }

    private bool TryReadMatchingState(string requestId)
    {
        if (!File.Exists(StatePath))
        {
            return false;
        }

        try
        {
            var state = JsonSerializer.Deserialize<RevitBridgeState>(
                File.ReadAllText(StatePath),
                _jsonOptions);
            return state?.RequestId.Equals(
                requestId,
                StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record RevitBridgeRequest(
        string RequestId,
        DateTimeOffset RequestedAtUtc);

    private sealed record RevitBridgeState(
        string RequestId,
        int ProcessId,
        long MainWindowHandle,
        string? DocumentPath,
        DateTimeOffset UpdatedAtUtc);
}
