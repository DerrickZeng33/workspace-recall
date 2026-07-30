# Release checklist

Workspace Recall remains source-only until every public-release gate below is
complete. Running a build or passing CI does not authorize publication.

## Release identity

- [ ] Choose a version and document user-visible changes.
- [ ] Confirm the release commit is on protected `main`.
- [ ] Confirm all required GitHub checks pass for that commit.
- [ ] Confirm the repository contains no unrelated or unreviewed changes.

## Privacy and security

- [ ] Review the complete release diff against [PRIVACY.md](PRIVACY.md) and
      [SECURITY.md](SECURITY.md).
- [ ] Confirm no layout data, window previews, documents, private paths,
      personal contact details, logs, credentials, certificates, or signing
      keys are present.
- [ ] Build in a clean, isolated Windows environment.
- [ ] Run `scripts\verify-release-package.ps1` against the final package.
- [ ] Scan the final package with current Windows security tooling.
- [ ] Review dependency and code-scanning alerts.

## Signing gate

- [ ] Sign the final executable with a trusted Windows code-signing identity,
      or package it through an approved Microsoft Store route.
- [ ] Run the package verifier with `-RequireSignature`.
- [ ] Verify the signature on a separate clean Windows environment.
- [ ] Keep private keys outside the repository and build artifacts.

An unsigned package must not be presented as a trusted public release.

## Clean-machine acceptance

- [ ] Test supported Windows 10 and Windows 11 environments.
- [ ] Confirm installation or extraction is understandable and reversible.
- [ ] Confirm launch does not request administrator privileges.
- [ ] Capture a workspace made only from fictional test documents.
- [ ] Verify all eligible windows appear in the inventory.
- [ ] Verify **File identified**, **Program only**, **Needs review**, and
      **Excluded** behavior.
- [ ] Close the test applications and verify restore and placement.
- [ ] Confirm removal leaves no unexpected startup entries or services.

## Publication

- [ ] Prepare release notes with requirements, limitations, privacy behavior,
      and the exact artifact to download.
- [ ] Generate and publish SHA-256 checksums after signing.
- [ ] Verify the published artifact matches the locally approved hash.
- [ ] Obtain explicit maintainer approval before making the release public.
- [ ] After publication, verify the download and signature from the public
      release page.

WinGet or Microsoft Store submission begins only after the public package is
stable and install/remove behavior has passed clean-machine acceptance.
