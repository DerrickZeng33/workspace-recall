# Privacy

Workspace Recall is designed to work locally. It has no built-in network
communication, telemetry, analytics, account, advertising, or cloud-upload
feature. If a captured path points to a mapped drive or network share,
restoring it can cause Windows and the target application to access that
location.

## Data collected during capture

A captured layout can contain:

- window titles, application names, process names, and executable paths;
- verified document or folder paths exposed by applications or process
  command lines;
- monitor identifiers, window coordinates, sizes, and display state;
- a program-only decision or an excluded-window decision; and
- window preview images, only when **Save window previews** is selected.

Command-line information is inspected locally only to identify existing file
or folder paths. Workspace Recall does not save the complete command line.
Unsaved documents have no reusable path and cannot be reopened by the app.

These values can disclose personal names, client or project names, folder
structures, and visible screen content. Review them before sharing a saved
layout or screenshot.

## Storage and access

The default layout is stored in:

`%LOCALAPPDATA%\WorkspaceRecall\default-layout.json`

Optional previews are stored below:

`%LOCALAPPDATA%\WorkspaceRecall\previews`

Workspace Recall protects this directory with Windows access rules for the
current user, the local system account, and local administrators. Files remain
ordinary local files and are not encrypted by the app.

Excluding a captured window deletes its saved preview. Capturing with
**Save window previews** turned off deletes previews from the previous saved
layout.

## Optional Revit integration

The Revit 2026 integration is disabled by default. Enabling it installs a
per-user helper. The helper reads the active document path only in response to
a fresh capture request from Workspace Recall; it does not continuously record
the active document.

Use **Disable Revit integration** to remove the per-user manifest, installed
helper, and request/state files. If Revit is already running, restart Revit
after disabling so the loaded helper is released.

## Delete local data

Close Workspace Recall, disable the Revit integration if it is enabled, and
delete `%LOCALAPPDATA%\WorkspaceRecall` to remove the saved layout and optional
previews. This cannot delete the original documents referenced by the layout.
