# ApiMonitor

[English](README.md)

**ApiMonitor** 是一款基于 WinUI 3 的轻量 Windows 桌面应用，用于查询并记录你自己的 API 账户余额。当前支持 DeepSeek 余额查询。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

- 当前正式版本：**v0.3.0**
- 运行时：.NET 10 / Windows App SDK 2.x，x64
- 分发：MSIX 侧载（自签名开发证书）以及未来的 Microsoft Store
- 许可证：[MIT](LICENSE)

## 从 v0.2.0 升级说明

- **v0.3.0 使用全新的完整 ApiMonitor 包身份**（包名与发布者）。v0.2.0 侧载包**不会原地升级**到 v0.3.0。
- 安装 v0.3.0 构建前，请先卸载旧的 v0.2.0 测试包。
- v0.2.0 的本地账户、余额历史和 Credential Locker API Key **不会自动迁移**；安装 v0.3.0 后需要重新添加账户与 API Key。

## 主要功能

- 添加、编辑和删除 API 账户
- 多币种余额查询（当前支持 DeepSeek：CNY / USD）
- 本地余额历史记录（含保留策略）
- 按币种设置低余额阈值规则
- 应用运行期间自动刷新
- 手动刷新与一键安全复制 API Key
- 重启后恢复本地余额快照
- 紧凑置顶余额窗口（整个应用仅一个实例）
- 紧凑窗口内切换账户与币种
- 主窗口与紧凑窗口余额及刷新状态实时同步
- 持久化紧凑窗口的位置、尺寸与置顶状态

## 安全与隐私设计

- API Key 保存在 **Windows 凭据管理器（Credential Locker）** 的 ApiMonitor 资源中，绝不写入 JSON、日志或诊断信息。
- API Key 只发送给对应 Provider 的官方接口（当前为 DeepSeek 官方余额接口）。
- 账户元数据、余额快照、历史记录和设置仅保存在本机应用数据目录。
- 自动刷新只在应用运行期间执行；本版本没有托盘驻留和系统通知。
- 紧凑窗口只展示本机已有的账户与快照数据，不会额外上传任何内容。
- 复制 API Key 会短暂写入 Windows 剪贴板，应用约 30 秒后尝试安全清理（不会清除你之后复制的新内容）。
- 无遥测、广告和崩溃上传。

## 系统要求

- Windows 10 1809（build 17763）或更高版本；推荐 Windows 11
- x64
- Windows App Runtime 2.3.1 或更高版本

## 安装方式

推荐使用 Release 资产中的**完整测试包**（`.zip`）：

1. 下载 `ApiMonitor_0.3.0.0_x64_Test.zip`。
2. 使用 `SHA256SUMS.txt` 核验 SHA-256 校验和。
3. 将随附的公开证书（`ApiMonitorDev.cer`）安装到 **本地计算机 > 受信任人**。
4. 运行 `Add-AppDevPackage.ps1`（或用 `Add-AppxPackage` 安装 `.msix`）。

如果之前安装过 v0.2.0 测试包，请先卸载（`Get-AppxPackage -Name ApiMonitor | Remove-AppxPackage`），再安装新包。

> GitHub Release 使用自签名开发证书签名。请只安装你信任且来自官方仓库的证书。Microsoft Store 版本将由微软签名和分发。

常见安装问题参见 [SUPPORT.md](SUPPORT.md)。

## 从源码构建

前置要求：

- Visual Studio 2026（或更新版本）并安装 Windows App SDK / WinUI 3 工作负载，或 .NET 10 SDK + Windows SDK
- 启用 Windows 开发者模式（用于 MSIX 侧载）

```powershell
# 还原并构建 Debug x64
dotnet build ApiMonitor.slnx -c Debug -p:Platform=x64

# 运行全部测试
dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug

# 构建 Release x64
dotnet build ApiMonitor.slnx -c Release -p:Platform=x64
```

项目使用单项目 MSIX 工具链。自 v0.3.0 起使用完整 ApiMonitor 身份（`ApiMonitor` / `CN=ApiMonitorDev`）。

第三方组件仍受其各自许可证约束，参见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。本项目与 DeepSeek、Microsoft 无隶属关系，两者均非本项目许可证签发方或背书方。

## 项目结构

```text
ApiMonitor.slnx
ApiMonitor.csproj          主 WinUI 3 应用项目（x64）
App.xaml / MainWindow.xaml 应用与主窗口
Views/                     主页面、账户编辑对话框、历史记录对话框
Views/CompactWindow        紧凑置顶余额窗口
ViewModels/                MVVM 视图模型
Models/                    领域模型
Providers/                 余额 Provider（DeepSeek）与注册表
Services/                  存储、密钥、刷新、历史、阈值、剪贴板、窗口管理服务
tests/ApiMonitor.Tests/    xUnit 测试套件
.github/workflows/ci.yml   CI 工作流
```

## 当前限制

- 自动刷新只在应用打开时运行；关闭窗口即停止监测。
- 本版本没有托盘图标、系统通知和后台任务。
- 关闭最后一个窗口后应用退出，不会后台驻留。
- 当前仅支持 DeepSeek Provider。
- GitHub Release 为自签名；正式商店签名随 Microsoft Store 分发提供。

## 路线图

- 更多余额 Provider
- 托盘驻留与系统通知
- Microsoft Store 上架
- 本地化完善

## 隐私

参见 [PRIVACY.md](PRIVACY.md)。

## 报告安全问题

参见 [SECURITY.md](SECURITY.md)。请使用 GitHub Private Vulnerability Reporting，切勿在 Issue 中粘贴真实 API Key。

## 参与贡献

参见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 支持

参见 [SUPPORT.md](SUPPORT.md)。如遇问题或想提需求，请在 [Issues](https://github.com/KiYouJyo/ApiMonitor/issues) 中提交。
