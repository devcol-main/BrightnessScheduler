# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

> Status: the SignPath Foundation application is pending. Until it is approved, release binaries are **unsigned**.
> The release workflow signs automatically as soon as the SignPath project is configured (see below).

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [@devcol-main](https://github.com/devcol-main) |
| Approvers | [@devcol-main](https://github.com/devcol-main) |

All team members use multi-factor authentication for GitHub and SignPath.

## What gets signed

Only binaries built by the GitHub Actions workflow [`release.yml`](.github/workflows/release.yml) from the source code in this repository, for tags `v*`:

- `BrightnessScheduler-<version>-win-x64.exe`
- `BrightnessScheduler-<version>-win-x64-small.exe`

Every signing request is manually approved by an approver. A `SHA256SUMS.txt` file is attached to each release.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

It only talks to your own displays (DDC/CI, WMI). Settings and a log file are stored locally in `%APPDATA%\BrightnessScheduler`.

## System changes and uninstalling

- **Start with Windows** (on by default, can be switched off in Settings) adds `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BrightnessScheduler`.
- To uninstall, open **Settings › Uninstall** (or run `BrightnessScheduler.exe --uninstall`). This removes the autostart entry, settings and log. Then delete the exe.

---

## Maintainer setup: SignPath Foundation (one-time)

1. **Make the project eligible**
   - Public repository with an OSI-approved license — Apache-2.0 (already done), at least one release published.
   - Turn on 2FA for the GitHub account.
2. **Apply** at <https://signpath.org/apply> with the repository URL. Wait for approval (usually days to a few weeks).
3. **In SignPath** (after approval you get an organization):
   - Install the *SignPath GitHub App* on this repository and link the predefined **GitHub.com** trusted build system to the project.
   - Create the project with slug `BrightnessScheduler` and a signing policy `release-signing` (manual approval).
   - Add an artifact configuration with the contents of [`.signpath/artifact-configuration.xml`](.signpath/artifact-configuration.xml) and make it the default.
   - Create an API token (CI user with *Submitter* role on the signing policy).
4. **In GitHub › Settings › Secrets and variables › Actions**
   - Secret `SIGNPATH_API_TOKEN` = the API token
   - Variable `SIGNPATH_ORGANIZATION_ID` = your SignPath organization ID
   - Optional variables if your slugs differ: `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`, `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`
5. **Release**: `git tag v1.1.0 && git push --tags` → approve the signing request in SignPath → the signed exes are attached to the GitHub release.

### Local signing (optional)

[`sign.ps1`](sign.ps1) signs the files in `dist\` on your own PC — with a test certificate (`-CreateTestCert`, only trusted on your machine), a certificate in the Windows store (`-Thumbprint`), or a `.pfx` file (`-PfxPath`).
