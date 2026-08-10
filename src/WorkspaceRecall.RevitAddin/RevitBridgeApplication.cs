using System.Diagnostics;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace WorkspaceRecall.RevitAddin;

public sealed class RevitBridgeApplication : IExternalApplication
{
    private string? _lastRequestId;

    public Result OnStartup(UIControlledApplication application)
    {
        application.Idling += OnIdling;
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= OnIdling;
        return Result.Succeeded;
    }

    private void OnIdling(object? sender, IdlingEventArgs eventArgs)
    {
        if (sender is not UIApplication uiApplication)
        {
            return;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkspaceRecall");
        var requestPath = Path.Combine(directory, "revit-request.json");
        var request = TryReadRequest(requestPath);
        if (request is null ||
            request.RequestId.Equals(
                _lastRequestId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (DateTimeOffset.UtcNow - request.RequestedAtUtc > TimeSpan.FromSeconds(30))
        {
            TryDelete(requestPath);
            return;
        }

        var state = new RevitBridgeState(
            request.RequestId,
            Environment.ProcessId,
            uiApplication.MainWindowHandle.ToInt64(),
            ReadDocumentPath(uiApplication.ActiveUIDocument?.Document),
            DateTimeOffset.UtcNow);
        if (TryWriteState(directory, state))
        {
            _lastRequestId = request.RequestId;
            TryDelete(requestPath);
        }
    }

    private static RevitBridgeRequest? TryReadRequest(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RevitBridgeRequest>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadDocumentPath(Document? document)
    {
        if (document is null ||
            document.IsFamilyDocument && document.PathName.Length == 0)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(document.PathName)
            ? null
            : document.PathName;
    }

    private static bool TryWriteState(
        string directory,
        RevitBridgeState state)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            var path = Path.Combine(directory, "revit-active.json");
            var temporaryPath = path + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    }));
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Space Recorder Revit bridge: {exception.Message}");
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // The request expires quickly and will be retried by the main application.
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
