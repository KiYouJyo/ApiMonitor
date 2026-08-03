# ApiMonitor

[简体中文](README.zh-CN.md)

![CI](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml/badge.svg)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**ApiMonitor** is a lightweight Windows desktop app built with WinUI 3 that lets you check and keep a local record of your own API account balances. It currently supports DeepSeek balance queries.

- Current release: **v0.4.0**
- Runtime: .NET 10 / Windows App SDK 2.x, x64
- Distribution: MSIX sideload (self-signed developer certificate) and future Microsoft Store
- License: [MIT](LICENSE)

## Upgrading from v0.2.0

- **v0.3.0 uses a new, complete ApiMonitor package identity** (package name and publisher). The v0.2.0 sideload package will **not** upgrade in place to v0.3.0.
- When installing v0.3.0, uninstall the old v0.2.0 test package first.
- Local accounts, balance history, and Credential Locker API keys from v0.2.0 are **not** migrated automatically; add your accounts and API keys again after installing v0.3.0.

Since **v0.3.1**, the installer supports true in-place upgrades. **v0.4.0** upgrades in place over v0.3.1: accounts, balance history, thresholds, compact-window settings, and Credential Locker API keys are preserved. The installer never enables the start-with-Windows startup task automatically.

## Features

- Add, edit, and delete API accounts
- Check balances for one or more currencies (currently DeepSeek: CNY / USD)
- Local balance history with retention
- Per-currency low-balance threshold rules
- Automatic refresh while the app is running
- Manual refresh and one-click secure copy of the API key
- Notification-area (tray) residency: close to tray, tray menu, single-instance launch
- Optional "start with Windows" (MSIX StartupTask, off by default)
- Local snapshot restore after restart
- Compact always-on-top balance window (single instance per app)
- Account and currency selection in the compact window
- Live balance and refresh-state synchronization between the main and compact windows
- Persistent compact-window position, size, and always-on-top state

## Security and privacy design

- API keys are stored in the **Windows Credential Locker** under the ApiMonitor resource, never in JSON, logs, or diagnostics.
- API keys are only sent to the corresponding provider's official endpoint (currently the DeepSeek balance API).
- Account metadata, balance snapshots, history, and settings are stored only in the local app data directory.
- Automatic refresh only runs while the app is running; there is no tray resident and no system notification in this version.
- The compact window shows the same local account and snapshot data; it does not upload anything extra.
- Copying an API key writes it to the Windows clipboard temporarily, and the app attempts to clear it after about 30 seconds (without clearing anything you copy afterwards).
- No telemetry, ads, or crash uploads.

## System requirements

- Windows 10 version 1809 (build 17763) or later; Windows 11 recommended
- x64
- Windows App Runtime 2.3.1 or later

## Installation

The recommended way is the **full test package** (`Test.zip`) from the Release assets. After extracting it, installation is fully automatic:

1. Download `ApiMonitor_0.4.0.0_x64_Test.zip`.
2. Extract the archive (any folder works, including paths with spaces or Chinese characters).
3. Double-click **`Install.cmd`**.
4. Confirm the **one UAC prompt** ("User Account Control") with **Yes**.
5. Wait for the script to verify, trust the certificate, install dependencies, and install/upgrade the app. When asked, press `Y` to launch ApiMonitor.

Uninstalling is equally simple: double-click **`Uninstall.cmd`** and follow the prompts (you can choose whether to also remove the developer certificate).

> The GitHub sideload release is signed with a self-signed developer certificate. The installer script automatically completes the trust step for you, but it does **not** bypass Windows security:
> - The certificate is only imported into **Local Machine > Trusted People**, never into Trusted Root.
> - The script verifies the SHA-256 checksums, the full certificate thumbprint (from both the MSIX signature and the bundled `.cer`), the certificate Subject `CN=ApiMonitorDev`, the Code Signing EKU, the validity period, and the package Identity before installing anything.
> - You still have to accept the one UAC prompt; that is the normal Windows mechanism for machine-level certificate trust.
> - Only install certificates you trust and only from the official repository. The future Microsoft Store version (v1.0) will be signed and distributed by Microsoft and will not need this flow.

See [INSTALL.md](https://github.com/KiYouJyo/ApiMonitor/blob/main/packaging/installer/INSTALL.md) for graphical steps, SmartScreen notes, common errors and exit codes, the manual fallback, and SHA-256 verification. See [UNINSTALL.md](https://github.com/KiYouJyo/ApiMonitor/blob/main/packaging/installer/UNINSTALL.md) for uninstall and certificate cleanup details.

See [SUPPORT.md](SUPPORT.md) for common installation issues.

## Building from source

Prerequisites:

- Visual Studio 2026 (or newer) with the Windows App SDK / WinUI 3 workload, or the .NET 10 SDK plus the Windows SDK
- Developer mode enabled on Windows (for MSIX sideloading)

```powershell
# Restore and build Debug x64
dotnet build ApiMonitor.slnx -c Debug -p:Platform=x64

# Run all tests
dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug

# Build Release x64
dotnet build ApiMonitor.slnx -c Release -p:Platform=x64
```

The project uses the single-project MSIX tooling. The complete ApiMonitor identity (`ApiMonitor` / `CN=ApiMonitorDev`) is used from v0.3.0 onward.

Third-party components remain subject to their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). This project is not affiliated with DeepSeek or Microsoft, and neither company licenses or endorses this project.

## Project structure

```text
ApiMonitor.slnx
ApiMonitor.csproj          Main WinUI 3 app project (x64)
App.xaml / MainWindow.xaml Application and main window
Views/                    Main page, account editor dialog, history dialog
Views/CompactWindow       Compact always-on-top balance window
ViewModels/               MVVM view models
Models/                   Domain models
Providers/                Balance providers (DeepSeek) and registry
Services/                 Storage, secrets, refresh, history, thresholds, clipboard, window management
tests/ApiMonitor.Tests/   xUnit test suite
.github/workflows/ci.yml  CI workflow
```

## Current limitations

- Automatic refresh only runs while the app is open; closing the window stops monitoring.
- No tray icon, no system notifications, and no background tasks in this version.
- Closing the last window exits the app; the app does not keep running in the background.
- Only the DeepSeek provider is available.
- The GitHub release is self-signed; a proper store signature comes with the Microsoft Store distribution.

## Roadmap

- Additional balance providers
- Tray residency and system notifications
- Microsoft Store release
- Localization improvements

## Privacy

See [PRIVACY.md](PRIVACY.md).

## Reporting security issues

See [SECURITY.md](SECURITY.md). Please use GitHub Private Vulnerability Reporting and never paste real API keys into issues.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Support

See [SUPPORT.md](SUPPORT.md). For bugs or feature requests, open an [issue](https://github.com/KiYouJyo/ApiMonitor/issues).
