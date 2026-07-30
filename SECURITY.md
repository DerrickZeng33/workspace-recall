# Security policy

Workspace Recall is an early Windows prototype. No public binary release is
currently provided. Builds are not code-signed, so only run a build you created
from source and reviewed.

## Report a vulnerability

Do not disclose a suspected vulnerability in a public issue. Use GitHub's
private vulnerability reporting feature under the repository's **Security**
tab. If that feature is unavailable, contact the maintainer privately through
their GitHub profile before sharing technical details.

Include the affected commit, Windows version, reproduction steps, observed
impact, and any relevant logs with personal paths or document names redacted.

## Security boundaries

Workspace Recall:

- does not expose a network listener or remote-control interface;
- does not require administrator privileges;
- stores layout information locally;
- opens captured, user-accessible applications and documents only when the user selects
  **Restore layout**; and
- can move, resize, minimize, maximize, or restore matched windows as part of
  that requested restore operation.

Executable, script, installer, shortcut, and registry-file paths are rejected
as document arguments. Program-only entries can launch only a captured
executable that still exists and has an `.exe` extension.

These controls reduce accidental or malicious launch behavior, but they do not
make allowed documents passive. A target application can execute macros or
other active content in a document format it trusts. A path on a mapped drive
or network share can also cause Windows or the target application to access
that location. This prototype has not received an independent security audit.
