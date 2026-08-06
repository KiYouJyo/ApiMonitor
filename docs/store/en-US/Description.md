# Full description

ApiMonitor is a local Windows desktop app (WinUI 3 / .NET 10, x64) for monitoring your own API account balances, credits, credential status, and map/GIS service health.

## Multiple providers, multiple accounts

11 providers are supported: AI balance providers (DeepSeek, OpenRouter, Moonshot / Kimi, SiliconFlow, xAI) and map/GIS health providers (AMap, Baidu Maps, Tencent Location, Tianditu, SuperMap iServer, generic OGC WMS/WMTS/WFS). Each provider supports multiple accounts; balances, credits, key quotas, and service health are tracked separately, and map/GIS accounts never enter the monetary balance summary.

## Local-first

Accounts, balances, history, and settings stay on your device; API keys are stored only in Windows Credential Locker; no telemetry, no ads, no developer cloud servers; notifications are generated locally.

## Highlights

Auto-refresh, balance alerts, notification-area tray, floating balance window, insights (trends and consumption estimates), portable backup (never contains keys), trilingual UI (Simplified Chinese / English / Japanese), and light / dark / system themes.

## Distribution channels

The GitHub sideload build is distributed through Releases with manual update checks; the Microsoft Store build updates through the Store and is treated as a fresh install (no migration of old sideload data). Both channels have identical privacy behavior.