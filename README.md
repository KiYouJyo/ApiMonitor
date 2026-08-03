# ApiMonitor

[简体中文](README.zh-CN.md)

![CI](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml/badge.svg)

**ApiMonitor** is a lightweight Windows desktop app built with WinUI 3 that lets you check and keep a local record of your own API account balances. It currently supports DeepSeek balance queries.

- Current version: **v0.2.0**
- Runtime: .NET 10 / Windows App SDK 2.x, x64
- Distribution: MSIX sideload (self-signed developer certificate) and future Microsoft Store

## Features

- Add, edit, and delete API accounts
- Check balances for one or more currencies (currently DeepSeek: CNY / USD)
- Local balance history with retention
- Per-currency low-balance threshold rules
- Automatic refresh while the app is running
- Manual refresh and one-click secure copy of the API key
- Local snapshot restore after restart

## Security and privacy design

- API keys are stored in the **Windows Credential Locker**, never in JSON, logs, or diagnostics.
- API keys are only sent to the corresponding provider's official endpoint (currently the DeepSeek balance API).
- Account metadata, balance snapshots, history, and settings are stored only in the local app data directory.
- Automatic refresh only runs while the app is running; there is no tray resident and no system notification in this version.
- Copying an API key writes it to the Windows clipboard temporarily, and the app attempts to clear it after about 30 seconds (without clearing anything you copy afterwards).
- No telemetry, ads, or crash uploads.

## System requirements

- Windows 10 version 1809 (build 17763) or later; Windows 11 recommended
- x64
- Windows App Runtime 2.3.1 or later

## Installation

The recommended way is the **full test package** (`.zip`) from the Release assets:

1. Download `ApiMonitor_0.2.0.0_x64_Test.zip`.
2. Verify the SHA-256 checksum against `SHA256SUMS.txt`.
3. Install the included public certificate (`ApiMonitor_0.2.0.0_x64.cer`) into **Local Machine > Trusted People**.
4. Run `Add-AppDevPackage.ps1` (or install the `.msix` with `Add-AppxPackage`).

> The GitHub release is signed with a self-signed developer certificate. Only install certificates you trust and only from the official repository. The Microsoft Store version will be signed and distributed by Microsoft.

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

The project uses the single-project MSIX tooling; the package identity and publisher are kept stable for compatible updates.

## Project structure

```text
ApiMonitor.slnx
ApiMonitor.csproj          Main WinUI 3 app project (x64)
App.xaml / MainWindow.xaml Application and main window
Views/                    Main page, account editor dialog, history dialog
ViewModels/               MVVM view models
Models/                   Domain models
Providers/                Balance providers (DeepSeek) and registry
Services/                 Storage, secrets, refresh, history, thresholds, clipboard
tests/ApiMonitor.Tests/   xUnit test suite
.github/workflows/ci.yml  CI workflow
```

## Current limitations

- Automatic refresh only runs while the app is open; closing the window stops monitoring.
- No tray icon, no system notifications, and no background tasks in this version.
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
