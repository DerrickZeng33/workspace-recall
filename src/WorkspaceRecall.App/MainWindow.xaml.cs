using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WorkspaceRecall.App.Models;
using WorkspaceRecall.App.Services;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace WorkspaceRecall.App;

public partial class MainWindow : Window
{
    private readonly LayoutStore _layoutStore = new();
    private readonly WindowCaptureService _captureService = new();
    private readonly WindowRestoreService _restoreService = new();
    private readonly WindowPreviewService _previewService = new();
    private readonly RevitBridgeInstaller _revitBridge = new();
    private WorkspaceLayout? _layout;
    private CapturedWindow? _selectedWindow;
    private CapturedWindow? _attentionWindow;
    private bool _isBusy;
    private string _revitStatus = "";

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        _revitStatus = _revitBridge.IsEnabled
            ? "Revit integration enabled."
            : "Revit integration disabled.";
        try
        {
            _layout = await _layoutStore.LoadAsync();
        }
        catch (Exception exception)
        {
            ActivityText.Text = $"Saved layout could not be read: {exception.Message}";
        }

        if (_layout is not null)
        {
            _selectedWindow = ChooseInitialSelection(_layout);
        }

        RefreshRevitIntegrationControl();
        RefreshView();
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true, "Checking visible windows and exact file paths…");
        try
        {
            if (_revitBridge.IsEnabled &&
                Process.GetProcessesByName("Revit").Length > 0)
            {
                _revitStatus = await _revitBridge.RequestSnapshotAsync(
                    TimeSpan.FromSeconds(3))
                    ? "Revit responded to this capture."
                    : "Revit did not respond to this capture.";
            }

            var ownWindowHandle = new WindowInteropHelper(this).Handle;
            var layout = await Task.Run(() => _captureService.Capture(ownWindowHandle));
            layout.ApplyRememberedDecisions(_layout);
            if (SavePreviewsCheckBox.IsChecked == true)
            {
                _previewService.CapturePreviews(layout, _layout);
            }
            else if (_layout is not null)
            {
                _previewService.DeletePreviews(_layout);
            }

            await _layoutStore.SaveAsync(layout);
            _layout = layout;
            _selectedWindow = ChooseInitialSelection(layout);
            ActivityText.Text =
                $"Captured {layout.Windows.Count} windows; {layout.RestorableCount} ready to restore; {layout.NeedsReviewCount} need review. {_revitStatus}";
        }
        catch (Exception exception)
        {
            ActivityText.Text = "Capture failed.";
            MessageBox.Show(
                this,
                exception.Message,
                "Workspace Recall could not capture this layout",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshView();
        }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_isBusy || _layout is null || _layout.RestorableCount == 0)
        {
            return;
        }

        var skippedWindows = _layout.Windows
            .Where(window => window.Status == CapturedWindowStatus.NeedsReview)
            .OrderBy(window => window.DisplayApplicationName)
            .ThenBy(window => window.DisplayFileName)
            .ToList();
        if (skippedWindows.Count > 0)
        {
            var skippedNames = string.Join(
                Environment.NewLine,
                skippedWindows.Select(window =>
                    $"• {window.DisplayApplicationName} — {window.DisplayFileName}"));
            var result = MessageBox.Show(
                this,
                $"Workspace Recall will restore {_layout.RestorableCount} ready windows and skip these {skippedWindows.Count} windows:{Environment.NewLine}{Environment.NewLine}{skippedNames}",
                "Some captured windows need review",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (result != MessageBoxResult.OK)
            {
                return;
            }
        }

        SetBusy(true, "Opening saved files…");
        WindowState = WindowState.Minimized;
        try
        {
            var progress = new Progress<string>(message => ActivityText.Text = message);
            var summary = await _restoreService.RestoreAsync(_layout, progress);
            if (summary.Errors.Count > 0)
            {
                WindowState = WindowState.Normal;
                Activate();
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, summary.Errors),
                    "Workspace Recall restored the layout with notices",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception exception)
        {
            WindowState = WindowState.Normal;
            Activate();
            ActivityText.Text = "Restore failed.";
            MessageBox.Show(
                this,
                exception.Message,
                "Workspace Recall could not restore this layout",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ChooseFileButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_layout is null || _attentionWindow is null)
        {
            return;
        }

        await ChooseFileForWindowAsync(_attentionWindow);
    }

    private async Task ChooseFileForWindowAsync(CapturedWindow window)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Choose the file open in {window.DisplayApplicationName}",
            CheckFileExists = true,
            Multiselect = false,
            Filter = "All files (*.*)|*.*",
            FileName = window.DisplayFileName == "Unknown file"
                ? ""
                : window.DisplayFileName
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!CapturedWindow.IsAllowedDocumentPath(dialog.FileName))
        {
            MessageBox.Show(
                this,
                "Executable, script, shortcut, installer, and registry files cannot be selected as documents.",
                "Choose a document file",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        window.FilePath = dialog.FileName;
        window.Detection = DetectionKind.UserConfirmed;
        window.DetectionDetail =
            "Confirmed by the user because this program did not expose its path.";
        window.Excluded = false;
        _selectedWindow = window;
        await _layoutStore.SaveAsync(_layout!);
        ActivityText.Text = $"{Path.GetFileName(dialog.FileName)} is now restorable.";
        RefreshView();
    }

    private async void MarkProgramOnlyButton_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_attentionWindow is null)
        {
            return;
        }

        await MarkProgramOnlyAsync(_attentionWindow);
    }

    private async Task MarkProgramOnlyAsync(CapturedWindow window)
    {
        if (_layout is null || !window.HasUsableExecutable)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Workspace Recall will reopen {window.DisplayApplicationName} itself and restore its window position.{Environment.NewLine}{Environment.NewLine}Its internal content, tabs, sessions, and unsaved data may not return.",
            "Mark this window as program only?",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (result != MessageBoxResult.OK)
        {
            return;
        }

        window.FilePath = null;
        window.Detection = DetectionKind.ProgramOnly;
        window.DetectionDetail =
            "Confirmed by the user as a program-only window. Internal content may not return.";
        window.Excluded = false;
        _selectedWindow = window;
        await _layoutStore.SaveAsync(_layout);
        ActivityText.Text = $"{window.DisplayApplicationName} will restore as a program-only window.";
        RefreshView();
    }

    private async void ExcludeButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_layout is null || _attentionWindow is null)
        {
            return;
        }

        _attentionWindow.Excluded = true;
        _previewService.DeletePreview(_attentionWindow);
        if (ReferenceEquals(_selectedWindow, _attentionWindow))
        {
            _selectedWindow = ChooseInitialSelection(_layout);
        }

        await _layoutStore.SaveAsync(_layout);
        ActivityText.Text = "The unresolved window will not be restored.";
        RefreshView();
    }

    private void OpenLocationButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var path = _selectedWindow?.FilePath;
        if (string.IsNullOrWhiteSpace(path) ||
            (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        var arguments = Directory.Exists(path)
            ? $"\"{path}\""
            : $"/select,\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments)
        {
            UseShellExecute = true
        });
    }

    private void RevitIntegrationButton_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_revitBridge.IsEnabled)
        {
            var result = MessageBox.Show(
                this,
                "Disable the optional Revit 2026 integration and remove its saved request/state files? Restart Revit afterward if it is open.",
                "Disable Revit integration?",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (result != MessageBoxResult.OK)
            {
                return;
            }

            _revitBridge.TryDisable(out _revitStatus);
        }
        else
        {
            var result = MessageBox.Show(
                this,
                "This installs an optional per-user Revit 2026 helper. It reads the active Revit document path only when you select Capture current layout. You can disable it here at any time.",
                "Enable Revit integration?",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (result != MessageBoxResult.OK)
            {
                return;
            }

            _revitBridge.TryEnable(out _revitStatus);
        }

        ActivityText.Text = _revitStatus;
        RefreshRevitIntegrationControl();
    }

    private void RefreshRevitIntegrationControl()
    {
        RevitIntegrationButton.Content = _revitBridge.IsEnabled
            ? "Disable Revit integration"
            : "Enable Revit integration";
        RevitIntegrationButton.IsEnabled =
            !_isBusy && (_revitBridge.IsAvailable || _revitBridge.IsEnabled);
        System.Windows.Automation.AutomationProperties.SetName(
            RevitIntegrationButton,
            RevitIntegrationButton.Content.ToString() ?? "Revit integration");
    }

    private void RefreshView()
    {
        var hasLayout = _layout is not null && _layout.Windows.Count > 0;
        EmptyState.Visibility = hasLayout
            ? Visibility.Collapsed
            : Visibility.Visible;
        MonitorCanvas.Visibility = hasLayout
            ? Visibility.Visible
            : Visibility.Collapsed;
        CaptureButtonText.Text = hasLayout
            ? "Capture again"
            : "Capture current layout";
        RestoreButton.IsEnabled = !_isBusy && (_layout?.RestorableCount ?? 0) > 0;

        if (_layout is null)
        {
            CaptureStatusDot.Fill = BrushFromResource("MutedTextBrush");
            CaptureStatusText.Text = "No capture yet";
            WindowCountText.Text = "0 windows";
            DisplayCountText.Text = $"{WindowCaptureService.CaptureDisplays().Count} displays";
            AttentionCountText.Text = "Capture to begin";
            ReadyCountText.Text = "0 of 0 ready";
            CapturedInventorySummary.Text = "0 windows captured";
            CapturedWindowList.ItemsSource = null;
            AttentionPanel.Visibility = Visibility.Collapsed;
            AllReadyText.Visibility = Visibility.Collapsed;
        }
        else
        {
            CaptureStatusDot.Fill = BrushFromResource("SuccessBrush");
            CaptureStatusText.Text = $"Last captured {DescribeAge(_layout.CapturedAt)}";
            WindowCountText.Text = $"{_layout.Windows.Count} windows";
            DisplayCountText.Text = $"{_layout.Displays.Count} displays";
            AttentionCountText.Text = _layout.NeedsAttentionCount == 0
                ? "All included windows ready"
                : $"{_layout.NeedsAttentionCount} need review";
            AttentionCountText.Foreground = _layout.NeedsAttentionCount == 0
                ? BrushFromResource("SuccessBrush")
                : BrushFromResource("WarningBrush");
            ReadyCountText.Text =
                $"{_layout.RestorableCount} of {_layout.Windows.Count(window => !window.Excluded)} ready";
            CapturedInventorySummary.Text =
                $"{_layout.Windows.Count} captured · {_layout.FileIdentifiedCount} files identified · {_layout.ProgramOnlyCount} program only · {_layout.NeedsReviewCount} need review · {_layout.ExcludedCount} excluded";
            _attentionWindow =
                _selectedWindow?.Status == CapturedWindowStatus.NeedsReview
                    ? _selectedWindow
                    : _layout.Windows.FirstOrDefault(window =>
                        window.Status == CapturedWindowStatus.NeedsReview);
            AttentionPanel.Visibility = _attentionWindow is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            AllReadyText.Visibility = _attentionWindow is null
                ? Visibility.Visible
                : Visibility.Collapsed;
            AttentionFileName.Text = _attentionWindow?.DisplayFileName ?? "";
            MarkProgramOnlyButton.IsEnabled =
                _attentionWindow?.HasUsableExecutable == true;
            RenderCapturedWindowInventory();
        }

        RefreshSelectedWindow();
        Dispatcher.BeginInvoke(
            RenderMonitorCanvas,
            DispatcherPriority.Loaded);
    }

    private void RefreshSelectedWindow()
    {
        var hasSelection = _selectedWindow is not null;
        SelectedWindowPanel.Visibility = hasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoSelectionText.Visibility = hasSelection
            ? Visibility.Collapsed
            : Visibility.Visible;
        OpenLocationButton.Visibility = hasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        OpenLocationButton.IsEnabled = _selectedWindow?.HasUsablePath == true;

        if (_selectedWindow is null)
        {
            return;
        }

        SelectedAppIcon.Source =
            AppIconService.TryLoadIcon(
                _selectedWindow.ExecutablePath,
                _selectedWindow.FilePath)
            ?? AppIconService.LoadFallbackIcon();
        SelectedFileName.Text = _selectedWindow.DisplayFileName;
        SelectedApplication.Text = _selectedWindow.DisplayApplicationName;
        SelectedFilePath.Text = _selectedWindow.Status switch
        {
            CapturedWindowStatus.ProgramOnly =>
                "No file required. Internal content, tabs, sessions, and unsaved data may not return.",
            CapturedWindowStatus.NeedsReview => "Not identified",
            _ => _selectedWindow.FilePath ?? "Not identified"
        };
        SelectedDetectionText.Text = _selectedWindow.DetectionLabel;
        SelectedStatusInline.Text = _selectedWindow.StatusLabel;
        SelectedPlacement.Text = _selectedWindow.PlacementLabel;
        SelectedState.Text = _selectedWindow.State.ToString();

        var statusColor = StatusBrush(_selectedWindow.Status);
        var detectionColor = DetectionBrush(_selectedWindow.Detection);
        SelectedStatusDot.Fill = statusColor;
        SelectedDetectionText.Foreground = detectionColor;
        SelectedDetectionBadge.BorderBrush = detectionColor;
        SelectedDetectionBadge.Background = new SolidColorBrush(
            Color.FromArgb(
                36,
                detectionColor.Color.R,
                detectionColor.Color.G,
                detectionColor.Color.B));
    }

    private void RenderCapturedWindowInventory()
    {
        if (_layout is null)
        {
            CapturedWindowList.ItemsSource = null;
            return;
        }

        var groups = _layout.Windows
            .GroupBy(
                window => window.DisplayDeviceName,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => DisplayIndex(group.Key))
            .Select((group, index) =>
            {
                var items = group
                    .OrderBy(window => window.DisplayApplicationName)
                    .ThenBy(window => window.DisplayFileName)
                    .Select(CreateInventoryItem)
                    .ToList();
                return new CapturedWindowInventoryGroup(
                    $"{DisplayLabel(group.Key)} · {items.Count} window{(items.Count == 1 ? "" : "s")}",
                    new Thickness(10, index == 0 ? 2 : 12, 10, 7),
                    items);
            })
            .ToList();
        CapturedWindowList.ItemsSource = groups;
    }

    private CapturedWindowInventoryItem CreateInventoryItem(CapturedWindow window)
    {
        var statusBrush = StatusBrush(window.Status);
        return new CapturedWindowInventoryItem(
            window,
            AppIconService.TryLoadIcon(
                window.ExecutablePath,
                window.FilePath) ?? AppIconService.LoadFallbackIcon(),
            window.DisplayApplicationName,
            window.DisplayFileName,
            window.FilePath ?? window.WindowTitle,
            window.StatusLabel,
            statusBrush,
            new SolidColorBrush(
                Color.FromArgb(
                    35,
                    statusBrush.Color.R,
                    statusBrush.Color.G,
                    statusBrush.Color.B)),
            window.PlacementLabel,
            ReferenceEquals(_selectedWindow, window),
            window.Status == CapturedWindowStatus.NeedsReview
                ? Visibility.Visible
                : Visibility.Collapsed,
            $"CapturedWindowRow_{window.Id}",
            $"Captured window: {window.DisplayApplicationName}, {window.DisplayFileName}, {window.StatusLabel}, {window.PlacementLabel}",
            string.IsNullOrWhiteSpace(window.FilePath)
                ? window.DetectionDetail
                : window.FilePath,
            $"ChooseFile_{window.Id}",
            $"Choose file for {window.DisplayApplicationName}",
            $"ProgramOnly_{window.Id}",
            $"Mark {window.DisplayApplicationName} as program only");
    }

    private void InventoryRow_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: CapturedWindow window })
        {
            _selectedWindow = window;
            RefreshView();
        }
    }

    private async void InventoryChooseFileButton_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: CapturedWindow window })
        {
            await ChooseFileForWindowAsync(window);
        }
    }

    private async void InventoryProgramOnlyButton_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: CapturedWindow window })
        {
            await MarkProgramOnlyAsync(window);
        }
    }

    private int DisplayIndex(string deviceName)
    {
        if (_layout is null)
        {
            return int.MaxValue;
        }

        var index = _layout.Displays.FindIndex(display =>
            display.DeviceName.Equals(
                deviceName,
                StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    private string DisplayLabel(string deviceName)
    {
        var index = DisplayIndex(deviceName);
        return index == int.MaxValue
            ? "Other display"
            : $"Display {index + 1}";
    }

    private void RenderMonitorCanvas()
    {
        MonitorCanvas.Children.Clear();
        if (_layout is null ||
            _layout.Displays.Count == 0 ||
            MonitorCanvasHost.ActualWidth <= 40 ||
            MonitorCanvasHost.ActualHeight <= 80)
        {
            return;
        }

        var minX = _layout.Displays.Min(display => display.Bounds.X);
        var minY = _layout.Displays.Min(display => display.Bounds.Y);
        var maxX = _layout.Displays.Max(display => display.Bounds.Right);
        var maxY = _layout.Displays.Max(display => display.Bounds.Bottom);
        var unionWidth = Math.Max(1, maxX - minX);
        var unionHeight = Math.Max(1, maxY - minY);
        var horizontalPadding = 26.0;
        var verticalPadding = 20.0;
        var labelSpace = 52.0;
        var scale = Math.Min(
            (MonitorCanvasHost.ActualWidth - horizontalPadding * 2) / unionWidth,
            (MonitorCanvasHost.ActualHeight - verticalPadding * 2 - labelSpace) / unionHeight);
        scale = Math.Max(0.03, scale);

        var renderedWidth = unionWidth * scale;
        var renderedHeight = unionHeight * scale;
        var originX = Math.Max(
            horizontalPadding,
            (MonitorCanvasHost.ActualWidth - renderedWidth) / 2);
        var originY = verticalPadding + 34;

        for (var index = 0; index < _layout.Displays.Count; index++)
        {
            var display = _layout.Displays[index];
            var displayX = originX + (display.Bounds.X - minX) * scale;
            var displayY = originY + (display.Bounds.Y - minY) * scale;
            var displayWidth = Math.Max(140, display.Bounds.Width * scale);
            var displayHeight = Math.Max(90, display.Bounds.Height * scale);
            RenderDisplay(
                display,
                index,
                displayX,
                displayY,
                displayWidth,
                displayHeight,
                scale);
        }
    }

    private void RenderDisplay(
        DisplaySnapshot display,
        int displayIndex,
        double x,
        double y,
        double width,
        double height,
        double scale)
    {
        var screenCanvas = new Canvas
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 22, 24)),
            ClipToBounds = true
        };
        var frame = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.FromRgb(30, 36, 38)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(71, 79, 82)),
            BorderThickness = new Thickness(7),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5),
            Child = screenCanvas
        };
        Canvas.SetLeft(frame, x);
        Canvas.SetTop(frame, y);
        MonitorCanvas.Children.Add(frame);

        var innerWidth = Math.Max(1, width - 24);
        var innerHeight = Math.Max(1, height - 24);
        var allDisplayWindows = _layout!.Windows
            .Where(window =>
                window.DisplayDeviceName.Equals(
                    display.DeviceName,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        var displayWindows = allDisplayWindows
            .OrderByDescending(window => window.ZOrder)
            .ToList();

        foreach (var window in displayWindows)
        {
            var sourceBounds = window.State == SavedWindowState.Maximized
                ? display.WorkArea
                : window.Bounds;
            var left = (sourceBounds.X - display.Bounds.X) * scale;
            var top = (sourceBounds.Y - display.Bounds.Y) * scale;
            var tileWidth = Math.Clamp(sourceBounds.Width * scale, 58, innerWidth);
            var tileHeight = Math.Clamp(sourceBounds.Height * scale, 42, innerHeight);
            left = Math.Clamp(left, 0, Math.Max(0, innerWidth - tileWidth));
            top = Math.Clamp(top, 0, Math.Max(0, innerHeight - tileHeight));

            var selected = ReferenceEquals(_selectedWindow, window);
            var statusBrush = StatusBrush(window.Status);
            var tile = new Button
            {
                Style = (Style)FindResource("MonitorTileButtonStyle"),
                Tag = window,
                Width = tileWidth,
                Height = tileHeight,
                Padding = new Thickness(7, 5, 7, 5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromRgb(22, 28, 30)),
                BorderBrush = selected
                    ? BrushFromResource("AccentBrush")
                    : new SolidColorBrush(Color.FromRgb(64, 73, 76)),
                BorderThickness = new Thickness(selected ? 2 : 1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Opacity = window.Excluded && !selected ? 0.52 : 1,
                ToolTip =
                    $"{window.DisplayFileName}\n{window.PlacementLabel}{(window.Excluded ? "\nExcluded from restore" : "")}"
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(
                tile,
                $"MonitorWindowTile_{window.Id}");
            System.Windows.Automation.AutomationProperties.SetName(
                tile,
                $"Monitor window: {window.DisplayApplicationName}, {window.DisplayFileName}");
            System.Windows.Automation.AutomationProperties.SetItemStatus(
                tile,
                selected ? "Selected" : "");
            tile.Click += WindowTile_Click;

            var tileGrid = new Grid();
            var preview = WindowPreviewService.TryLoadPreview(
                window.PreviewImagePath);
            if (preview is not null)
            {
                tileGrid.Children.Add(new Image
                {
                    Source = preview,
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.82
                });
            }

            var header = new Grid
            {
                Height = 28,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(
                    Color.FromArgb(220, 22, 28, 30))
            };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            var icon = new Image
            {
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                Source = AppIconService.TryLoadIcon(
                             window.ExecutablePath,
                             window.FilePath)
                         ?? AppIconService.LoadFallbackIcon()
            };
            var label = new TextBlock
            {
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = tileWidth < 100 ? 9 : 11,
                Text = window.DisplayFileName,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 1);
            var status = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = statusBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(status, 2);
            header.Children.Add(icon);
            header.Children.Add(label);
            header.Children.Add(status);
            tileGrid.Children.Add(header);
            tile.Content = tileGrid;

            Canvas.SetLeft(tile, left);
            Canvas.SetTop(tile, top);
            Canvas.SetZIndex(tile, selected ? 2000 : 1000 - window.ZOrder);
            screenCanvas.Children.Add(tile);
        }

        var countBadge = new Border
        {
            Width = 78,
            Height = 24,
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromArgb(225, 19, 25, 27)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(77, 88, 92)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Text = $"{allDisplayWindows.Count} windows"
            }
        };
        Canvas.SetLeft(countBadge, Math.Max(4, innerWidth - countBadge.Width - 7));
        Canvas.SetTop(countBadge, Math.Max(4, innerHeight - countBadge.Height - 7));
        Canvas.SetZIndex(countBadge, 2000);
        screenCanvas.Children.Add(countBadge);

        var stand = new Border
        {
            Width = Math.Max(28, width * 0.23),
            Height = 5,
            Background = new SolidColorBrush(Color.FromRgb(71, 79, 82)),
            CornerRadius = new CornerRadius(2)
        };
        Canvas.SetLeft(stand, x + (width - stand.Width) / 2);
        Canvas.SetTop(stand, y + height + 8);
        MonitorCanvas.Children.Add(stand);

        var labelText = new TextBlock
        {
            Width = width,
            TextAlignment = TextAlignment.Center,
            Foreground = BrushFromResource("MutedTextBrush"),
            Text = $"Display {displayIndex + 1}"
        };
        Canvas.SetLeft(labelText, x);
        Canvas.SetTop(labelText, y + height + 17);
        MonitorCanvas.Children.Add(labelText);
    }

    private void WindowTile_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: CapturedWindow window })
        {
            _selectedWindow = window;
            RefreshView();
        }
    }

    private void MonitorCanvasHost_SizeChanged(
        object sender,
        SizeChangedEventArgs eventArgs) =>
        RenderMonitorCanvas();

    private void SetBusy(bool busy, string? activity = null)
    {
        _isBusy = busy;
        CaptureButton.IsEnabled = !busy;
        RefreshRevitIntegrationControl();
        RestoreButton.IsEnabled =
            !busy && (_layout?.RestorableCount ?? 0) > 0;
        if (activity is not null)
        {
            ActivityText.Text = activity;
        }
    }

    private SolidColorBrush DetectionBrush(DetectionKind detection) =>
        (SolidColorBrush)FindResource(detection switch
        {
            DetectionKind.ExactPath => "SuccessBrush",
            DetectionKind.CommandLine => "AccentBrush",
            DetectionKind.UserConfirmed => "AccentBrush",
            DetectionKind.ProgramOnly => "AccentBrush",
            _ => "WarningBrush"
        });

    private SolidColorBrush StatusBrush(CapturedWindowStatus status) =>
        (SolidColorBrush)FindResource(status switch
        {
            CapturedWindowStatus.FileIdentified => "SuccessBrush",
            CapturedWindowStatus.ProgramOnly => "AccentBrush",
            CapturedWindowStatus.Excluded => "MutedTextBrush",
            _ => "WarningBrush"
        });

    private SolidColorBrush BrushFromResource(string key) =>
        (SolidColorBrush)FindResource(key);

    private static CapturedWindow? ChooseInitialSelection(
        WorkspaceLayout layout) =>
        layout.Windows.FirstOrDefault(window =>
            window.Status == CapturedWindowStatus.NeedsReview)
        ?? layout.Windows.FirstOrDefault(window => window.IsRestoreReady)
        ?? layout.Windows.FirstOrDefault();

    private static string DescribeAge(DateTimeOffset capturedAt)
    {
        var age = DateTimeOffset.Now - capturedAt;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)Math.Round(age.TotalMinutes));
            return $"{minutes} minute{(minutes == 1 ? "" : "s")} ago";
        }

        if (capturedAt.Date == DateTimeOffset.Now.Date)
        {
            return $"today at {capturedAt:HH:mm}";
        }

        return capturedAt.ToString("dd MMM yyyy 'at' HH:mm");
    }

    private void Window_SourceInitialized(object? sender, EventArgs eventArgs)
    {
        var darkMode = 1;
        NativeMethods.DwmSetWindowAttribute(
            new WindowInteropHelper(this).Handle,
            20,
            ref darkMode,
            sizeof(int));
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) =>
        Close();

    private void Window_StateChanged(object? sender, EventArgs eventArgs)
    {
        if (MaximizeGlyph is null || MaximizeButton is null)
        {
            return;
        }

        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Text = isMaximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = isMaximized ? "Restore" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(
            MaximizeButton,
            isMaximized ? "Restore" : "Maximize");
    }

    private sealed record CapturedWindowInventoryGroup(
        string Header,
        Thickness HeaderMargin,
        IReadOnlyList<CapturedWindowInventoryItem> Items);

    private sealed record CapturedWindowInventoryItem(
        CapturedWindow Window,
        ImageSource Icon,
        string ApplicationName,
        string CapturedItem,
        string CapturedItemToolTip,
        string StatusLabel,
        SolidColorBrush StatusBrush,
        SolidColorBrush StatusBackground,
        string Placement,
        bool IsSelected,
        Visibility ActionVisibility,
        string AutomationId,
        string AutomationName,
        string HelpText,
        string ChooseFileAutomationId,
        string ChooseFileAutomationName,
        string ProgramOnlyAutomationId,
        string ProgramOnlyAutomationName);
}
