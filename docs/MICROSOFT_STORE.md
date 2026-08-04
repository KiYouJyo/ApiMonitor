# Microsoft Store Listing Draft (ApiMonitor v0.7.0)

This document is a drafting aid only. It does not modify the Microsoft Partner Center.
Microsoft Store distribution is planned for **v1.0**; v0.7.0 is distributed as a
self-signed MSIX sideload package from GitHub Releases.

## Product name

- EN: ApiMonitor
- 中文：ApiMonitor
- 日本語: ApiMonitor

## Short description

- EN: Lightweight WinUI 3 app to check and track your own API account balances (DeepSeek, OpenRouter) with local history, insights and portable backup.
- 中文：基于 WinUI 3 的轻量桌面应用，查询并记录你自己的 API 账户余额（DeepSeek、OpenRouter），支持本地历史、数据洞察与便携备份。

## Full description

- EN: ApiMonitor lets you add multiple API accounts, query current balances, keep local balance history, set per-metric low-balance thresholds, and refresh automatically while the app is running. v0.6.0 added a local data-insights dashboard with trend charts, daily-consumption and remaining-days estimates (local history only), CSV export, portable backup and safe merge import, light/dark/system themes, a trilingual interface (Simplified Chinese / English / Japanese), a complete About page with manual GitHub update check, and a unified title bar / navigation pane / page background. v0.7.0 replaces the compact window with a lightweight floating balance window (one selected account's main balance number, always on top, no taskbar slot) and refreshes the app icons. API keys are stored in the Windows Credential Locker and are only sent to the provider's official balance API. No telemetry, ads, cloud push, or developer servers.
- 中文：ApiMonitor 支持添加多个 API 账户、查询当前余额、保存本地余额历史、设置各指标低余额阈值，并在应用运行期间自动刷新。v0.6.0 新增本地数据洞察面板（趋势图、基于本机历史的每日消耗与可用天数估算）、CSV 导出、便携备份与安全合并导入、浅色/深色/跟随系统主题、三语界面（简体中文/English/日本語）、完整关于页（手动 GitHub 更新检查），以及统一的标题栏、导航面板与页面背景。v0.7.0 以轻量悬浮余额窗（只显示一个选定账户的主额度数字、始终置顶、不占任务栏）替代紧凑窗口，并整体更新应用图标。API Key 保存在 Windows 凭据管理器，仅发送给 Provider 官方余额接口；无遥测、广告、云端推送或开发者服务器。

## Key features

- EN: Multi-account balance monitoring (DeepSeek, OpenRouter); local balance history; data insights with trend charts, consumption estimates and CSV export; portable backup (no keys) and safe merge import; low-balance thresholds with Windows notification-center alerts; light/dark/system themes; trilingual UI; lightweight floating always-on-top balance window; secure API key storage and one-click copy; manual GitHub update check.
- 中文：多账户余额监控（DeepSeek、OpenRouter）；本地余额历史；数据洞察（趋势图、消费估算、CSV 导出）；便携备份（不含密钥）与安全合并导入；低余额阈值与 Windows 通知中心提醒；浅色/深色/跟随系统主题；三语界面；轻量置顶悬浮余额窗；API Key 安全存储与一键复制；手动 GitHub 更新检查。

## System requirements

- Windows 10 1809 (build 17763) or later; x64; Windows App Runtime 2.3.1 or later

## Privacy statement

- EN: See PRIVACY.md. Keys stay in Credential Locker; balances, history and all analysis stay local; portable backups never contain keys; update checks only run on click and send no account/balance/device data; no telemetry.
- 中文：参见 PRIVACY.md。密钥保存在 Credential Locker；余额、历史与全部分析仅在本机完成；便携备份不含密钥；更新检查仅在点击时运行且不上传账户/余额/设备数据；无遥测。

## URLs

- Support: https://github.com/KiYouJyo/ApiMonitor/issues
- Privacy policy: https://github.com/KiYouJyo/ApiMonitor/blob/main/PRIVACY.md
- GitHub: https://github.com/KiYouJyo/ApiMonitor

## v0.7.0 update notes (for the future v1.0 listing)

- EN: Floating balance window (one selected account's main balance number, always on top, single instance, no taskbar slot, fixed-size monochrome single-surface design, smooth native Windows dragging, position/selected-account persistence) replacing the compact window; simplified home-page header; refreshed app icons (TerminalShare asset set, multi-size ICO + full WinUI/MSIX logo scales); in-place upgrade from v0.6.0 preserving accounts, credentials, history and settings (floating-window settings migrated from the old compact-window settings).
- 中文：悬浮余额窗（只显示一个选定账户的主额度数字、始终置顶、单实例、不占任务栏、固定尺寸黑白单层设计、Windows 原生流畅拖动、位置/选中账户持久化）替代紧凑窗口；主页头部简化；应用图标整体更新（TerminalShare 资产包，多尺寸 ICO + 完整 WinUI/MSIX 各缩放图标）；从 v0.6.0 原地升级，保留账户、凭据、历史与设置（悬浮窗设置由旧紧凑窗口设置自动迁移）。

## v0.6.0 update notes (for the future v1.0 listing)

- EN: Data insights with trend charts, consumption and remaining-days estimates, CSV export; portable backup and safe merge import; themes; trilingual UI; complete About page with manual update check; unified app shell; in-place upgrade from v0.5.0 preserving accounts, credentials, history and settings.
- 中文：新增数据洞察（趋势图、消耗与可用天数估算、CSV 导出）；便携备份与安全合并导入；主题；三语界面；完整关于页与手动更新检查；统一应用外壳；从 v0.5.0 原地升级，保留账户、凭据、历史与设置。

## App assets (v0.7.0)

The repository ships the complete TerminalShare icon asset set under `Assets/` for the future listing:

- `Assets/ApiMonitor.ico` / `Assets/TrayIcon.ico` — multi-size Windows ICO (EXE, title bar, taskbar/tray)
- `Assets/Square44x44Logo.scale-*` and `Square44x44Logo.targetsize-*` (+ altform variants) — small tiles / taskbar / Start menu
- `Assets/Square71x71Logo.scale-*`, `Square150x150Logo.scale-*`, `Wide310x150Logo.scale-*`, `Square310x310Logo.scale-*` — Windows 10 tile assets
- `Assets/StoreLogo.scale-*` — package store identity
- `Assets/SplashScreen.scale-*` — native splash screen
- `Assets/StoreListingIcon-300x300.png` — 300×300 Partner Center listing icon

## Recommended keywords

`WinUI 3`, `Windows App SDK`, `API balance`, `DeepSeek`, `OpenRouter`, `余额监控`, `API 监控`, `残高監視`
