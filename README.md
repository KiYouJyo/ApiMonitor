# ApiMonitor

[简体中文](README.zh-CN.md) · [日本語](README.ja-JP.md)

![CI](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml/badge.svg)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**ApiMonitor** is a lightweight Windows desktop app built with WinUI 3 that locally monitors your own API balances, credits, credential status, and map/GIS service health. It supports **11 providers** — five AI balance providers (**DeepSeek**, **OpenRouter**, **Moonshot / Kimi**, **SiliconFlow**, **xAI**) and six map/GIS health providers (map platforms **AMap**, **Baidu Maps**, **Tencent Location**, **Tianditu**, and self-hosted GIS services **SuperMap iServer** and generic **OGC** WMS/WMTS/WFS) — with balances, credits, and service health tracked separately, multi-account management, and optional Windows notification-center alerts.

- Current version: **v1.0.0** (DisplayVersion `1.0.0`, GitHub sideload candidate PackageVersion `1.0.0.1`, Microsoft Store PackageVersion `1.0.0.0`)
- Runtime: .NET 10 / Windows App SDK 2.x, x64
- Distribution: MSIX sideload (self-signed developer certificate) and a Microsoft Store first-release candidate (official identity `JoKiy.ApiMonitor`, awaiting manual acceptance)
- License: [MIT](LICENSE)
- Languages: 简体中文 · English · 日本語 (switchable in Settings → Appearance and language)

## Upgrading

- **v1.0.0 GitHub sideload** upgrades **in place** over v0.9.0: accounts, AccountIds, Credential Locker entries, latest balances/history, thresholds, and all settings are preserved. The **Microsoft Store build is a fresh install** (`JoKiy.ApiMonitor_4wdwgytaw3v2m`): it never reads or migrates old sideload data, shows the first-run guide, and starts with an empty account list by design.
- **v0.9.0** upgrades **in place** over v0.8.0: accounts, AccountIds, Credential Locker entries (including the new multi-slot credentials), latest balances/history, thresholds, auto-refresh / notification / tray / floating-window / sign-in startup / appearance (theme and language) settings are all preserved. All five existing AI providers and their metric IDs are unchanged. The accounts/history JSON schema stays at version 3 (new fields are optional).
- **v0.8.0** upgraded **in place** over v0.7.0 (historical).
- **v0.7.0** upgrades **in place** over v0.6.0: accounts, AccountIds, Credential Locker API keys, latest balances, history, thresholds, auto-refresh / notification / tray / floating-window / sign-in startup / appearance (theme and language) settings are all preserved. Old `compact-window-settings.json` is migrated once and idempotently to `floating-window-settings.json` on first launch. The installer never enables notifications or sign-in startup automatically.
- **v0.6.0** upgraded **in place** over v0.5.0 (historical).
- **v0.5.0** upgrades **in place** over v0.4.0 (historical).
- **v0.2.0 sideload packages will not upgrade in place**; uninstall the old package first and re-add your accounts.

## Features

- Multiple accounts per provider (e.g., several DeepSeek accounts, several OpenRouter keys)
- **DeepSeek**, **OpenRouter**, **Moonshot / Kimi**, **SiliconFlow** and **xAI** providers, selected dynamically from the provider registry (not hardcoded in the UI)
- **OpenRouter two credential modes**:
  - **普通 API Key**: key quota remaining / limit, and total / daily / weekly / monthly usage
  - **Management Key**: account Credits (remaining = total − usage, never clamped to zero)
- **Moonshot / Kimi** (v0.8.0): queries `GET https://api.moonshot.cn/v1/users/me/balance` with a regular API key and shows the available balance (CNY, official `available_balance` = cash + voucher), cash balance and voucher balance. Missing fields are `null` (never `0`); the available balance is the primary metric and cash/voucher are never added again.
- **SiliconFlow** (v0.8.0): queries `GET https://api.siliconflow.cn/v1/user/info` with a regular API key. It reads only the balance fields (`totalBalance` primary, plus `balance` / `chargeBalance` / optional `grantedBalance`) and ignores user profile data. The full response is never logged; when the official structure changes, the app returns "官方响应结构暂不支持" instead of showing a wrong zero.
- **xAI** (v0.8.0): uses the **Management API**, not the inference API — `GET https://management-api.x.ai/v1/billing/teams/{team_id}/prepaid/balance` with a **Management Key** and the **Team ID**. A regular model API key cannot query balances. The documented "Representation of USD Cents" ledger value is converted to the remaining prepaid credits in USD; negative (overdrawn) values are preserved and never clamped or passed through `Math.Abs`.
- **AMap / Baidu Maps / Tencent Location / Tianditu** (v0.9.0): health probes against official Web Service APIs with fixed public inputs — AMap geocoding (`/v3/geocode/geo`), Baidu geocoding (`/geocoding/v3/`), Tencent district list (`/ws/district/v1/list`), Tianditu place search V2.0 (`/v2/search`). Each probe consumes one API call (shown in the UI). Status codes are mapped from the official error tables; unknown codes are surfaced as a safe provider error with the numeric code instead of a guessed meaning. None of these platforms expose an exact remaining-quota API, so `quota.remaining/used/limit/reset_at` stay `null` and are never displayed as `0`.
- **SuperMap iServer** (v0.9.0): self-hosted health monitoring via the service catalog (`{baseUrl}/iserver/services.json`), optional expected-service check and an opt-in manager status probe (`/iserver/manager/serverstatus.json`, off by default). HTTP requires explicit user confirmation; an empty catalog is not treated as offline.
- **Generic OGC service** (v0.9.0): WMS 1.1.1/1.3.0, WMTS 1.0.0, WFS 1.0.0/2.0.0 GetCapabilities health probes with secure XML parsing (DTD/external entities/entity expansion disabled, size and depth limits, no XSLT). Works with MapGIS Server, GeoServer, SuperMap and other OGC servers; only GetCapabilities is used, never GetMap/GetFeature.
- Unified geospatial metric model (v0.9.0): every map/GIS account exposes `service.availability`, `service.latency.ms`, `credential.status`, `permission.status` and `quota.state` (plus `services.count` / `expected-service.present` for SuperMap and `layers.count` / `expected-layer.present` / `service.type` / `service.version` for OGC). Service accounts never enter the monetary balance summary, and no fake ¥/Credits/percentage is shown.
- Quota protection (v0.9.0): new map accounts default to auto-refresh **off**; when enabled, the default interval is 6 hours with a 1-hour minimum; self-hosted GIS services keep the 5-minute minimum. 429/QPS/quota-exceeded/401/403/key-invalid responses are never auto-retried.
- Service health notifications (v0.9.0): CredentialInvalid, PermissionDenied, ServiceNotEnabled, QuotaExceeded, ServiceUnavailable, ServiceRecovered, ExpectedServiceMissing, ExpectedServiceRecovered. New map/GIS accounts default to notifications **off**; transient errors notify only after two consecutive occurrences; recovery notifies after one success; manual test failures never notify. Notifications never contain keys, tokens, full URLs, intranet paths or catalog contents.
- Provider capability metadata (v0.8.0): each provider declares its default official Base URL, required non-sensitive config fields (e.g., xAI Team ID), primary metric, currency, multi-currency / breakdown support and credential-validation support. Custom endpoints are **not** allowed for official providers.
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
- **Floating balance window** (v0.7.0): a lightweight black-and-white, fixed-size rounded square always-on-top widget for one selected account, showing the account name, provider, main balance number, unit and short status. It does not occupy a taskbar slot, and closing it never exits ApiMonitor. Open, switch or close it from any account card (**设为悬浮窗**) or the tray menu (**打开悬浮窗** / **关闭悬浮窗**); the selected account and window position are remembered. It is a single-surface design with smooth native Windows dragging, and it replaces the former compact window.
- The home-page header was simplified: the old subtitle line was removed and the title, actions, status bar and account overview move together without reserved spacing.
- The application icon set was replaced end to end: the EXE/window (title-bar) icon, taskbar/Start menu package logos, splash screen, notification-area (tray) icon and store listing asset all use the new `ApiMonitor.ico` / `TrayIcon.ico` and package logo assets.
- **Data Insights** page (v0.6.0): account / metric / time-range selection, a lightweight local trend chart (WinUI-native, no chart framework), current value, range change, first/latest/min/max values, a collapsible history table, and CSV export
- **Consumption estimates** (v0.6.0): estimated daily consumption (median of valid intervals) and estimated days left, computed only from local history; clearly labeled "估算值" with a disclaimer, and explicit reasons when estimation is not possible (not enough data, no consumption observed, recent top-ups, unsupported metric, unknown current value)
- **Portable backup** (v0.6.0, updated in v0.7.0/v0.8.0): export/import `.apimonitor-backup` (ZIP+JSON) from Settings → Data management — accounts (non-sensitive metadata, including the xAI Team ID), provider settings, balance history, thresholds, auto-refresh/notification/tray/floating-window/appearance settings. v0.8.0/v0.7.0 backups use `floating-window-settings.json`; v0.6.0 backups with the old `compact-window-settings.json` are still accepted on import. **Never contains API keys, Management Keys or credentials.** Import is a safe merge: existing accounts keep their local credentials, new accounts are flagged as needing a re-entered key, history is deduplicated by stable ID, and failures roll back.
- **Themes** (v0.6.0): follow system / light / dark, applied immediately to the main and floating windows and persisted.
- **Unified app shell** (v0.6.0): the title bar, navigation pane and page backgrounds share one consistent theme surface across light, dark and high-contrast.
- **Trilingual UI** (v0.6.0): 简体中文 / English / 日本語. Switching the language saves the preference, asks to restart, and restarts via `AppInstance.Restart`; it never partially localizes the window.
- **Complete About page** (v0.6.0): product info (DisplayVersion and PackageVersion kept separate), dynamic provider list, privacy & security summary, project links, offline local documents (privacy policy / MIT license / third-party notices), manual update check (GitHub REST, only on click, never auto-downloads or installs), copy diagnostics (non-sensitive), and open local data folder.
- Secure one-click API key copy (clipboard auto-clear after ~30 seconds)

## Security and privacy design

- API keys are stored in the **Windows Credential Locker** under the ApiMonitor resource, never in JSON, logs, or diagnostics.
- Keys are only sent to the matching provider's official HTTPS host (DeepSeek `api.deepseek.com`, OpenRouter `openrouter.ai`, Moonshot `api.moonshot.cn`, SiliconFlow `api.siliconflow.cn` / `api.siliconflow.com`, xAI Management API `management-api.x.ai`). A shared credential host whitelist validates every request before the key is attached; non-HTTPS or non-whitelisted destinations are refused. OpenRouter Management Keys are only used for the Credits endpoint and are never sent elsewhere. xAI Management Keys are only sent to `management-api.x.ai` and never to the inference endpoint.
- Balance queries use GET-only, side-effect-free official endpoints; ApiMonitor never sends model inference requests, so querying a balance never consumes tokens or generates charges.
- Timeout / 429 / 5xx responses are retried a limited number of times with cancellation support (401/403/404/config errors are never retried).
- Account metadata, balance snapshots, history, settings, and notification state are stored only in the local app data directory.
- Notifications are generated locally by the running ApiMonitor process; notification arguments contain only non-sensitive identifiers (`action`, `accountId`, `providerId`, `metricId`) and never API keys, history text, Authorization headers, credential resources, or local file paths.
- **No cloud push, no WNS remote push, no telemetry, no developer servers.** Notifications stop when you choose "退出 ApiMonitor".
- Portable backups and CSV exports **never contain API keys, credentials, Authorization headers, logs, or local paths**.
- Update checks only run when you click "检查更新"; they send no account/balance/device data and never download or install anything automatically.
- Automatic refresh only runs while the app is running; hiding the window to the tray keeps monitoring, and exiting fully stops it.
- Sign-in startup is user-enabled (off by default) and only resides in the tray on sign-in.
- Geospatial security (v0.9.0): the four map providers are locked to their official HTTPS hosts (`restapi.amap.com`, `api.map.baidu.com`, `apis.map.qq.com`, `api.tianditu.gov.cn`) with no custom Base URL; redirects are never followed, so credentials cannot be forwarded to another origin. Self-hosted GIS accepts only `http`/`https` (HTTP needs explicit confirmation) and blocks file/ftp/data/custom schemes; credentials never follow cross-host, cross-port or HTTPS→HTTP redirects. Sensitive query parameters (`key`, `ak`, `tk`, `sig`, `sn`, `token`, …) are stripped from logs, and exceptions never contain full request URIs. No vendor console is ever scraped, no LAN scanning or port probing is performed, and service addresses are never uploaded anywhere.
- Multi-slot credentials (v0.9.0): Key+SK (AMap/Baidu/Tencent), Basic username+password, Bearer token and query token are stored as independent Windows Credential Locker entries under the unchanged `ApiMonitor` resource; account JSON only records presence flags, old single-key entries remain readable, and backups never contain any credential value.

## System requirements

- Windows 10 version 1809 (build 17763) or later; Windows 11 recommended
- x64
- Windows App Runtime 2.3.1 or later

## Installation

The recommended way is the **full test package** (`Test.zip`) from the Release assets. After extracting it, installation is fully automatic:

1. Download `ApiMonitor_1.0.0.1_x64_Test.zip` (GitHub sideload candidate; the Store build installs from Microsoft Store).
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

The distribution channel is chosen **at build time** with the
`DistributionChannel` MSBuild property (`Development` / `GitHubSideload` /
`MicrosoftStore`); it is never guessed at runtime from certificates,
`Install.cmd`, network state, or build configuration:

```powershell
# Debug x64 (Development channel, default)
dotnet build ApiMonitor.csproj -c Debug -p:Platform=x64

# GitHub sideload Release x64 (signed 1.0.0.1 candidate)
dotnet build ApiMonitor.csproj -c Release -p:Platform=x64 -p:DistributionChannel=GitHubSideload

# Microsoft Store Release x64 (compile check; packaging via New-StorePackage.ps1)
dotnet build ApiMonitor.csproj -c Release -p:Platform=x64 -p:DistributionChannel=MicrosoftStore
```

Microsoft Store packaging is fully scripted and manual-only:
`packaging/New-StorePackage.ps1` builds the unsigned `.msixupload` (official
identity, `1.0.0.0`) in an isolated worktree, validates it, and optionally
creates a dev-signed local-acceptance MSIX. GitHub sideload candidates are
built with `packaging/New-GitHubCandidatePackage.ps1`. Store output goes to
`packaging/output/v1.0.0/store/` and GitHub output to
`packaging/output/v1.0.0/github/` — the two are never mixed. See
[docs/MICROSOFT_STORE.md](docs/MICROSOFT_STORE.md).

Third-party components remain subject to their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). This project is not affiliated with DeepSeek, OpenRouter, Moonshot, SiliconFlow, xAI, or Microsoft.

## Project structure

```text
ApiMonitor.slnx
ApiMonitor.csproj          Main WinUI 3 app project (x64)
App.xaml / MainWindow.xaml Application and main window
Views/                    Main page, account editor dialog, history dialog
Views/FloatingBalanceWindow  Lightweight always-on-top floating balance window
ViewModels/               MVVM view models
Models/                   Domain models (including generic BalanceMetric)
Providers/                Balance and health providers (5 AI + AMap, Baidu Maps, Tencent Location, Tianditu, SuperMap iServer, OGC) and registry
Services/                 Storage, secrets, refresh, history, thresholds, notifications, clipboard, window management
tests/ApiMonitor.Tests/   xUnit test suite
tests/installer/          Installer tooling tests
.github/workflows/ci.yml  CI workflow (tests + Debug/Release builds, unsigned)
.github/workflows/store-package.yml  Manual-only Store candidate workflow
```

## Current limitations

- Notifications are generated only from queries made while the ApiMonitor process is running. Sign-in startup and tray residency keep monitoring alive, but choosing "退出 ApiMonitor" stops it; there is no Windows Service or scheduled query after full exit.
- No cloud push (WNS), email, SMS, or webhook delivery.
- Consumption and remaining-days estimates are based only on local history and are labeled as estimates.
- Language changes require an application restart.
- Legacy stored metric display labels may retain their original text.
- Update checks are manual only; the app never auto-downloads or auto-installs updates.
- The Microsoft Store first release (v1.0.0) is prepared but not yet published: the official-identity candidate, WACK report, and trilingual listing are ready locally, and manual acceptance must complete before any Partner Center submission.
- The Store build updates through Microsoft Store only; it never downloads GitHub sideload packages. The Store build is a fresh install and does not migrate old sideload data.
- The five official providers (DeepSeek, OpenRouter, Moonshot, SiliconFlow, xAI); no third-party DLL provider loading.
- Map platforms (AMap, Baidu, Tencent, Tianditu) do not expose exact remaining quotas through public APIs; those values are always unknown (`null`) and are never invented. One active probe consumes one API call; new map accounts default to auto-refresh off.
- Tianditu does not officially document token-invalid/permission/quota-limit status codes; unrecognized status codes are shown as a safe provider error with the numeric code rather than a guessed meaning.
- MapGIS Server has no officially proven public catalog/health interface, so it is monitored through the generic OGC provider (WMS/WMTS/WFS GetCapabilities) instead of a proprietary `mapgis-server` provider.
- SuperMap manager-status probing is off by default and only enabled with an authorized credential.
- SiliconFlow's balance API does not return a currency field; CNY is assumed from the platform's pricing convention. If the official structure changes, the app returns "响应结构暂不支持" instead of guessing.
- The GitHub sideload release is signed with the self-signed `CN=ApiMonitorDev` certificate.
- This project is not affiliated with or endorsed by AMap, Baidu, Tencent, Tianditu, SuperMap, MapGIS (Zondy Cyber) or any of the AI providers.

## Roadmap

- Publish v1.0.0 to Microsoft Store after manual acceptance
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
