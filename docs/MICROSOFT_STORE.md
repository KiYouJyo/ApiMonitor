# Microsoft Store Preparation (ApiMonitor v0.9.0)

This document is a drafting aid only. It does not modify the Microsoft
Partner Center, and nothing here is auto-submitted. Current Store status:
**Blocked: Store association required** — see
[`STORE_ASSOCIATION_REQUIRED.md`](../STORE_ASSOCIATION_REQUIRED.md).

Until the reserved ApiMonitor product is associated in Visual Studio
(Publish → Associate App with the Store…), the Store package
(`ApiMonitor_0.9.0.0_x64.msixupload`) cannot be generated. The GitHub
sideload identity (`CN=ApiMonitorDev`) must never be used as a Store
Publisher, and the GitHub Release is independent of the Store submission.

## Product name

- EN: ApiMonitor
- 中文：ApiMonitor
- 日本語: ApiMonitor

## Short description

- EN: Monitor API balances, credits, credentials, map services, and GIS endpoints locally on Windows.
- 中文：在本地统一监测 API 账户余额、Credits、凭据状态、地图服务和 GIS 接口可用性。
- 日本語：API残高、クレジット、認証情報、地図サービス、GISエンドポイントをWindows上でローカル監視。

## Positioning

ApiMonitor is a local Windows tool for **API balance, credits, credential
status, and service health monitoring**. It is not limited to being an
"API account balance viewer".

## Full description topics (see `../store-listing/v0.9.0/` for the finished copy)

- Multi-provider, multi-account: 5 AI balance providers (DeepSeek, OpenRouter,
  Moonshot / Kimi, SiliconFlow, xAI) and 6 map/GIS health providers (AMap,
  Baidu Maps, Tencent Location, Tianditu, SuperMap iServer, generic OGC
  WMS/WMTS/WFS)
- Balances and credits; map credential, permission, and service status
- SuperMap iServer and WMS/WMTS/WFS support self-hosted addresses; OGC calls
  GetCapabilities by default
- Latency and local history trends; local notifications; tray and floating
  window; CSV export; local backup; trilingual UI (简体中文 / English / 日本語)
  and light/dark/system themes
- All credentials stay in the Windows Credential Locker; no telemetry, no
  cloud sync, no developer servers; free and open source (MIT)
- Active map probes may consume one API call; when an exact remaining quota
  cannot be queried, it stays unknown (never faked)

## Must NOT claim

- Support for every API
- Exact remaining quota for all map platforms
- Official vendor certification
- Monitoring after the app has fully exited
- Protection from all charges
- That the Microsoft Store version is already live (it is in preparation)

## System requirements

- Windows 10 1809 (build 17763) or later; x64; Windows App Runtime 2.3.1 or
  later (see the actual manifest `MinVersion`/`MaxVersionTested` in
  `Package.appxmanifest`)

## Privacy statement

- See `PRIVACY.md`. Keys, secrets, and tokens stay in the Windows Credential
  Locker; balances, credits, service health, history, and settings stay local;
  portable backups never contain credentials; update checks only run on click
  and send no account/balance/device data; no telemetry, no ads.

## URLs

- Support: https://github.com/KiYouJyo/ApiMonitor/issues
- Privacy policy: https://github.com/KiYouJyo/ApiMonitor/blob/main/PRIVACY.md
- Project homepage / source: https://github.com/KiYouJyo/ApiMonitor
- Issue tracker: https://github.com/KiYouJyo/ApiMonitor/issues
- License: https://github.com/KiYouJyo/ApiMonitor/blob/main/LICENSE

## v0.9.0 update notes

- 6 new map/GIS health providers (AMap, Baidu Maps, Tencent Location,
  Tianditu, SuperMap iServer, generic OGC); balances/credits and service
  health tracked separately; map providers never enter the balance summary
- Multi-slot credentials (Key+SK, Basic, Bearer, query token) in Credential
  Locker; service health notifications; quota protection (new map accounts
  default to auto-refresh off)
- In-place upgrade from v0.7.0 / v0.8.0 preserving accounts, credentials,
  history and settings (schema stays v3)

## v0.8.0 update notes (not released separately)

- Moonshot / Kimi, SiliconFlow and xAI balance providers; provider capability
  metadata; HTTPS host whitelist; non-sensitive config fields (xAI Team ID)

## Recommended keywords (max 7)

`API balance`, `DeepSeek`, `OpenRouter`, `GIS`, `map service`, `余额监控`, `API 監視`

## Store prep artifacts

- Listing copy (trilingual): `../store-listing/v0.9.0/`
- Submission checklist: `../StoreSubmissionChecklist.md`
- Store package + report: `../packaging/store/v0.9.0/`
