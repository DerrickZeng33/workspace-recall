using System.Security.AccessControl;
using System.Security.Principal;
using WorkspaceRecall.App.Models;
using WorkspaceRecall.App.Services;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Command line paths are discovered dynamically", TestCommandLinePathDiscovery),
    ("Unquoted paths with spaces are reconstructed", TestUnquotedPathDiscovery),
    ("Executable paths are not mistaken for documents", TestExecutableIsIgnored),
    ("Layout persistence round-trips", TestLayoutPersistence),
    ("Window bounds remap across monitor sizes", TestMonitorRemapping),
    ("Placement labels identify snapped halves", TestPlacementLabels),
    ("Excluded and unresolved windows are counted correctly", TestLayoutCounts),
    ("Captured windows report identification and restore readiness separately", TestCaptureInventoryStatuses),
    ("Program-only confirmation survives recapture", TestProgramOnlyDecisionSurvivesRecapture),
    ("Restore plan includes files and confirmed program-only windows", TestProgramOnlyRestorePlan),
    ("Program-only restore reserves a separate target for each captured window", TestProgramOnlyTargetReservation),
    ("Captured-window inventory uses a recognizable program name", TestInventoryProgramName),
    ("Local capture data is restricted to the current Windows account", TestPrivateDataDirectory),
    ("Excluded and opt-out windows do not retain saved previews", TestPreviewDeletion),
    ("Restore rejects executable and script paths outside program-only mode", TestDangerousRestorePaths),
    ("Revit integration is explicit, on demand, and removable", TestRevitIntegrationLifecycle)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}");
    }
}

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        var displays = WindowCaptureService.CaptureDisplays();
        Assert(displays.Count > 0, "No Windows displays were enumerated.");
        Console.WriteLine($"PASS  Live display enumeration ({displays.Count} found)");
        var commandLine = DocumentPathResolver.TryReadProcessCommandLine(Environment.ProcessId);
        Assert(
            !string.IsNullOrWhiteSpace(commandLine),
            "The current process command line was not read through WMI.");
        Console.WriteLine("PASS  Live process command-line query");
    }
    catch (Exception exception)
    {
        failures.Add($"Live display enumeration: {exception.Message}");
        Console.WriteLine("FAIL  Live display enumeration");
    }
}

if (args.Contains("--live-office-adapters", StringComparer.OrdinalIgnoreCase))
{
    foreach (var processName in new[] { "EXCEL", "WINWORD" })
    {
        var process = System.Diagnostics.Process
            .GetProcessesByName(processName)
            .FirstOrDefault(candidate => candidate.MainWindowHandle != nint.Zero);
        if (process is null)
        {
            Console.WriteLine($"SKIP  {processName} adapter (application is not open)");
            continue;
        }

        using (process)
        {
            var window = new CapturedWindow
            {
                ProcessId = process.Id,
                ProcessName = processName,
                ApplicationName = processName,
                ExecutablePath = process.MainModule?.FileName ?? "",
                WindowTitle = process.MainWindowTitle,
                WindowHandle = process.MainWindowHandle
            };
            var resolver = new DocumentPathResolver();
            resolver.Resolve([window]);
            if (!window.HasUsablePath)
            {
                failures.Add(
                    $"{processName} resolver: expected a usable path, got {window.Detection} ({window.FilePath ?? "no path"}). {string.Join(" | ", resolver.AdapterDiagnostics)}");
                Console.WriteLine($"FAIL  {processName} adapter");
            }
            else
            {
                Console.WriteLine(
                    $"PASS  {processName} resolver [{window.Detection}] ({window.FilePath})");
            }
        }
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Failures:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine();
Console.WriteLine($"All {tests.Count + (args.Contains("--live") ? 2 : 0)} checks passed.");
return 0;

static Task TestCommandLinePathDiscovery()
{
    return WithTemporaryDirectory(directory =>
    {
        var documentPath = Path.Combine(directory, "Budget FY26.xlsx");
        File.WriteAllText(documentPath, "test");
        var commandLine = $"\"C:\\Tools\\sample.exe\" --reuse-window \"{documentPath}\"";
        var paths = DocumentPathResolver.ExtractExistingPaths(commandLine);

        Assert(paths.Count == 1, $"Expected one path, got {paths.Count}.");
        Assert(
            string.Equals(paths[0], documentPath, StringComparison.OrdinalIgnoreCase),
            "The discovered path did not match the document argument.");
        return Task.CompletedTask;
    });
}

static Task TestExecutableIsIgnored()
{
    return WithTemporaryDirectory(directory =>
    {
        var executablePath = Path.Combine(directory, "sample.exe");
        var documentPath = Path.Combine(directory, "Site Plan.dwg");
        File.WriteAllBytes(executablePath, []);
        File.WriteAllText(documentPath, "drawing");
        var commandLine = $"\"{executablePath}\" \"{documentPath}\"";
        var paths = DocumentPathResolver.ExtractExistingPaths(
            commandLine,
            executablePath);

        Assert(paths.Count == 1, $"Expected one document path, got {paths.Count}.");
        Assert(
            string.Equals(paths[0], documentPath, StringComparison.OrdinalIgnoreCase),
            "The executable was not filtered from the results.");
        return Task.CompletedTask;
    });
}

static Task TestUnquotedPathDiscovery()
{
    return WithTemporaryDirectory(directory =>
    {
        var nestedDirectory = Path.Combine(directory, "Folder With Spaces");
        Directory.CreateDirectory(nestedDirectory);
        var documentPath = Path.Combine(nestedDirectory, "WorkspaceRecall.sln");
        File.WriteAllText(documentPath, "test");
        var commandLine = $"\"C:\\Windows\\system32\\notepad.exe\" {documentPath}";
        var paths = DocumentPathResolver.ExtractExistingPaths(commandLine);

        Assert(
            paths.Contains(documentPath, StringComparer.OrdinalIgnoreCase),
            "The unquoted path was not reconstructed.");
        return Task.CompletedTask;
    });
}

static Task TestLayoutPersistence()
{
    return WithTemporaryDirectory(async directory =>
    {
        var documentPath = Path.Combine(directory, "Training Plan.docx");
        File.WriteAllText(documentPath, "test");
        var store = new LayoutStore(Path.Combine(directory, "layout.json"));
        var layout = new WorkspaceLayout
        {
            CapturedAt = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(8)),
            Displays =
            [
                new DisplaySnapshot(
                    "\\\\.\\DISPLAY1",
                    new PixelRect(0, 0, 1920, 1080),
                    new PixelRect(0, 0, 1920, 1040),
                    true)
            ],
            Windows =
            [
                new CapturedWindow
                {
                    ProcessName = "WINWORD",
                    ApplicationName = "Microsoft Word",
                    FilePath = documentPath,
                    Detection = DetectionKind.ExactPath,
                    Bounds = new PixelRect(960, 0, 960, 1040),
                    DisplayDeviceName = "\\\\.\\DISPLAY1",
                    PlacementLabel = "Display 1 · Right half"
                }
            ]
        };

        await store.SaveAsync(layout);
        var restored = await store.LoadAsync()
            ?? throw new InvalidOperationException("The saved layout was not loaded.");
        Assert(restored.Windows.Count == 1, "The saved window count changed.");
        Assert(
            restored.Windows[0].Detection == DetectionKind.ExactPath,
            "The detection enum did not round-trip.");
        Assert(
            restored.Windows[0].FilePath == documentPath,
            "The file path did not round-trip.");
    });
}

static Task TestMonitorRemapping()
{
    var savedDisplay = new DisplaySnapshot(
        "\\\\.\\DISPLAY1",
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(0, 0, 1920, 1040),
        true);
    var currentDisplay = new DisplaySnapshot(
        "\\\\.\\DISPLAY1",
        new PixelRect(0, 0, 2560, 1440),
        new PixelRect(0, 0, 2560, 1400),
        true);
    var window = new CapturedWindow
    {
        Bounds = new PixelRect(960, 0, 960, 1040),
        DisplayDeviceName = "\\\\.\\DISPLAY1"
    };

    var mapped = WindowRestoreService.MapBounds(
        window,
        [savedDisplay],
        [currentDisplay]);
    Assert(mapped.X == 1280, $"Expected X 1280, got {mapped.X}.");
    Assert(mapped.Width == 1280, $"Expected width 1280, got {mapped.Width}.");
    Assert(mapped.Height == 1400, $"Expected height 1400, got {mapped.Height}.");
    return Task.CompletedTask;
}

static Task TestPlacementLabels()
{
    var display = new DisplaySnapshot(
        "\\\\.\\DISPLAY1",
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(0, 0, 1920, 1040),
        true);
    var left = WindowCaptureService.DescribePlacement(
        new PixelRect(0, 0, 960, 1040),
        SavedWindowState.Normal,
        display,
        [display]);
    var right = WindowCaptureService.DescribePlacement(
        new PixelRect(960, 0, 960, 1040),
        SavedWindowState.Normal,
        display,
        [display]);

    Assert(left.EndsWith("Left half"), $"Unexpected left label: {left}");
    Assert(right.EndsWith("Right half"), $"Unexpected right label: {right}");
    return Task.CompletedTask;
}

static Task TestLayoutCounts()
{
    return WithTemporaryDirectory(directory =>
    {
        var readyPath = Path.Combine(directory, "ready.txt");
        File.WriteAllText(readyPath, "ready");
        var layout = new WorkspaceLayout
        {
            Windows =
            [
                new CapturedWindow { FilePath = readyPath },
                new CapturedWindow(),
                new CapturedWindow { Excluded = true }
            ]
        };

        Assert(layout.RestorableCount == 1, "Restorable count should be one.");
        Assert(layout.NeedsAttentionCount == 1, "Needs-attention count should be one.");
        return Task.CompletedTask;
    });
}

static Task TestCaptureInventoryStatuses()
{
    return WithTemporaryDirectory(directory =>
    {
        var documentPath = Path.Combine(directory, "2D Plan.dwg");
        var executablePath = Path.Combine(directory, "TrainingDeliveryManager.exe");
        File.WriteAllText(documentPath, "drawing");
        File.WriteAllBytes(executablePath, []);
        var fileIdentified = new CapturedWindow
        {
            FilePath = documentPath,
            Detection = DetectionKind.ExactPath
        };
        var programOnly = new CapturedWindow
        {
            ExecutablePath = executablePath,
            Detection = DetectionKind.ProgramOnly
        };
        var needsReview = new CapturedWindow();
        var excluded = new CapturedWindow
        {
            FilePath = documentPath,
            Detection = DetectionKind.ExactPath,
            Excluded = true
        };
        var layout = new WorkspaceLayout
        {
            Windows = [fileIdentified, programOnly, needsReview, excluded]
        };

        Assert(
            fileIdentified.Status == CapturedWindowStatus.FileIdentified,
            "A verified document should be file identified.");
        Assert(
            programOnly.Status == CapturedWindowStatus.ProgramOnly,
            "A confirmed executable should be program only.");
        Assert(
            needsReview.Status == CapturedWindowStatus.NeedsReview,
            "An unresolved window should need review.");
        Assert(
            excluded.Status == CapturedWindowStatus.Excluded,
            "Exclusion should override identification.");
        Assert(layout.FileIdentifiedCount == 1, "One file should be identified.");
        Assert(layout.ProgramOnlyCount == 1, "One program-only window was expected.");
        Assert(layout.NeedsReviewCount == 1, "One window should need review.");
        Assert(layout.ExcludedCount == 1, "One window should be excluded.");
        Assert(layout.RestorableCount == 2, "Two windows should be restore ready.");
        Assert(layout.NeedsAttentionCount == 1, "Only needs-review windows require attention.");
        return Task.CompletedTask;
    });
}

static Task TestProgramOnlyDecisionSurvivesRecapture()
{
    return WithTemporaryDirectory(directory =>
    {
        var executablePath = Path.Combine(directory, "TrainingDeliveryManager.exe");
        File.WriteAllBytes(executablePath, []);
        var previousLayout = new WorkspaceLayout
        {
            Windows =
            [
                new CapturedWindow
                {
                    ExecutablePath = executablePath,
                    Detection = DetectionKind.ProgramOnly
                }
            ]
        };
        var recapturedWindow = new CapturedWindow
        {
            ExecutablePath = executablePath.ToUpperInvariant(),
            Detection = DetectionKind.NeedsFile
        };
        var recapturedLayout = new WorkspaceLayout
        {
            Windows = [recapturedWindow]
        };

        recapturedLayout.ApplyRememberedDecisions(previousLayout);

        Assert(
            recapturedWindow.Detection == DetectionKind.ProgramOnly,
            "The program-only decision was not carried into the new capture.");
        Assert(
            recapturedWindow.Status == CapturedWindowStatus.ProgramOnly,
            "The recaptured window should be restore ready as program only.");
        return Task.CompletedTask;
    });
}

static Task TestProgramOnlyRestorePlan()
{
    return WithTemporaryDirectory(directory =>
    {
        var documentPath = Path.Combine(directory, "Training Plan.docx");
        var documentAppPath = Path.Combine(directory, "word.exe");
        var programOnlyPath = Path.Combine(directory, "TrainingDeliveryManager.exe");
        File.WriteAllText(documentPath, "document");
        File.WriteAllBytes(documentAppPath, []);
        File.WriteAllBytes(programOnlyPath, []);
        var fileWindow = new CapturedWindow
        {
            ExecutablePath = documentAppPath,
            FilePath = documentPath,
            Detection = DetectionKind.ExactPath,
            ZOrder = 2
        };
        var programOnlyWindow = new CapturedWindow
        {
            ExecutablePath = programOnlyPath,
            Detection = DetectionKind.ProgramOnly,
            ZOrder = 3
        };
        var layout = new WorkspaceLayout
        {
            Windows =
            [
                new CapturedWindow
                {
                    ExecutablePath = programOnlyPath,
                    Detection = DetectionKind.NeedsFile
                },
                fileWindow,
                programOnlyWindow,
                new CapturedWindow
                {
                    ExecutablePath = programOnlyPath,
                    Detection = DetectionKind.ProgramOnly,
                    Excluded = true
                }
            ]
        };

        var plan = WindowRestoreService.BuildLaunchPlan(layout);

        Assert(plan.Count == 2, $"Expected two restore launches, got {plan.Count}.");
        var fileLaunch = plan.Single(item => ReferenceEquals(item.Window, fileWindow));
        Assert(
            fileLaunch.FileName == documentAppPath,
            "The file-backed window should use its saved application.");
        Assert(
            fileLaunch.Arguments.SequenceEqual([documentPath]),
            "The file-backed window should pass the document path once.");
        var programLaunch = plan.Single(item =>
            ReferenceEquals(item.Window, programOnlyWindow));
        Assert(
            programLaunch.FileName == programOnlyPath,
            "The program-only window should launch its executable.");
        Assert(
            programLaunch.Arguments.Count == 0,
            "A program-only launch must not invent a file argument.");
        return Task.CompletedTask;
    });
}

static Task TestProgramOnlyTargetReservation()
{
    var firstWindow = new CapturedWindow
    {
        ProcessName = "ProgramOnlyManager",
        WindowTitle = "Program Only Manager",
        Detection = DetectionKind.ProgramOnly
    };
    var secondWindow = new CapturedWindow
    {
        ProcessName = "ProgramOnlyManager",
        WindowTitle = "Program Only Manager",
        Detection = DetectionKind.ProgramOnly
    };
    var candidates = new[]
    {
        new WindowMatchCandidate(
            (nint)101,
            10,
            "Program Only Manager",
            "ProgramOnlyManager"),
        new WindowMatchCandidate(
            (nint)202,
            20,
            "Program Only Manager",
            "ProgramOnlyManager")
    };
    var claimedHandles = new HashSet<nint>();

    var firstHandle = WindowRestoreService.SelectTargetWindow(
        candidates,
        firstWindow,
        startedProcessId: null,
        claimedHandles);
    claimedHandles.Add(firstHandle);
    var secondHandle = WindowRestoreService.SelectTargetWindow(
        candidates,
        secondWindow,
        startedProcessId: null,
        claimedHandles);

    Assert(firstHandle != nint.Zero, "The first program-only window was not matched.");
    Assert(secondHandle != nint.Zero, "The second program-only window was not matched.");
    Assert(
        firstHandle != secondHandle,
        "Two captured windows must not claim the same restored window.");
    return Task.CompletedTask;
}

static Task TestInventoryProgramName()
{
    var programOnlyManager = new CapturedWindow
    {
        ProcessName = "ProgramOnlyManager.WinUI",
        ApplicationName = "ProgramOnlyManager.WinUI",
        WindowTitle = "Program Only Manager"
    };
    var autoCad = new CapturedWindow
    {
        ProcessName = "acad",
        ApplicationName = "AutoCAD Application",
        WindowTitle = "Autodesk AutoCAD 2026 - [2D Plan.dwg]"
    };
    var documentTitle = new CapturedWindow
    {
        ProcessName = "SomeApp.WinUI",
        ApplicationName = "SomeApp.WinUI",
        WindowTitle = "book1.xlsx - Some App"
    };

    Assert(
        programOnlyManager.DisplayApplicationName == "Program Only Manager",
        "An internal process-style description should be made readable.");
    Assert(
        autoCad.DisplayApplicationName == "AutoCAD Application",
        "A recognizable application description should be preserved.");
    Assert(
        documentTitle.DisplayApplicationName == "Some App",
        "A document title must not replace the application name.");
    return Task.CompletedTask;
}

static Task TestPrivateDataDirectory()
{
    return WithTemporaryDirectory(directory =>
    {
        var worldSid = new SecurityIdentifier(
            WellKnownSidType.WorldSid,
            domainSid: null);
        var insecureRule = new FileSystemAccessRule(
            worldSid,
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);
        var insecureAcl = new DirectorySecurity();
        insecureAcl.AddAccessRule(insecureRule);
        new DirectoryInfo(directory).SetAccessControl(insecureAcl);
        var existingFile = Path.Combine(directory, "default-layout.json");
        File.WriteAllText(existingFile, "{}");

        PrivateDataDirectory.EnsureSecure(directory);

        var directoryRules = new DirectoryInfo(directory)
            .GetAccessControl()
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .ToList();
        var fileRules = new FileInfo(existingFile)
            .GetAccessControl()
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .ToList();
        var broadSids = new[]
        {
            worldSid,
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)
        };

        Assert(
            !directoryRules.Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                broadSids.Contains((SecurityIdentifier)rule.IdentityReference)),
            "The app-data directory still grants broad read access.");
        Assert(
            !fileRules.Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                broadSids.Contains((SecurityIdentifier)rule.IdentityReference)),
            "An existing app-data file still grants broad read access.");
        var currentUserSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows user has no SID.");
        Assert(
            directoryRules.Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                currentUserSid.Equals(rule.IdentityReference) &&
                rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)),
            "The current Windows account does not retain full control.");
        return Task.CompletedTask;
    });
}

static Task TestPreviewDeletion()
{
    return WithTemporaryDirectory(directory =>
    {
        var previewDirectory = Path.Combine(directory, "previews");
        var outsidePreview = Path.Combine(directory, "outside.png");
        var service = new WindowPreviewService(previewDirectory);
        var firstPreview = Path.Combine(previewDirectory, "first.png");
        var secondPreview = Path.Combine(previewDirectory, "second.png");
        File.WriteAllBytes(firstPreview, [1]);
        File.WriteAllBytes(secondPreview, [2]);
        File.WriteAllBytes(outsidePreview, [3]);
        var firstWindow = new CapturedWindow { PreviewImagePath = firstPreview };
        var secondWindow = new CapturedWindow { PreviewImagePath = secondPreview };
        var outsideWindow = new CapturedWindow { PreviewImagePath = outsidePreview };

        service.DeletePreview(firstWindow);
        service.DeletePreviews(new WorkspaceLayout
        {
            Windows = [secondWindow, outsideWindow]
        });

        Assert(!File.Exists(firstPreview), "The excluded window preview was retained.");
        Assert(!File.Exists(secondPreview), "The opt-out layout preview was retained.");
        Assert(File.Exists(outsidePreview), "A preview outside the private directory was deleted.");
        Assert(firstWindow.PreviewImagePath is null, "The excluded window still references a preview.");
        Assert(secondWindow.PreviewImagePath is null, "The opt-out window still references a preview.");
        Assert(outsideWindow.PreviewImagePath is null, "The unsafe external preview reference was retained.");
        return Task.CompletedTask;
    });
}

static Task TestDangerousRestorePaths()
{
    return WithTemporaryDirectory(directory =>
    {
        var documentPath = Path.Combine(directory, "Site Plan.dwg");
        var executablePath = Path.Combine(directory, "trusted.exe");
        var scriptPath = Path.Combine(directory, "layout.cmd");
        File.WriteAllText(documentPath, "drawing");
        File.WriteAllBytes(executablePath, []);
        File.WriteAllText(scriptPath, "echo unsafe");
        var documentWindow = new CapturedWindow
        {
            FilePath = documentPath,
            Detection = DetectionKind.ExactPath
        };
        var executableAsDocument = new CapturedWindow
        {
            FilePath = executablePath,
            Detection = DetectionKind.UserConfirmed
        };
        var scriptAsProgram = new CapturedWindow
        {
            ExecutablePath = scriptPath,
            Detection = DetectionKind.ProgramOnly
        };
        var executableAsProgram = new CapturedWindow
        {
            ExecutablePath = executablePath,
            Detection = DetectionKind.ProgramOnly
        };

        Assert(documentWindow.HasUsablePath, "A normal document path was rejected.");
        Assert(
            !executableAsDocument.HasUsablePath,
            "An executable was accepted as a document.");
        Assert(
            !scriptAsProgram.HasUsableExecutable,
            "A command script was accepted as a program executable.");
        Assert(
            executableAsProgram.HasUsableExecutable,
            "A captured executable was rejected from program-only mode.");
        var plan = WindowRestoreService.BuildLaunchPlan(new WorkspaceLayout
        {
            Windows =
            [
                documentWindow,
                executableAsDocument,
                scriptAsProgram,
                executableAsProgram
            ]
        });
        Assert(plan.Count == 2, $"Expected two safe launches, got {plan.Count}.");
        return Task.CompletedTask;
    });
}

static Task TestRevitIntegrationLifecycle()
{
    return WithTemporaryDirectory(async directory =>
    {
        var dataDirectory = Path.Combine(directory, "private-data");
        var manifestPath = Path.Combine(directory, "Revit", "WorkspaceRecall.addin");
        var bundledAddInPath = Path.Combine(directory, "bundle", "WorkspaceRecall.RevitAddin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(bundledAddInPath)!);
        File.WriteAllBytes(bundledAddInPath, [1, 2, 3]);
        var installer = new RevitBridgeInstaller(
            dataDirectory,
            manifestPath,
            bundledAddInPath);

        Assert(!installer.IsEnabled, "Revit integration should start disabled.");
        Assert(
            installer.TryEnable(out var enableStatus),
            $"Revit integration did not enable: {enableStatus}");
        Assert(installer.IsEnabled, "The enabled Revit integration was not detected.");
        var installedAddInPath = Path.Combine(
            dataDirectory,
            "RevitAddin",
            "WorkspaceRecall.RevitAddin.dll");
        Assert(File.Exists(installedAddInPath), "The helper was not copied to private storage.");
        Assert(
            File.ReadAllText(manifestPath).Contains(
                installedAddInPath,
                StringComparison.OrdinalIgnoreCase),
            "The manifest does not target the private helper copy.");

        var requestTask = installer.RequestSnapshotAsync(
            TimeSpan.FromSeconds(2));
        var requestTimer = System.Diagnostics.Stopwatch.StartNew();
        while (!File.Exists(installer.RequestPath) &&
               requestTimer.Elapsed < TimeSpan.FromSeconds(1))
        {
            await Task.Delay(20);
        }

        Assert(File.Exists(installer.RequestPath), "No on-demand request was created.");
        using var requestDocument = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(installer.RequestPath));
        var requestId = requestDocument.RootElement
            .GetProperty("requestId")
            .GetString();
        File.WriteAllText(
            installer.StatePath,
            $$"""
              {
                "requestId": "{{requestId}}",
                "processId": 123,
                "mainWindowHandle": 456,
                "documentPath": "C:\\Projects\\Example.rvt",
                "updatedAtUtc": "{{DateTimeOffset.UtcNow:O}}"
              }
              """);
        File.Delete(installer.RequestPath);

        Assert(
            await requestTask,
            "The matching Revit response was not accepted.");
        Assert(
            installer.TryDisable(out var disableStatus),
            $"Revit integration did not disable: {disableStatus}");
        Assert(!installer.IsEnabled, "The Revit manifest was retained.");
        Assert(!File.Exists(installer.RequestPath), "The Revit request was retained.");
        Assert(!File.Exists(installer.StatePath), "The Revit state was retained.");
    });
}

static async Task WithTemporaryDirectory(Func<string, Task> action)
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "WorkspaceRecallTests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        await action(directory);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
