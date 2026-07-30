using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorkspaceRecall.App.Services;

public static class AppIconService
{
    public static ImageSource? TryLoadIcon(string? executablePath, string? filePath)
    {
        var sourcePath = new[] { executablePath, filePath }
            .FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) &&
                File.Exists(path));
        if (sourcePath is null)
        {
            return null;
        }

        try
        {
            var fileInfo = new ShellFileInfo();
            var result = ShGetFileInfo(
                sourcePath,
                0,
                ref fileInfo,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiIcon | ShgfiLargeIcon);
            if (result == nint.Zero || fileInfo.IconHandle == nint.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    fileInfo.IconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(fileInfo.IconHandle);
            }
        }
        catch
        {
            return null;
        }
    }

    public static BitmapImage LoadFallbackIcon()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(
            "pack://application:,,,/Assets/workspace-recall-icon.png",
            UriKind.Absolute);
        image.DecodePixelWidth = 32;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        internal nint IconHandle;
        internal int IconIndex;
        internal uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        internal string TypeName;
    }

    [DllImport(
        "shell32.dll",
        EntryPoint = "SHGetFileInfoW",
        CharSet = CharSet.Unicode)]
    private static extern nint ShGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);
}
