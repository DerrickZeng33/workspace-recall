using System.Text.Json;
using System.Text.Json.Serialization;
using WorkspaceRecall.App.Models;

namespace WorkspaceRecall.App.Services;

public sealed class LayoutStore
{
    private readonly string _layoutPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public LayoutStore(string? layoutPath = null)
    {
        _layoutPath = layoutPath ?? Path.Combine(
            PrivateDataDirectory.DefaultPath,
            "default-layout.json");
        var directory = Path.GetDirectoryName(_layoutPath)
            ?? throw new InvalidOperationException("The layout path has no parent directory.");
        PrivateDataDirectory.EnsureSecure(directory);
    }

    public string LayoutPath => _layoutPath;

    public async Task<WorkspaceLayout?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_layoutPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_layoutPath);
        return await JsonSerializer.DeserializeAsync<WorkspaceLayout>(
            stream,
            _jsonOptions,
            cancellationToken);
    }

    public async Task SaveAsync(
        WorkspaceLayout layout,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_layoutPath)
            ?? throw new InvalidOperationException("The layout path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _layoutPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                layout,
                _jsonOptions,
                cancellationToken);
        }

        File.Move(temporaryPath, _layoutPath, overwrite: true);
    }
}
