# Support

## Common installation issues

### Self-signed certificate

Since v0.3.1, `Install.cmd` performs all certificate steps automatically. The public certificate (`ApiMonitorDev.cer`) is imported only into **Local Machine > Trusted People** (never Trusted Root), and only after the script verifies the SHA-256 checksums, the full signer thumbprint, the Subject (`CN=ApiMonitorDev`), the Code Signing EKU, and the package Identity. You still see one normal UAC prompt because machine-level certificate trust requires administrator consent. Only install certificates you trust and only from the official repository.

### Upgrading v0.3.1 to v0.4.0

v0.4.0 upgrades **in place** over v0.3.1: accounts, balance history, thresholds, compact-window settings, and Credential Locker API keys are preserved. Just run `Install.cmd` again. The installer never enables the "start with Windows" startup task automatically.

### Upgrading v0.3.0 to v0.3.1

v0.3.1 upgraded in place over v0.3.0 (historical release line).

### Upgrading from v0.2.0 to v0.3.0

v0.3.0 uses a new package identity and does not upgrade over a v0.2.0 sideload package. Accounts, balance history, and Credential Locker API keys are not migrated.

1. Uninstall the old test package: `Get-AppxPackage -Name ApiMonitor | Remove-AppxPackage`.
2. Install the new package with `Add-AppDevPackage.ps1` (or `Add-AppxPackage -Path <file>.msix`).
3. Add your accounts and API keys again inside the app.

Never paste real API keys into issues or logs during this process.

## Notification-area (tray) operation

Starting with v0.4.0 the app stays resident in the notification area:

- **Cannot find the tray icon?** It is the ApiMonitor blue "A" icon. Check the **overflow area** of the notification area (the `^` chevron near the clock) — Windows may have hidden it there; drag it out to pin it.
- **The tray icon disappeared after an Explorer restart?** It should reappear automatically within a few seconds (the app listens for the `TaskbarCreated` message and re-adds the icon). No restart of ApiMonitor is required. If it does not return, restart Explorer (`explorer.exe`) or sign out and back in.
- **How to fully exit the app?** Right-click the tray icon → **退出 ApiMonitor** (Exit ApiMonitor). Closing the main window only hides it to the tray by default; choose **关闭主窗口时：退出 ApiMonitor** in the settings to make the close button exit the app instead.
- **How to turn off start-with-Windows?** Open the main window → settings section **通知区域与启动** → toggle **登录 Windows 时启动 ApiMonitor** off. You can also disable it in **Task Manager → Startup apps** or **Settings → Apps → Startup**; the app respects that choice.
- **How to re-enable it in Windows startup settings?** Toggle it on inside the app, or enable `ApiMonitor` in **Settings → Apps → Startup**.
- The tray Tooltip shows a short balance summary (normal / low balance count / no data / refreshing). It never shows API keys or balance details.

### Windows App Runtime

The app requires Windows App Runtime 2.3.1 or later. The full test package includes the runtime dependencies under `Dependencies\x64`, and `Install.cmd` installs only what the current x64 system is missing (same or higher installed versions are skipped). If Windows still reports a missing runtime, install the official Microsoft Windows App Runtime redistributable.

### MSIX installation fails

- Check the exit code shown by `Install.cmd` and the log file `%TEMP%\ApiMonitor-Install-*.log`; the code mapping is documented in `INSTALL.md`.
- Exit code 6 means a security check failed (checksum/certificate/thumbprint mismatch): re-download the full `Test.zip`, verify the SHA-256 values, and never install files from unknown sources.
- Exit code 4 means a newer version is already installed; the installer refuses to downgrade.
- Exit code 5 means another package with the same name but a different Publisher exists; resolve that conflict first.
- For manual troubleshooting, see the manual fallback in `INSTALL.md`.

## Balance query issues

- **401 Unauthorized**: the API key is invalid or expired; edit the account and save a new key.
- **Network errors**: check your connection/DNS, then retry.
- **Balance unavailable**: the provider may report the account as unavailable; retry later.

## Filing an issue

- Search existing issues first.
- Include the app version, Windows version, and the exact error message.
- **Never include your real API key, Credential Locker data, or unredacted logs.**

Feature requests are welcome via the feature request template.
