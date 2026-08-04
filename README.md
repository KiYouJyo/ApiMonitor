# ApiMonitor

[简体中文](README.zh-CN.md) · [日本語](README.ja-JP.md)

![CI](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml/badge.svg)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**ApiMonitor** is a lightweight Windows desktop app built with WinUI 3 that lets you check and keep a local record of your own API account balances. It supports **DeepSeek** balance queries and **OpenRouter** key quota / Credits queries, with multi-account management and optional Windows notification-center low-balance alerts.

- Current version: **v0.7.0** (DisplayVersion `0.7.0`, PackageVersion `0.7.0.1`)
- Runtime: .NET 10 / Windows App SDK 2.x, x64
- Distribution: MSIX sideload (self-signed developer certificate) and future Microsoft Store
- License: [MIT](LICENSE)
- Languages: 简体中文 · English · 日本語 (switchable in Settings → Appearance and language)

## Upgrading

- **v0.7.0** upgrades **in place** over v0.6.0: accounts, AccountIds, Credential Locker API keys, latest balances, history, thresholds, auto-refresh / notification / tray / floating-window / sign-in startup / appearance (theme and language) settings are all preserved. Old `compact-window-settings.json` is migrated once and idempotently to `floating-window-settings.json` on first launch. The installer never enables notifications or sign-in startup automatically.
- **v0.6.0** upgraded **in place** over v0.5.0 (historical).
- **v0.5.0** upgrades **in place** over v0.4.0 (historical).
- **v0.2.0 sideload packages will not upgrade in place**; uninstall the old package first and re-add your accounts.

## Features

- Multiple accounts per provider (e.g., several DeepSeek accounts, several OpenRouter keys)
- **DeepSeek** and **OpenRouter** providers, selected dynamically from the provider registry (not hardcoded in the UI)
- **OpenRouter two credential modes**:
  - **普通 API Key**: key quota remaining / limit, and total / daily / weekly / monthly usage
  - **Management Key**: account Credits (remaining = total − usage, never clamped to zero)
- Multi-account summary (total / low-balance / failed), provider filter, and status filter (all / normal / low / unknown / failed)
- Refresh one account or all accounts (reuses per-account concurrency locks; one failure never affects others)
- Per-account history, thresholds, automatic refresh, and notification settings
- Generic **BalanceMetric** domain model: monetary balances, platform Credits, key quotas, and usage metrics share one stable representation; unknown values are `null` (never `0`) and unlimited quotas never falsely trigger low-balance alerts
- **Windows notification-center low-balance alerts** (AppNotification):
  - First-low alert, repeat cooldown (off / 6 h / 12 h / 24 h / 3 d), and recovery alerts
  - "打开账户" and "暂停提醒 24 小时" notification buttons
  - Per-account + metric notification state, snapshot deduplication, merged account-level notifications, stable Tag replacement
  - Test notification button; global alerts are **off by default** after upgrade
- Notification-area (tray) residency, close-to-tray, single instance, optional sign-in startup, and Explorer restart recovery (all from v0.4.0, unchanged)
- **Floating balance window** (v0.7.0): a lightweight black-and-white, rounded square always-on-top information block with the account name, provider, main balance number, unit and short status. It does not occupy a taskbar slot, does not exit the app when closed, and there is exactly one instance. Open it from any account card (**设为悬浮窗**) or the tray menu; the last position and selected account are restored (old compact-window settings are migrated automatically). It replaces the former compact window.
- The home header has been tightened so the title, actions, status bar and account overview move together without a reserved subtitle gap; the custom `ApiMonitor.ico` is also applied explicitly to the window chrome.
- **Data Insights** page (v0.6.0): account / metric / time-range selection, a lightweight local trend chart (WinUI-native, no chart framework), current value, range change, first/latest/min/max values, a collapsible history table, and CSV export
- **Consumption estimates** (v0.6.0): estimated daily consumption (median of valid intervals) and estimated days left, computed only from local history; clearly labeled "估算值" with a disclaimer, and explicit reasons when estimation is not possible (not enough data, no consumption observed, recent top-ups, unsupported metric, unknown current value)
- **Portable backup** (v0.6.0, updated in v0.7.0): export/import `.apimonitor-backup` (ZIP+JSON) from Settings → Data management — accounts (non-sensitive metadata), provider settings, balance history, thresholds, auto-refresh/notification/tray/floating-window/appearance settings. v0.7.0 backups use `floating-window-settings.json`; v0.6.0 backups with the old `compact-window-settings.json` are still accepted on import. **Never contains API keys or credentials.** Import is a safe merge: existing accounts keep their local credentials, new accounts are flagged as needing a re-entered key, history is deduplicated by stable ID, and failures roll back.
- **Themes** (v0.6.0): follow system / light / dark, applied immediately to the main and floating windows and persisted.
- **Unified app shell** (v0.6.0): the title bar, navigation pane and page backgrounds share one consistent theme surface across light, dark and high-contrast.
- **Trilingual UI** (v0.6.0): 简体中文 / English / 日本語. Switching the language saves the preference, asks to restart, and restarts via `AppInstance.Restart`; it never partially localizes the window.
- **Complete About page** (v0.6.0): product info (DisplayVersion and PackageVersion kept separate), dynamic provider list, privacy & security summary, project links, offline local documents (privacy policy / MIT license / third-party notices), manual update check (GitHub REST, only on click, never auto-downloads or installs), copy diagnostics (non-sensitive), and open local data folder.
- Secure one-click API key copy (clipboard auto-clear after ~30 seconds)

## Security and privacy design

- API keys are stored in the **Windows Credential Locker** under the ApiMonitor resource, never in JSON, logs, or diagnostics.
- Keys are only sent to the matching provider's official endpoint (DeepSeek balance API, OpenRouter key/credits APIs). OpenRouter Management Keys are only used for the Credits endpoint and are never sent elsewhere.
- Account metadata, balance snapshots, history, settings, and notification state are stored only in the local app data directory.
- Notifications are generated locally by the running ApiMonitor process; notification arguments contain only non-sensitive identifiers (`action`, `accountId`, `providerId`, `metricId`) and never API keys, history text, Authorization headers, credential resources, or local file paths.
- **No cloud push, no WNS remote push, no telemetry, no developer servers.** Notifications stop when you choose "退出 ApiMonitor".
- Portable backups and CSV exports **never contain API keys, credentials, Authorization headers, logs, or local paths**.
- Update checks only run when you click "检查更新"; they send no account/balance/device data and never download or install anything automatically.
- Automatic refresh only runs while the app is running; hiding the window to the tray keeps monitoring, and exiting fully stops it.
- Sign-in startup is user-enabled (off by default) and only resides in the tray on sign-in.

## System requirements

- Windows 10 version 1809 (build 17763) or later; Windows 11 recommended
- x64
- Windows App Runtime 2.3.1 or later

## Installation

The recommended way is the **full test package** (`Test.zip`) from the Release assets. After extracting it, installation is fully automatic:

1. Download `ApiMonitor_0.7.0.1_x64_Test.zip`.
2. Extract the archive (any folder works, including paths with spaces or Chinese characters).
3. Double-click **`Install.cmd`**.
4. Confirm the **one UAC prompt** with **Yes**.
5. Wait for the script to verify, trust the certificate, install dependencies, and install/upgrade the app. When asked, press `Y` to launch ApiMonitor.

Uninstalling is equally simple: double-click **`Uninstall.cmd`** and follow the prompts (you can choose whether to also remove the developer certificate).

> The GitHub sideload release is signed with a self-signed developer certificate. The installer script automatically completes the trust step for you, but it does **not** bypass Windows security:
> - The certificate is only imported into **Local Machine > Trusted People**, never into Trusted Root.
> - The script verifies the SHA-256 checksums, the full certificate thumbprint (from both the MSIX signature and the bundled `.cer`), the certificate Subject `CN=ApiMonitorDev`, the Code Signing EKU, the validity period, and the package Identity before installing anything.
> - You still have to accept the one UAC prompt; that is the normal Windows mechanism for machine-level certificate trust.

See [INSTALL.md](https://github.com/KiYouJyo/ApiMonitor/blob/main/packaging/installer/INSTALL.md) for graphical steps, SmartScreen notes, common errors and exit codes, the manual fallback, and SHA-256 verification. See [UNINSTALL.md](https://github.com/KiYouJyo/ApiMonitor/blob/main/packaging/installer/UNINSTALL.md) for uninstall and certificate cleanup details.

See [SUPPORT.md](SUPPORT.md) for common installation and notification issues.

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

The project uses the single-project MSIX tooling. The complete ApiMonitor identity (`ApiMonitor` / `CN=ApiMonitorDev`) is used from v0.3.0 onward; Package Family and the Credential Locker resource stay unchanged.

Third-party components remain subject to their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). This project is not affiliated with DeepSeek, OpenRouter, or Microsoft.

## Project structure

```text
ApiMonitor.slnx
ApiMonitor.csproj          Main WinUI 3 app project (x64)
App.xaml / MainWindow.xaml Application and main window
Views/                    Main page, account editor dialog, history dialog
Views/FloatingBalanceWindow  Lightweight always-on-top floating balance window
ViewModels/               MVVM view models
Models/                   Domain models (including generic BalanceMetric)
Providers/                Balance providers (DeepSeek, OpenRouter) and registry
Services/                 Storage, secrets, refresh, history, thresholds, notifications, clipboard, window management
tests/ApiMonitor.Tests/   xUnit test suite
tests/installer/          Installer tooling tests
.github/workflows/ci.yml  CI workflow
```

## Current limitations

- Notifications are generated only from queries made while the ApiMonitor process is running. Sign-in startup and tray residency keep monitoring alive, but choosing "退出 ApiMonitor" stops it; there is no Windows Service or scheduled query after full exit.
- No cloud push (WNS), email, SMS, or webhook delivery.
- Consumption and remaining-days estimates are based only on local history and are labeled as estimates.
- Language changes require an application restart.
- Legacy stored metric display labels may retain their original text.
- Update checks are manual only; the app never auto-downloads or auto-installs updates.
- No Microsoft Store listing yet (planned for v1.0).
- Exactly two providers (DeepSeek and OpenRouter); no third-party DLL provider loading.
- The GitHub sideload release is signed with the self-signed `CN=ApiMonitorDev` certificate.

## Roadmap

- Microsoft Store release (v1.0)
- Additional balance providers
- Localization improvements

## Privacy

See [PRIVACY.md](PRIVACY.md).

## Reporting security issues

See [SECURITY.md](SECURITY.md). Please use GitHub Private Vulnerability Reporting and never paste real API keys into issues.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Support

See [SUPPORT.md](SUPPORT.md). For bugs or feature requests, open an [issue](https://github.com/KiYouJyo/ApiMonitor/issues).
