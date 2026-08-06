# 完整说明

ApiMonitor 是一款本地运行的 Windows 桌面应用（WinUI 3 / .NET 10，x64），用于监测你自己的 API 账户余额、Credits、凭据状态，以及地图/GIS 服务健康。

## 多 Provider、多账户

支持 11 个 Provider：AI 余额 Provider（DeepSeek、OpenRouter、Moonshot / Kimi、SiliconFlow、xAI）与地图/GIS 健康 Provider（高德开放平台、百度地图开放平台、腾讯位置服务、天地图、SuperMap iServer、通用 OGC WMS/WMTS/WFS）。每个 Provider 可添加多个账户；余额、Credits、密钥额度与服务健康分别统计，地图/GIS 账户绝不进入资金余额汇总。

## 本地优先

账户、余额、历史与设置只保存在本机；API Key 只保存在 Windows Credential Locker；无遥测、无广告、无开发者云端服务器；通知由本机进程生成。

## 主要体验

自动刷新、余额提醒、通知区域托盘、悬浮余额窗、数据洞察（趋势与消费估算）、便携备份（不含密钥）、三语界面（简体中文 / English / 日本語）与浅色/深色/跟随系统主题。

## 分发渠道

GitHub 侧载版通过 Releases 分发并可手动检查更新；Microsoft Store 版通过 Store 更新，按全新安装处理，不迁移旧侧载数据。两个渠道的隐私行为一致。