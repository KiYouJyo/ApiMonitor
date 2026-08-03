# Support

## Common installation issues

### Self-signed certificate

GitHub releases are signed with a self-signed developer certificate. Install the public certificate (`ApiMonitor_0.2.0.0_x64.cer`) into **Local Machine > Trusted People** before installing the MSIX. Only install certificates you trust and only from the official repository.

### Windows App Runtime

The app requires Windows App Runtime 2.3.1 or later. The full test package includes the runtime dependencies; if Windows still reports a missing runtime, install the official Microsoft Windows App Runtime redistributable.

### MSIX installation fails

- v0.3.0 builds use a new package identity and will not install over a v0.2.0 package; uninstall the old test package first.
- If the error is about an existing package, check whether you are trying to install an older or mismatched package.
- Run `Add-AppxPackage -Path <file>.msix` from an elevated PowerShell if prompted, or use `Add-AppDevPackage.ps1`.

## Balance query issues

- **401 Unauthorized**: the API key is invalid or expired; edit the account and save a new key.
- **Network errors**: check your connection/DNS, then retry.
- **Balance unavailable**: the provider may report the account as unavailable; retry later.

## Filing an issue

- Search existing issues first.
- Include the app version, Windows version, and the exact error message.
- **Never include your real API key, Credential Locker data, or unredacted logs.**

Feature requests are welcome via the feature request template.
