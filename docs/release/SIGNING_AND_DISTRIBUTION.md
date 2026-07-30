# Signing and distribution decision

## Current decision

Workspace Recall is source-only. The project may build and validate unsigned
packages in isolated development or CI environments, but it does not publish
them as trusted releases.

## Public-release gate

Before a binary is made public, the project must use one of these routes:

1. Sign the final Windows executable with a trusted code-signing identity.
2. Package and distribute the application through an approved Microsoft Store
   process that establishes publisher trust.

The final choice is deferred until the maintainer has reviewed identity,
cost, renewal, automation, and recovery requirements. Documentation and CI
must not imply that an unsigned build is safe merely because it compiled or
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
the final signed executable:

```powershell
.\scripts\verify-release-package.ps1 `
    -PackagePath .\dist\WorkspaceRecall-win-x64 `
    -RequireSignature
```

Passing this verifier is one release gate, not a substitute for clean-machine
testing, malware scanning, signature verification, or explicit publication
approval.
