# Public channel brief

This is a factual source for future launch writing. It is not a post to publish
verbatim and does not authorize posting in the maintainer's name.

## One-sentence description

Space Recorder is a privacy-first Windows desktop app that captures
multi-monitor window layouts, attempts to identify verified document paths,
and shows whether every captured item is ready before restoration.

## Demonstrable product facts

- The current prototype supports one saved layout named **Default Layout**.
- Capture inventories eligible user-facing windows independently of whether
  their files can be identified.
- Statuses are **File identified**, **Program only**, **Needs review**, and
  **Excluded**.
- Ready applications and files can be reopened and matched windows returned to
  saved displays, positions, sizes, and states.
- Path detection uses Microsoft Office and AutoCAD automation, an optional
  Revit 2026 helper, command-line inspection, and generic existing-path
  resolution.
- Detection is dynamic but cannot be guaranteed for every Windows application.
- The app has no built-in network communication, telemetry, accounts, uploads,
  input hooks, startup service, or remote-control feature.
- Saved layouts and optional previews remain local; previews are disabled by
  default.

## Fair PowerToys comparison

Microsoft PowerToys Workspaces restores application positions and supports
manually configured command-line arguments, including document paths.
Space Recorder's distinct focus is attempting automatic verified path
detection per captured window and making unresolved items visible before
restore.

Do not claim that PowerToys Workspaces cannot reopen documents.

## Claims not to make

- Do not say every Windows application or file format is supported.
- Do not promise unsaved documents, internal tabs, sessions, or application
  state will return.
- Do not call the app secure merely because it is local-only.
- Do not describe synthetic interface artwork as a live product screenshot.
- Do not claim a user count, success rate, testimonial, download total, or
  endorsement without evidence and permission.
- Do not describe an unsigned build as trusted.

## Available assets

- `docs/assets/interface-preview.png` — synthetic interface preview using
  fictional files and paths.
- `docs/assets/social-preview.png` — 1280×640 repository share image.
- `PRIVACY.md` — data lifecycle and deletion behavior.
- `SECURITY.md` — security boundary and private reporting process.
- `ROADMAP.md` — current and future scope.

## Maker prompts

The maintainer should answer these personally when writing a Show HN or launch
story:

- What repetitive workspace problem led you to build this?
- Which application failed to expose a file path and revealed the need for a
  visible **Needs review** state?
- Why is capture completeness different from restore readiness?
- What did you deliberately exclude for privacy or safety?
- What feedback would change the next version?
