# Workspace Recall

Workspace Recall is an early Windows desktop prototype that saves the
positions of user-facing application windows and, when an application exposes
one, the exact local file or folder to reopen. Selecting **Restore layout**
launches the ready entries and returns their windows to the recorded displays,
positions, sizes, and states.

The app is dynamic rather than tied to a fixed list of file extensions, but it
cannot guarantee file detection for every Windows application. Applications
that do not expose a verifiable path are shown as **Needs review** until the
user chooses a file, confirms **Program only**, or excludes the window.

## Current scope

- One saved layout named Default Layout.
- A complete captured-window inventory with **File identified**,
  **Program only**, **Needs review**, or **Excluded** status.
- Multi-monitor placement and normal, minimized, or maximized window state.
- File detection through Microsoft Office and AutoCAD automation, an optional
  Revit 2026 helper, and a generic existing-path resolver.
- Manual file selection when automatic detection is unavailable.
- Optional local window previews, disabled by default.

Unsaved documents cannot be reopened because they do not have a file path.
Program-only restoration reopens an application, but cannot promise that the
application's internal tabs, session, or unsaved state will return.

## Privacy and security

Workspace Recall has no built-in network communication, telemetry, analytics,
accounts, uploads, input hooks, startup service, or remote-control feature. It
does not request administrator privileges. Restoring a path on a network share
can still cause Windows and the target application to access that share.

The saved layout can contain private window titles and local file, folder, and
executable paths. Optional previews can contain anything visible in a window.
Data stays under `%LOCALAPPDATA%\WorkspaceRecall`, protected for the current
Windows user, local system, and local administrators. See [PRIVACY.md](PRIVACY.md)
for the complete data lifecycle.

Restore is an explicit user action. It can launch captured applications and
safe local document paths, then move or change the state of matched windows.
Executable, script, installer, shortcut, and registry paths are not accepted as
documents. This extension check is defense in depth; a trusted application can
still execute active content inside an otherwise allowed document format. See
[SECURITY.md](SECURITY.md) for the security boundary and reporting
instructions.

The optional Revit integration is disabled by default. The user must enable it
in the app, and its helper responds only to a fresh capture request. Disabling
the integration removes its per-user manifest and local request/state data.

No public binary is provided yet. Local builds are unsigned and should not be
redistributed as trusted releases.

## Build

Requirements:

- Windows 10 or 11;
- .NET 8 SDK; and
- optionally, Autodesk Revit 2026 for building the Revit helper.

Build the portable, framework-dependent output:

```powershell
.\build.ps1 -Configuration Release
```

The script locates a local Revit 2026 installation when available. To provide
the API location explicitly:

```powershell
.\build.ps1 -Configuration Release -RevitApiPath 'C:\Program Files\Autodesk\Revit 2026'
```

If the Revit API assemblies are unavailable, the main app is still published
without the optional helper. The output is placed in
`dist\WorkspaceRecall-win-x64`.

## Verify

Run the automated behavior tests:

```powershell
dotnet run --project .\tests\WorkspaceRecall.Tests\WorkspaceRecall.Tests.csproj -c Release
```

Run the Windows live-capture tests and UI inventory check:

```powershell
dotnet run --project .\tests\WorkspaceRecall.Tests\WorkspaceRecall.Tests.csproj -c Release -- --live
.\tests\verify_inventory_ui.ps1
```

The live checks use the current desktop and saved Default Layout. Review the
local data first if the screen or document names are sensitive.

## License

[MIT](LICENSE)
