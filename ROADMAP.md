# Roadmap

Space Recorder is an early, source-only prototype. Roadmap items describe
direction, not promised dates.

## Current

- Capture one layout named **Default Layout**.
- Inventory every eligible user-facing window.
- Distinguish capture completeness from restore readiness.
- Identify verified file paths when applications expose them.
- Support manual file selection, program-only restore, and exclusion.
- Restore ready applications and return matched windows to saved placements.
- Keep all saved state local with previews disabled by default.

## Launch readiness

- Validate packaging on clean Windows 10 and 11 environments.
- Establish a trustworthy signing or Microsoft Store distribution route.
- Publish a public binary only after that trust requirement is satisfied.
- Expand privacy-safe documentation and application compatibility testing.

## Future candidates

- Multiple named layouts.
- Editable layouts.
- A more advanced layout-management interface.
- Additional verified path adapters where Windows applications expose stable,
  documented integration points.
- Broader automated multi-monitor and application compatibility coverage.

Feature requests are evaluated against privacy, explicit user control, and
restore reliability. A candidate may change or be rejected as the prototype is
tested.

The maintainer-facing [launch plan](docs/launch/LAUNCH_PLAN.md) describes the
signing, beta, outreach, and measurement sequence.
