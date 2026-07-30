using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkspaceRecall.App.Models;

namespace WorkspaceRecall.App.Services;

public sealed class WindowPreviewService
{
    private readonly string _previewDirectory;

    public WindowPreviewService(string? previewDirectory = null)
    {
        _previewDirectory = previewDirectory ?? Path.Combine(
            PrivateDataDirectory.DefaultPath,
            "previews");
        PrivateDataDirectory.EnsureSecure(_previewDirectory);
    }

    public void CapturePreviews(
        WorkspaceLayout layout,
        WorkspaceLayout? previousLayout = null)
    {
        Directory.CreateDirectory(_previewDirectory);
        foreach (var window in layout.Windows)
        {
            window.PreviewImagePath = CapturePreview(window);
        }

        if (previousLayout is not null)
        {
            CleanupPreviousPreviews(previousLayout, layout);
        }
    }

    public static BitmapImage? TryLoadPreview(string? previewPath)
    {
        if (string.IsNullOrWhiteSpace(previewPath) ||
            !File.Exists(previewPath))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(previewPath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public void DeletePreview(CapturedWindow window)
    {
        DeletePreviewFile(window.PreviewImagePath);
        window.PreviewImagePath = null;
    }

    public void DeletePreviews(WorkspaceLayout layout)
    {
        foreach (var window in layout.Windows)
        {
            DeletePreview(window);
        }
    }

    private string? CapturePreview(CapturedWindow window)
    {
        if (window.WindowHandle == nint.Zero ||
            !NativeMethods.GetWindowRect(window.WindowHandle, out var windowRect))
        {
            return null;
        }

        var width = Math.Clamp(windowRect.Right - windowRect.Left, 1, 7680);
        var height = Math.Clamp(windowRect.Bottom - windowRect.Top, 1, 4320);
        var screenDc = GetDC(nint.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmapHandle = CreateCompatibleBitmap(screenDc, width, height);
        if (screenDc == nint.Zero ||
            memoryDc == nint.Zero ||
            bitmapHandle == nint.Zero)
        {
            ReleaseCaptureHandles(screenDc, memoryDc, bitmapHandle, nint.Zero);
            return null;
        }

        var previousBitmap = SelectObject(memoryDc, bitmapHandle);
        try
        {
            if (!NativeMethods.PrintWindow(window.WindowHandle, memoryDc, 2))
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();

            var scale = Math.Min(
                1,
                Math.Min(760d / width, 460d / height));
            BitmapSource preview = source;
            if (scale < 1)
            {
                var scaled = new TransformedBitmap(
                    source,
                    new ScaleTransform(scale, scale));
                scaled.Freeze();
                preview = scaled;
            }

            var previewPath = Path.Combine(
                _previewDirectory,
                $"{window.Id}.png");
            using var stream = File.Create(previewPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(preview));
            encoder.Save(stream);
            return previewPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseCaptureHandles(screenDc, memoryDc, bitmapHandle, previousBitmap);
        }
    }

    private void CleanupPreviousPreviews(
        WorkspaceLayout previousLayout,
        WorkspaceLayout currentLayout)
    {
        var currentPaths = currentLayout.Windows
            .Select(window => window.PreviewImagePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var oldPath in previousLayout.Windows
                     .Select(window => window.PreviewImagePath)
                     .Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!currentPaths.Contains(oldPath!))
            {
                DeletePreviewFile(oldPath);
            }
        }
    }

    private void DeletePreviewFile(string? previewPath)
    {
        if (string.IsNullOrWhiteSpace(previewPath))
        {
            return;
        }

        try
        {
            var expectedRoot = Path.GetFullPath(_previewDirectory) +
                               Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(previewPath);
            if (fullPath.StartsWith(
                    expectedRoot,
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // A stale or manually edited layout must not delete outside preview storage.
        }
    }

    private static void ReleaseCaptureHandles(
        nint screenDc,
        nint memoryDc,
        nint bitmapHandle,
        nint previousBitmap)
    {
        if (memoryDc != nint.Zero && previousBitmap != nint.Zero)
        {
            SelectObject(memoryDc, previousBitmap);
        }

        if (bitmapHandle != nint.Zero)
        {
            DeleteObject(bitmapHandle);
        }

        if (memoryDc != nint.Zero)
        {
            DeleteDC(memoryDc);
        }

        if (screenDc != nint.Zero)
        {
            ReleaseDC(nint.Zero, screenDc);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(
        nint deviceContext,
        int width,
        int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);
}
