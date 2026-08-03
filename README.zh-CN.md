# ApiMonitor

[English](README.md)

**ApiMonitor** 是一款基于 WinUI 3 的轻量 Windows 桌面应用，用于查询并记录你自己的 API 账户余额。当前支持 DeepSeek 余额查询。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

- 当前正式版本：**v0.4.0**
- 运行时：.NET 10 / Windows App SDK 2.x，x64
- 分发：MSIX 侧载（自签名开发证书）以及未来的 Microsoft Store
- 许可证：[MIT](LICENSE)

## 从 v0.2.0 升级说明

- **v0.3.0 使用全新的完整 ApiMonitor 包身份**（包名与发布者）。v0.2.0 侧载包**不会原地升级**到 v0.3.0。
- 安装 v0.3.0 构建前，请先卸载旧的 v0.2.0 测试包。
- v0.2.0 的本地账户、余额历史和 Credential Locker API Key **不会自动迁移**；安装 v0.3.0 后需要重新添加账户与 API Key。

从 **v0.3.1** 开始，安装程序支持真正的原地升级；**v0.4.0** 在 v0.3.1 之上原地升级，会保留你的账户、余额历史、阈值、紧凑窗口设置和 Credential Locker API Key。安装程序**不会**自动开启“登录 Windows 时启动”。

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
- 通知区域驻留：关闭到托盘、托盘菜单、单实例启动、可选登录启动

## 安全与隐私设计

- API Key 保存在 **Windows 凭据管理器（Credential Locker）** 的 ApiMonitor 资源中，绝不写入 JSON、日志或诊断信息。
- API Key 只发送给对应 Provider 的官方接口（当前为 DeepSeek 官方余额接口）。
- 账户元数据、余额快照、历史记录和设置仅保存在本机应用数据目录。
- 自动刷新只在应用运行期间执行；关闭主窗口并隐藏到通知区域后进程仍在运行，自动刷新继续；只有选择“退出 ApiMonitor”才完全结束程序。
- 本版本没有系统通知中心推送、低余额 Toast、通知声音或托盘气泡；阈值状态只反映在托盘提示文本与菜单中。
- 登录启动为用户可选功能（默认关闭），仅驻留通知区域，不自动弹出主窗口。
- 托盘状态只显示余额状态摘要，不包含 API Key 或余额明细。
- 紧凑窗口只展示本机已有的账户与快照数据，不会额外上传任何内容。
- 复制 API Key 会短暂写入 Windows 剪贴板，应用约 30 秒后尝试安全清理（不会清除你之后复制的新内容）。
- 无遥测、广告和崩溃上传。

## 系统要求

- Windows 10 1809（build 17763）或更高版本；推荐 Windows 11
- x64
- Windows App Runtime 2.3.1 或更高版本

## 安装方式

推荐使用 Release 资产中的**完整测试包**（`Test.zip`），解压后即可全自动安装：

1. 下载 `ApiMonitor_0.4.0.0_x64_Test.zip`。
2. 解压到任意目录（路径可包含空格和中文）。
3. 双击 **`Install.cmd`**。
4. 在出现的**一次 UAC 提示**（用户帐户控制）中选择“是”。
5. 等待脚本自动完成校验、证书信任、依赖检查与安装/升级；询问是否启动时输入 `Y`（默认）即可。

卸载同样简单：双击 **`Uninstall.cmd`** 并按提示操作（可自行选择是否同时移除开发证书）。

> GitHub 侧载版本使用自签名开发证书。安装脚本会自动完成信任步骤，但**不会绕过** Windows 安全机制：
> - 证书只导入 **本地计算机 > 受信任人（Trusted People）**，绝不导入受信任根证书颁发机构。
> - 脚本在安装前校验 SHA-256、MSIX 签名与随包 CER 的**完整 Thumbprint**、证书 Subject（`CN=ApiMonitorDev`）、代码签名 EKU、有效期以及包 Identity。
> - 你仍需要确认一次 UAC，这是 Windows 对机器级证书信任的正常机制。
> - 请只安装你信任且来自官方仓库的证书。未来的 Microsoft Store 版本（v1.0）将由微软签名和分发，不再需要该流程。

图形化步骤、SmartScreen 说明、常见错误与退出码、手动安装备用方案和 SHA-256 校验方法见 [INSTALL.md](https://github.com/KiYouJyo/ApiMonitor/blob/main/packaging/installer/INSTALL.md)；卸载与证书清理说明见 [UNINSTALL.md](https://github.com/KiYouJyo/ApiMonitor/blob/main/packaging/installer/UNINSTALL.md)。

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
