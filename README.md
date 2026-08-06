简体中文 | [日本語](README.ja.md) | [English](README.en.md)

# ApiMonitor

面向开发者的本地优先 Windows API 余额、额度与服务健康监测工具。

[![MIT License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE) [![CI](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/KiYouJyo/ApiMonitor/actions/workflows/ci.yml) [![GitHub Release](https://img.shields.io/github/v/release/KiYouJyo/ApiMonitor?display_name=tag&sort=semver)](https://github.com/KiYouJyo/ApiMonitor/releases/latest) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20WinUI%203-0078D4?logo=windows) ![x64](https://img.shields.io/badge/arch-x64-0078D4)

## 获取应用

**GitHub 侧载版（当前可用）**：当前版本 **v1.0.0**（PackageVersion `1.0.0.2`），x64，自签名。推荐下载[最新 GitHub Release](https://github.com/KiYouJyo/ApiMonitor/releases/latest) 中的完整 `ApiMonitor_1.0.0.2_x64_Test.zip`，解压后双击 `Install.cmd` 即可安装；更新来源为 GitHub Releases。

**Microsoft Store 版**：PackageVersion `1.0.0.0`，使用独立的 Store 身份（`JoKiy.ApiMonitor`）。Store 包已上传至 Partner Center，正在完成首次发布流程；尚未公开，因此暂不提供下载链接。

两条渠道使用不同的 Package Family，**不能相互覆盖升级**；Store 版按全新安装处理，**不迁移** GitHub 侧载版的数据；不建议同时安装两个渠道版本。

## 关于 ApiMonitor

ApiMonitor 是一款面向开发者的本地优先 Windows API 余额、额度与服务健康监测工具，支持多个 AI API 平台以及地图/GIS 服务。账户、余额、历史与设置只保存在本机；无遥测、无广告、无开发者云端服务器。

## 主要功能

- 多 Provider、多账户管理
- API 余额、Credits、额度与服务健康监测
- 自动刷新（仅在应用运行时执行）
- 低余额与服务状态提醒（Windows 通知中心，默认关闭）
- 本地历史与数据洞察（趋势、消耗估算、CSV 导出）
- 通知区域托盘常驻、关闭到托盘、登录启动（默认关闭）
- 悬浮余额窗（始终置顶的紧凑额度窗）
- 首次启动引导
- 应用运行状况检查（21 项只读非敏感诊断）
- 本地便携备份（不含任何密钥）
- 简体中文、English、日本語
- 浅色、深色与跟随系统主题

## 支持的服务

**AI 余额与额度**

- DeepSeek
- OpenRouter（普通 API Key 与 Management Key）
- Moonshot / Kimi
- SiliconFlow
- xAI（Management API）

**地图与 GIS 服务健康**

- AMap
- Baidu Maps
- Tencent Location
- Tianditu
- SuperMap iServer
- OGC WMS / WMTS / WFS

说明：地图平台通常只提供接口健康与凭据状态，不一定提供精确剩余额度；应用不显示虚假的 0 或百分比。自托管 GIS 服务地址只保存在本机。

各 Provider 的认证模式、指标定义、Host 白名单与安全限制详见 [docs/PROVIDERS.md](docs/PROVIDERS.md) 与 [docs/SECURITY-ARCHITECTURE.md](docs/SECURITY-ARCHITECTURE.md)。

## 安装

GitHub 侧载版从 [Releases](https://github.com/KiYouJyo/ApiMonitor/releases/latest) 下载完整 `Test.zip`：

1. 下载并解压 `ApiMonitor_1.0.0.2_x64_Test.zip`。
2. 双击 **`Install.cmd`**，确认一次 UAC。
3. 等待脚本完成证书信任、依赖检查与安装/升级。

详细步骤、SmartScreen 说明、退出码与 SHA-256 校验见 [packaging/installer/INSTALL.md](packaging/installer/INSTALL.md)；卸载见 [UNINSTALL.md](packaging/installer/UNINSTALL.md)。

## 隐私与本地设计

- API Key 只保存在 Windows Credential Locker，只发送给你选择的 Provider 官方接口（或明确配置的自托管 GIS 地址）。
- 账户、余额、历史与设置只保存在本机应用数据目录；无遥测、无广告、无开发者云端服务器、无云同步。
- 通知由本机进程生成；退出应用后停止。
- GitHub 版只在点击“检查更新”时访问 GitHub Releases API；Store 版通过 StoreContext 检查更新，不打开 GitHub 下载页。
- 便携备份与 CSV 导出不包含任何密钥。

详见 [PRIVACY.md](PRIVACY.md) 与 [docs/SECURITY-ARCHITECTURE.md](docs/SECURITY-ARCHITECTURE.md)。

## 系统要求

- Windows 10 版本 1809（build 17763）或更高，x64
- Windows App Runtime 2.3.1 或更高（Store 版由 Store 处理依赖）

## 数据与备份

数据文件保存在本机应用数据目录（账户、余额历史、阈值与设置分离存储，损坏自动备份）。设置页支持 `.apimonitor-backup` 便携备份导出/导入（不含凭据，导入为安全合并）。详见 [docs/DATA-STORAGE.md](docs/DATA-STORAGE.md) 与 [docs/BACKUP.md](docs/BACKUP.md)。

## 语言

界面支持简体中文、日本語与 English，可在设置中选择（切换后需要重启应用）。

## 文档

- [发布指南](docs/RELEASE.md)
- [Microsoft Store 发布指南](docs/STORE-PUBLISHING.md)
- [Provider 说明](docs/PROVIDERS.md)
- [数据存储](docs/DATA-STORAGE.md) · [数据备份](docs/BACKUP.md) · [安全架构](docs/SECURITY-ARCHITECTURE.md)
- [路线图](docs/ROADMAP.md)
- [更改日志](CHANGELOG.md)
- [第三方声明](THIRD-PARTY-NOTICES.md)

## 开发与构建

```powershell
dotnet restore ApiMonitor.slnx -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug
dotnet build ApiMonitor.slnx -c Debug -p:Platform=x64 --no-restore
```

分发渠道在构建时通过 `DistributionChannel` 属性确定（`Development` / `GitHubSideload` / `MicrosoftStore`）。完整构建、渠道隔离与发布流程见 [docs/RELEASE.md](docs/RELEASE.md)。

## 问题反馈

请通过 [GitHub Issues](https://github.com/KiYouJyo/ApiMonitor/issues) 反馈问题。提交诊断信息前请检查并移除敏感内容（真实 API Key、Authorization、Management Key、内网 GIS 地址、包含账户信息的 LocalState）。安全问题请使用 GitHub Private Vulnerability Reporting。

## 路线图

当前完成情况与未来方向见 [docs/ROADMAP.md](docs/ROADMAP.md)。路线图用于说明方向，不构成版本或日期承诺。

## 许可证与第三方声明

本项目采用 [MIT License](LICENSE)。依赖与第三方组件声明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。本项目与 DeepSeek、OpenRouter、Moonshot、SiliconFlow、xAI、各地图/GIS 平台及 Microsoft 均无官方隶属关系。
