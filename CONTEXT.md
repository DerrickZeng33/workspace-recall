# Space Recorder

Space Recorder describes user-facing Windows application windows, the identities needed to reopen them, and their readiness to participate in a saved workspace restoration.

## Language

**Eligible Window**:
A user-facing application window that belongs to the workspace, including a minimized window. Background processes, system windows, tool palettes, and tray utilities are not eligible.

**Captured Window**:
A snapshot of one eligible window in a workspace layout. Capture records the window independently of whether its file or launch identity is sufficient for restoration.
_Avoid_: Captured program

**File-Identified Window**:
A captured window associated with a verified existing file path.
_Avoid_: Exact-path window, command-line window

**Program-Only Window**:
A captured window that the user has confirmed should be restored by opening its application without a file. Its internal content, tabs, session, and unsaved data are not guaranteed to return.

**Needs-Review Window**:
A captured window for which no file has been identified and which has not been confirmed as program-only.
_Avoid_: Missing window, uncaptured window

**Excluded Window**:
A captured window intentionally omitted from workspace restoration.

**Restore-Ready Window**:
A non-excluded captured window with either an identified file or a confirmed program-only launch identity.
_Avoid_: Captured window

**Capture Completeness**:
The condition in which every eligible window appears in the captured-window inventory. It is independent of restore readiness.

**Restore Readiness**:
The condition in which every non-excluded captured window is restore-ready.
_Avoid_: Capture completeness
