# Signing and distribution decision

## Current decision

Space Recorder is source-only. Its Microsoft Store product identity has been
reserved, and the project may build and validate an unsigned Store package in
isolated development or CI environments. No package has been uploaded,
certified, or published.

## Public-release gate

Before a binary is made public, the project must use one of these routes:

1. Sign the final Windows executable with a trusted code-signing identity.
2. Package and distribute the application through an approved Microsoft Store
   process that establishes publisher trust.

The current planned route is Microsoft Store certification. Documentation and
CI must not imply that an unsigned build is safe merely because it compiled or
passed tests.

## Key-handling boundary

- Never commit certificates, private keys, passwords, recovery material, or
  exported signing bundles.
- Never include signing material in a release asset or general CI artifact.
- Limit signing authority to an explicitly approved release environment.
- Sign the final artifact before calculating published checksums.
- Treat certificate replacement or account recovery as a maintainer-controlled
  security event.

## Package verification

`scripts\verify-release-package.ps1` checks required runtime files, rejects
common private-data and key material, detects links that can escape the
package, and calculates SHA-256 hashes. Use `-RequireSignature` only against
an executable that was signed outside the Store route:

```powershell
.\scripts\verify-release-package.ps1 `
    -PackagePath .\dist\WorkspaceRecall-win-x64 `
    -RequireSignature
```

Passing this verifier is one release gate, not a substitute for clean-machine
testing, malware scanning, signature verification, or explicit publication
approval.

`scripts\build-msix.ps1` creates an unsigned `.msix` and `.msixupload` for
Partner Center. It omits the optional Revit helper and requests only the
`runFullTrust` capability required by the WPF desktop app.

`scripts\verify-msix-package.ps1` confirms that the package identity matches
Partner Center, required files and image dimensions are present, private or
key material is absent, and no capability other than `runFullTrust` is
declared. The verifier also rejects a pre-existing package signature because
Microsoft Store signs an accepted Store package.
