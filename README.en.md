简体中文 | [日本語](README.ja.md) | English

# ApiMonitor

A local-first Windows monitor for API balances, credits, and AI/GIS service health.

[![MIT License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![CI](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml) [![GitHub Release](https://img.shields.io/github/v/release/KiYouJyo/ApiMonitor?display_name=tag&sort=semver)](https://github.com/KiYouJyo/ApiMonitor/releases/latest) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20WinUI%203-0078D4?logo=windows) ![x64](https://img.shields.io/badge/arch-x64-0078D4)

## Get the app

**GitHub sideload (currently available)**: current version **v1.0.0** (PackageVersion `1.0.0.2`), x64, self-signed. Download the full `ApiMonitor_1.0.0.2_x64_Test.zip` from the [latest GitHub Release](https://github.com/KiYouJyo/ApiMonitor/releases/latest), extract it, and run `Install.cmd`. Updates come from GitHub Releases.

**Microsoft Store**: PackageVersion `1.0.0.0` with a separate Store identity (`JoKiy.ApiMonitor`). The Store package has been uploaded to Partner Center and the first release is being completed; it is not public yet, so no download link is provided.

The two channels use different Package Families and **cannot update each other in place**. The Store build is a fresh install and does **not** migrate GitHub sideload data. Installing both channel versions at once is not recommended.

## About ApiMonitor

ApiMonitor is a local-first Windows tool for developers to monitor API balances, quotas, and service health across multiple AI platforms and map/GIS services. Accounts, balances, history, and settings stay on your device; there is no telemetry, no ads, and no developer cloud server.

## Features

- Multiple providers, multiple accounts
- API balances, credits, quotas, and service health
- Auto-refresh (only while the app is running)
- Low-balance and service-status alerts (Windows notification center, off by default)
- Local history and insights (trends, consumption estimates, CSV export)
- Notification-area tray, close-to-tray, sign-in startup (off by default)
- Floating balance window (compact always-on-top widget)
- First-run guide
- App health checks (21 read-only, non-sensitive diagnostics)
- Local portable backup (never contains keys)
- Simplified Chinese, English, and Japanese
- Light, dark, and system themes

## Supported services

**AI balances and quotas**

- DeepSeek
- OpenRouter (standard API key and Management key)
- Moonshot / Kimi
- SiliconFlow
- xAI (Management API)

**Map and GIS service health**

- AMap
- Baidu Maps
- Tencent Location
- Tianditu
- SuperMap iServer
- OGC WMS / WMTS / WFS

Map platforms usually expose interface health and credential status rather than an exact remaining quota; the app never invents a zero or a percentage. Self-hosted GIS service addresses are stored only on your device.

See [docs/PROVIDERS.md](docs/PROVIDERS.md) and [docs/SECURITY-ARCHITECTURE.md](docs/SECURITY-ARCHITECTURE.md) for provider authentication modes, metric definitions, host whitelists, and security limits.

## Installation

For the GitHub sideload channel, download the full `Test.zip` from [Releases](https://github.com/KiYouJyo/ApiMonitor/releases/latest):

1. Download and extract `ApiMonitor_1.0.0.2_x64_Test.zip`.
2. Double-click **`Install.cmd`** and accept one UAC prompt.
3. Wait for the script to complete certificate trust, dependency checks, and install/upgrade.

See [packaging/installer/INSTALL.md](packaging/installer/INSTALL.md) for details, SmartScreen notes, exit codes, and SHA-256 verification; see [UNINSTALL.md](packaging/installer/UNINSTALL.md) for removal.

## Privacy and local design

- API keys are stored only in Windows Credential Locker and sent only to the official endpoints of the providers you choose (or self-hosted GIS addresses you configure).
- Accounts, balances, history, and settings stay in the local app data directory; no telemetry, no ads, no developer cloud server, no cloud sync.
- Notifications are generated locally and stop when you exit the app.
- The GitHub build accesses the GitHub Releases API only when you click "Check for updates"; the Store build uses StoreContext and never opens GitHub download pages.
- Portable backups and CSV exports never contain keys.

See [PRIVACY.md](PRIVACY.md) and [docs/SECURITY-ARCHITECTURE.md](docs/SECURITY-ARCHITECTURE.md).

## Requirements

- Windows 10 version 1809 (build 17763) or later, x64
- Windows App Runtime 2.3.1 or later (the Store build resolves dependencies through the Store)

## Data and backup

Data files live in the local app data directory (accounts, balance history, thresholds, and settings are stored separately; corrupt files are backed up automatically). Settings provides `.apimonitor-backup` export/import (no credentials; import is a safe merge). See [docs/DATA-STORAGE.md](docs/DATA-STORAGE.md) and [docs/BACKUP.md](docs/BACKUP.md).

## Language

The UI supports Simplified Chinese, Japanese, and English; switching languages requires an app restart.

## Documentation

- [Release guide](docs/RELEASE.md)
- [Microsoft Store publishing guide](docs/STORE-PUBLISHING.md)
- [Providers](docs/PROVIDERS.md)
- [Data storage](docs/DATA-STORAGE.md) · [Backup](docs/BACKUP.md) · [Security architecture](docs/SECURITY-ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [Changelog](CHANGELOG.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)

## Development and building

```powershell
dotnet restore ApiMonitor.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug
dotnet build ApiMonitor.slnx -c Debug -p:Platform=x64 --no-restore
```

The distribution channel is chosen at build time via the `DistributionChannel` property (`Development` / `GitHubSideload` / `MicrosoftStore`). See [docs/RELEASE.md](docs/RELEASE.md) for the full build, channel-isolation, and release flow.

## Feedback

Report issues via [GitHub Issues](https://github.com/KiYouJyo/ApiMonitor/issues). Before submitting diagnostics, remove anything sensitive (real API keys, Authorization headers, Management keys, intranet GIS addresses, LocalState with account data). For security issues, use GitHub Private Vulnerability Reporting.

## Roadmap

See [docs/ROADMAP.md](docs/ROADMAP.md). The roadmap communicates direction and is not a version or date commitment.

## License and third-party notices

This project is licensed under the [MIT License](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). This project is not affiliated with DeepSeek, OpenRouter, Moonshot, SiliconFlow, xAI, any map/GIS platform, or Microsoft.
