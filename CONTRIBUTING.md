# Contributing to Space Recorder

Thank you for helping improve Space Recorder. The project is an early
Windows desktop prototype, so focused bug reports and small, verifiable
changes are the most useful contributions.

## Before opening an issue

- Search existing issues and Discussions first.
- Use an issue for a reproducible bug or scoped feature request.
- Use a Discussion for questions, ideas that still need shaping, or general
  feedback.
- Follow [SECURITY.md](SECURITY.md) for vulnerabilities. Do not open a public
  issue for a security problem.

## Protect private workspace data

Never upload real Space Recorder data without fully sanitizing it. In
particular, do not attach:

- `%LOCALAPPDATA%\WorkspaceRecall` or `default-layout.json`;
- window previews or full-desktop screenshots;
- real file, folder, executable, or network-share paths;
- window titles containing names, organizations, projects, or account
  information; or
- documents used during capture or restore.

Prefer a reproduction made with fictional filenames and content under a
temporary test folder. If sanitization would make a report incomplete, use the
private security-reporting process instead of publishing the data.

## Development workflow

Requirements:

- Windows 10 or 11
- .NET 8 SDK
- optionally, Autodesk Revit 2026 for the Revit helper

Run the automated behavior tests:

```powershell
dotnet run --project .\tests\WorkspaceRecall.Tests\WorkspaceRecall.Tests.csproj -c Release
```

Check formatting:

```powershell
dotnet format .\src\WorkspaceRecall.App\WorkspaceRecall.App.csproj --verify-no-changes
dotnet format .\tests\WorkspaceRecall.Tests\WorkspaceRecall.Tests.csproj --verify-no-changes
```

The `--live` tests and `tests\verify_inventory_ui.ps1` inspect the current
Windows desktop and may store layout data locally. Do not run them unless you
have reviewed the privacy warning in [README.md](README.md) and intentionally
prepared a safe test desktop.

## Pull requests

- Keep each pull request focused on one behavior or documentation goal.
- Add or update automated tests when behavior changes.
- Do not commit build outputs, layout data, previews, secrets, certificates,
  private paths, or real document names.
- Describe how the change was verified.
- Complete the privacy and security checklist in the pull request template.

All contributions must follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
