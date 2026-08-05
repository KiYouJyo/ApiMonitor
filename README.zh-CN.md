# ApiMonitor

[English](README.md) · [日本語](README.ja-JP.md)

**ApiMonitor** 是一款基于 WinUI 3 的轻量 Windows 桌面应用，用于查询并记录你自己的 API 账户余额。支持 **DeepSeek**、**OpenRouter**、**Moonshot / Kimi**、**SiliconFlow** 与 **xAI** 余额查询，以及多账户管理与可选的 Windows 通知中心低余额提醒。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

- 当前版本：**v0.8.0**（DisplayVersion `0.8.0` / PackageVersion `0.8.0.0`）
- 运行时：.NET 10 / Windows App SDK 2.x，x64
- 分发：MSIX 侧载（自签名开发证书）以及未来的 Microsoft Store（计划 v1.0）
- 许可证：[MIT](LICENSE)
- 语言：简体中文 · English · 日本語（设置 → 外观与语言 中切换）

## 升级说明

- **v0.8.0** 在 v0.7.0 之上**原地升级**：账户、AccountId、Credential Locker API Key、最新余额、历史记录、阈值、自动刷新 / 通知 / 托盘 / 悬浮窗 / 登录启动 / 外观（主题与语言）设置全部保留；非敏感 Provider 配置（如 xAI Team ID）一并保留，密钥仍只保存在 Credential Locker。v0.7.0 数据文件无需提升 schema 版本。
- **v0.7.0** 在 v0.6.0 之上**原地升级**：账户、AccountId、Credential Locker API Key、最新余额、历史记录、阈值、自动刷新 / 通知 / 托盘 / 悬浮窗 / 登录启动 / 外观（主题与语言）设置全部保留。旧 `compact-window-settings.json` 在首次启动时一次性、幂等迁移为 `floating-window-settings.json`。安装程序**不会**自动开启系统提醒，也**不会**自动开启登录启动。
- **v0.6.0** 在 v0.5.0 之上原地升级（历史版本）。
- **v0.2.0 侧载包不会原地升级**：请先卸载旧包，再重新添加账户。

## 主要功能

- 同一 Provider 支持多个账户（如多个 DeepSeek 账户、多个 OpenRouter 密钥）
- **DeepSeek**、**OpenRouter**、**Moonshot / Kimi**、**SiliconFlow** 与 **xAI** 五个 Provider，选择项由 Provider 注册表动态生成（不写死在 XAML）
- **OpenRouter 两种凭据模式**：
  - **普通 API Key**：密钥剩余额度 / 额度上限，以及累计、今日、本周、本月使用量
  - **Management Key**：账户总 Credits（剩余 = 总充值 − 总使用，负值不钳制为 0）
- **Moonshot / Kimi**（v0.8.0）：使用普通 API Key 查询 `GET https://api.moonshot.cn/v1/users/me/balance`，显示可用余额（人民币元，官方 `available_balance` = 现金 + 代金券）、现金余额与代金券余额。缺失字段映射为 null（绝不显示为 0）；主指标为可用余额，现金与代金券不重复相加。
- **SiliconFlow**（v0.8.0）：使用普通 API Key 查询 `GET https://api.siliconflow.cn/v1/user/info`，只读取余额字段（主指标 `totalBalance`，次级 `balance` / `chargeBalance` / 可选 `grantedBalance`），用户资料一律忽略；完整响应绝不写入日志；官方结构变化时返回“响应结构暂不支持”，不误显示为 0。
- **xAI**（v0.8.0）：使用 **Management API** 而非推理 API —— `GET https://management-api.x.ai/v1/billing/teams/{team_id}/prepaid/balance`，需要 **Management Key** 与 **Team ID**。普通模型 API Key 不能查询余额。官方“Representation of USD Cents”账务值按文档转换为美元预付费 Credits；透支负值原样保留，不钳制、不 `Math.Abs`。
- Provider 能力元数据（v0.8.0）：每个 Provider 声明默认官方 Base URL、必填非敏感配置字段（如 xAI Team ID）、主指标、币种、是否支持多币种 / 余额分项 / 凭据验证；官方 Provider 默认**不允许**自定义端点。
- 多账户汇总（总数 / 低余额数 / 查询失败数）、Provider 筛选与状态筛选（全部 / 正常 / 低余额 / 未知 / 失败）
- 逐账户刷新与“刷新全部账户”（复用账户级并发锁，一个账户失败不影响其他账户）
- 逐账户历史、阈值、自动刷新与通知设置
- 通用 **BalanceMetric** 指标模型：货币余额、平台 Credits、密钥额度与使用量使用统一稳定表示；未知数值为 null（绝不用 0 表示），无限额度绝不误触发低余额提醒
- **Windows 通知中心低余额提醒**（AppNotification）：
  - 首次低余额提醒、重复提醒冷却（不重复 / 6 小时 / 12 小时 / 24 小时 / 3 天）与余额恢复提醒
  - 通知按钮：“打开账户”与“暂停提醒 24 小时”
  - 按账户 + 指标维护通知状态，快照去重，多指标合并为一条账户级通知，稳定 Tag 替换旧提醒
  - 测试通知按钮；升级后**全局系统提醒默认关闭**
- 通知区域常驻、关闭到托盘、单实例、可选登录启动与 Explorer 重启恢复（v0.4.0 行为保持不变）
- **悬浮余额窗**（v0.7.0）：轻量、黑白主题、固定尺寸、圆角、单层小方块样式的始终置顶悬浮额度窗，只显示一个选定账户的账户名、Provider、主额度数字、单位和简短状态。不在任务栏单独占位，关闭不退出应用，全局仅一个实例；可从任意账户卡片（**设为悬浮窗**）或托盘菜单（**打开悬浮窗** / **关闭悬浮窗**）打开、切换与关闭，选中账户与窗口位置自动记住并恢复（旧紧凑窗口设置自动迁移）。支持 Windows 原生流畅拖动。替代原紧凑窗口。
- 主页顶部布局已精简：删除旧副标题，标题、操作按钮、提示条与账户概览整体上移，不再为副标题保留空白。
- 应用图标已整体替换：EXE/窗口（标题栏）图标、任务栏/开始菜单包徽标、启动画面、托盘图标与 Store 列表图资全部更新为新的 `ApiMonitor.ico` / `TrayIcon.ico` 与包徽标资产。
- **数据洞察页**（v0.6.0）：账户 / 指标 / 时间范围选择、轻量本地趋势图（WinUI 原生，不引入图表框架）、当前值、区间变化、首末与极值、可折叠历史表、CSV 导出
- **消费估算**（v0.6.0）：按有效区间中位数估算每日消耗与预计可用天数，仅基于本机历史；明确标注“估算值”并附说明，数据不足 / 未观察到消耗 / 最近充值 / 指标不支持预测 / 当前值未知等显示明确原因
- **便携备份**（v0.6.0，v0.7.0 / v0.8.0 更新）：设置 → 数据管理 中导出 / 导入 `.apimonitor-backup`（ZIP+JSON）——账户元数据（含 xAI Team ID 等非敏感配置）、Provider 非敏感设置、余额历史、阈值、自动刷新 / 通知 / 托盘 / 悬浮窗 / 外观设置。v0.8.0 / v0.7.0 备份使用 `floating-window-settings.json`，仍可导入含旧 `compact-window-settings.json` 的 v0.6.0 备份。**绝不包含 API Key、Management Key 或凭据**；导入为安全合并（已有账户保留本机凭据、新账户标记需重新输入密钥、历史按稳定 Id 去重、失败回滚）
- **主题设置**（v0.6.0）：跟随系统 / 浅色 / 深色，立即应用到主窗口与悬浮窗并持久化
- **统一应用外壳**（v0.6.0）：标题栏、导航面板与页面背景共享统一主题表面（浅色 / 深色 / 高对比度一致）
- **三语界面**（v0.6.0）：简体中文 / English / 日本語。切换语言会保存偏好、提示重启并通过 `AppInstance.Restart` 重启，不会出现半本地化
- **完整“关于”页**（v0.6.0）：产品信息（DisplayVersion 与 PackageVersion 分离）、动态 Provider 列表、隐私与安全摘要、项目链接、本地文档（离线查看）、手动检查更新（GitHub REST，仅点击时访问，不自动下载安装）、复制诊断信息（非敏感）、打开本地数据文件夹
- API Key 一键安全复制（约 30 秒后尝试从剪贴板清理）

## 安全与隐私设计

- API Key 保存在 **Windows 凭据管理器（Credential Locker）** 的 ApiMonitor 资源中，绝不写入 JSON、日志或诊断信息。
- 密钥只发送给对应 Provider 的官方 HTTPS 主机（DeepSeek `api.deepseek.com`、OpenRouter `openrouter.ai`、Moonshot `api.moonshot.cn`、SiliconFlow `api.siliconflow.cn` / `api.siliconflow.com`、xAI Management API `management-api.x.ai`）。共享的凭据主机白名单在发送前校验每个请求，非 HTTPS 或非白名单目标一律拒绝。OpenRouter Management Key 只用于 Credits 接口；xAI Management Key 只发往 `management-api.x.ai`，绝不发往推理端点。
- 余额查询只调用 GET 且无副作用的官方接口；ApiMonitor 绝不发送模型推理请求，因此查询余额不会消耗 Token 或产生调用费用。
- 超时、429 与 5xx 响应会做有限次数、可取消的重试；401 / 403 / 404 与配置类错误绝不自动重试。
- 账户元数据、余额快照、历史记录、设置与通知状态仅保存在本机应用数据目录。
- 通知由本机 ApiMonitor 进程在运行期间生成；通知参数只包含非敏感标识（`action`、`accountId`、`providerId`、`metricId`），绝不包含 API Key、余额正文、Authorization、凭据资源或本机路径。
- **无云端推送、无 WNS 远程推送、无遥测、无开发者服务器**。选择“退出 ApiMonitor”后不再查询或发送新提醒。
- 便携备份与 CSV 导出**绝不包含** API Key、凭据、Authorization、日志与本机路径。
- 更新检查仅在点击“检查更新”时运行，不上传账户 / 余额 / 设备数据，绝不自动下载或安装。
- 自动刷新只在应用运行期间执行；关闭主窗口并隐藏到通知区域后进程仍在运行，自动刷新继续；只有选择“退出 ApiMonitor”才完全结束程序。
- 登录启动为用户可选功能（默认关闭），仅驻留通知区域，不自动弹出主窗口。

## 系统要求

- Windows 10 1809（build 17763）或更高版本；推荐 Windows 11
- x64
- Windows App Runtime 2.3.1 或更高版本

## 安装方式

推荐使用 Release 资产中的**完整测试包**（`Test.zip`），解压后即可全自动安装：

1. 下载 `ApiMonitor_0.8.0.0_x64_Test.zip`。
2. 解压到任意目录（路径可包含空格和中文）。
3. 双击 **`Install.cmd`**。
4. 在出现的**一次 UAC 提示**（用户帐户控制）中选择“是”。
5. 等待脚本自动完成校验、证书信任、依赖检查与安装/升级；询问是否启动时输入 `Y`（默认）即可。

卸载同样简单：双击 **`Uninstall.cmd`** 并按提示操作（可自行选择是否同时移除开发证书）。

> GitHub 侧载版本使用自签名开发证书。安装脚本会自动完成信任步骤，但**不会绕过** Windows 安全机制：
> - 证书只导入 **本地计算机 > 受信任人（Trusted People）**，绝不导入受信任根证书颁发机构。
> - 脚本在安装前校验 SHA-256、MSIX 签名与随包 CER 的**完整 Thumbprint**、证书 Subject（`CN=ApiMonitorDev`）、代码签名 EKU、有效期以及包 Identity。
> - 你仍需要确认一次 UAC，这是 Windows 对机器级证书信任的正常机制。

图形化步骤、SmartScreen 说明、常见错误与退出码、手动安装备用方案和 SHA-256 校验方法见 [INSTALL.md](https://github.com/KiYouJyo/ApiMonitor/blob/main/packaging/installer/INSTALL.md)；卸载与证书清理说明见 [UNINSTALL.md](https://github.com/KiYouJyo/ApiMonitor/blob/main/packaging/installer/UNINSTALL.md)。

常见安装与通知问题参见 [SUPPORT.md](SUPPORT.md)。

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

项目使用单项目 MSIX 工具链。自 v0.3.0 起使用完整 ApiMonitor 身份（`ApiMonitor` / `CN=ApiMonitorDev`）；Package Family 与 Credential Locker 资源保持不变。

第三方组件仍受其各自许可证约束，参见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。本项目与 DeepSeek、OpenRouter、Moonshot、SiliconFlow、xAI、Microsoft 均无隶属关系。

## 项目结构

```text
ApiMonitor.slnx
ApiMonitor.csproj          主 WinUI 3 应用项目（x64）
App.xaml / MainWindow.xaml 应用与主窗口
Views/                     主页面、账户编辑对话框、历史记录对话框
Views/FloatingBalanceWindow  轻量置顶悬浮余额窗
ViewModels/                MVVM 视图模型
Models/                    领域模型（含通用 BalanceMetric）
Providers/                 余额 Provider（DeepSeek、OpenRouter、Moonshot、SiliconFlow、xAI）与注册表
Services/                  存储、密钥、刷新、历史、阈值、通知、剪贴板、窗口管理服务
tests/ApiMonitor.Tests/    xUnit 测试套件
tests/installer/           安装工具测试
.github/workflows/ci.yml   CI 工作流
```

## 当前限制

- 通知只能根据 ApiMonitor 进程运行期间获得的查询结果产生。登录启动与托盘驻留可让应用持续监测，但选择“退出 ApiMonitor”后监测停止；无 Windows Service、无退出后的定时查询。
- 无云端推送（WNS）、邮件、短信或 Webhook。
- 消耗与可用天数估算仅基于本机历史，界面明确标注“估算值”。
- 语言切换需要重启应用。
- 旧指标的历史显示标签可能保留原有文本。
- 更新检查仅手动触发，应用不会自动下载或安装更新。
- Microsoft Store 上架计划在 v1.0 进行。
- 官方支持 DeepSeek、OpenRouter、Moonshot、SiliconFlow 与 xAI 五个 Provider，不支持第三方 DLL Provider 动态加载。
- SiliconFlow 余额接口不返回币种字段，按平台计价惯例视为人民币（CNY）；官方结构变化时应用返回“响应结构暂不支持”，不做猜测显示。
- GitHub 侧载版使用自签名开发证书（`CN=ApiMonitorDev`）。

## 路线图

- Microsoft Store 上架（v1.0）
- 更多余额 Provider
- 本地化完善

## 隐私

参见 [PRIVACY.md](PRIVACY.md)。

## 报告安全问题

参见 [SECURITY.md](SECURITY.md)。请使用 GitHub Private Vulnerability Reporting，切勿在 Issue 中粘贴真实 API Key。

## 参与贡献

参见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 支持

参见 [SUPPORT.md](SUPPORT.md)。如遇问题或想提需求，请在 [Issues](https://github.com/KiYouJyo/ApiMonitor/issues) 中提交。
