# Changelog

## [Unreleased]

## [0.5.0-UI] - 2026-08-03（验收前主界面信息架构修正，仍在 0.5.0.0）

- 主窗口改为 NavigationView 导航外壳：主页（账户仪表盘）与独立设置页
- 恢复醒目的“添加账户”入口（顶部主按钮 + 空状态“添加第一个账户”）
- 账户卡片恢复完整信息展示（指标/监测状态/通知状态/统一操作栏），修复卡片过矮裁切
- 汇总与筛选框增加明确标签；通知激活强制回到主页并定位账户
- 删除账户确认包含 Provider、删除范围与不可撤销提示

## [0.5.0] - 2026-08-03（开发版本，等待人工验收；未合并、未发布）

## [0.5.0] - 2026-08-03（开发版本，等待人工验收；未合并、未发布）

### Added

- 通用余额指标模型（BalanceMetric）：货币余额、平台 Credits、密钥额度、累计/周期使用量统一表示；未知数值为 null，无限额度不误触发低余额提醒
- 多账户管理：同一 Provider 可添加多个账户；主界面账户总数/低余额/查询失败汇总、Provider 与状态筛选、刷新全部账户
- OpenRouter Provider：普通 API Key（/api/v1/key）与 Management Key（/api/v1/credits）两种凭据模式，剩余 Credits = 总充值 − 总使用（负数不钳制）；凭据模式为账户非敏感设置，密钥仍只进 Credential Locker
- Windows 通知中心低余额提醒（AppNotification）：首次低余额、重复提醒冷却（不重复/6h/12h/24h/3d）、余额恢复提醒、暂停提醒 24 小时、快照去重、多指标合并、稳定 Tag 替换、测试通知
- 全局与每账户通知设置：升级后全局系统提醒默认关闭；已有 DeepSeek 阈值保留
- 通知注册与单实例激活整合：先绑定 NotificationInvoked 再 Register；通知点击打开/定位对应账户，第二实例重定向，退出时 Unregister
- 数据迁移：账户/余额文件升级到通用指标结构（账户 schema 2→3、余额记录 schema 2→3、设置体系 tray-settings 4→5），迁移前备份旧文件且幂等
- 安装与卸载工具升级到 0.5.0.0，支持 v0.4.0 原地升级，安装脚本不自动开启通知与登录启动

### Changed

- 余额快照与历史记录改用稳定 MetricId；阈值规则按 MetricId 关联
- Package.appxmanifest 增加 windows.toastNotificationActivation 与 comServer 扩展（AppNotification 激活）
- 主界面、紧凑窗口、托盘状态与通知状态共用同一套阈值计算

### Fixed

- 修复了升级后凭据模式变化会误要求重新测试连接的问题

### Security

- 通知参数只包含 action/accountId/providerId/metricId 非敏感标识；通知内容不含 API Key
- OpenRouter Management Key 权限说明与 403 提示；`limit_remaining=null` 显示为无限额度而非 0

## [0.4.0] - 2026-08-03

## [0.4.0] - 2026-08-03

### Added

- Native Windows notification-area icon (Shell_NotifyIconW, NOTIFYICON_VERSION_4, stable GUID, dedicated hidden message window)
- Close-to-tray behavior: closing the main window hides it to the notification area (configurable to exit instead)
- Tray menu for opening the main and compact windows, refreshing all accounts, toggling sign-in startup and exiting
- Single-instance activation: a second launch redirects to and activates the existing instance
- Optional start at Windows sign-in (MSIX StartupTask, off by default; sign-in startup stays in the tray only)
- Explorer restart recovery: the tray icon reappears automatically after Explorer restarts
- Tray Tooltip balance-status summary (normal / low-balance count / no data / refreshing / recent failure), without API keys
- Persistent notification-area and startup settings (tray-settings.json, schema 3 → 4)
- Multi-size tray icon (16/20/24/32/48/256)
- In-place upgrade from v0.3.1 preserving accounts, balance history, thresholds, compact-window settings and Credential Locker keys

### Fixed

- Fixed the tray context menu appearing near the upper-left corner
- Corrected NOTIFYICON_VERSION_4 callback parsing (event in LOWORD(lParam), icon ID in HIWORD(lParam), cursor anchor from GetCursorPos / wParam)
- Positioned the context menu near the cursor or the notification icon, with monitor-aware expansion direction
- Fixed reopening a hidden main window from the tray menu

### Changed

- Application lifecycle now remains active while the tray icon is running
- Closing the final visible window no longer necessarily exits the application
- Explicit exit now performs centralized cleanup (scheduler, in-flight requests, settings, tray icon, windows)
- Settings schema upgraded for tray and startup preferences

## [0.3.1] - 2026-08-03

- 新增一键侧载安装体验：双击 `Install.cmd`，确认一次 UAC 后自动完成证书导入、依赖检查与 MSIX 安装/升级
- 安装程序自动校验 SHA-256、MSIX 签名证书完整 Thumbprint、证书 Subject/EKU/有效期与包 Identity
- 证书只导入 Local Machine\TrustedPeople，绝不导入 Trusted Root；同 Thumbprint 已存在时跳过
- 支持 v0.3.0 → v0.3.1 原地升级，保留账户、余额历史、阈值、窗口设置与 Credential Locker 凭据
- 相同版本不重复安装；更高版本拒绝降级；同名不同 Publisher 的冲突包明确报错
- 依赖仅安装当前 x64 系统所需；已安装相同或更高版本时跳过
- 新增 `Uninstall.cmd`：仅卸载当前用户的 ApiMonitor（精确 Identity 匹配，不使用 -AllUsers），可选择按完整 Thumbprint 清理证书
- 安装/卸载日志写入 `%TEMP%`，不包含 API Key、Credential Locker 内容或余额数据
- 新增隔离临时目录运行安装工具测试（69 项），CI 中不修改机器级证书库、不安装真实包
- 更新 README、README.zh-CN、SUPPORT 与安装/卸载文档

## [0.3.0] - 2026-08-03

- 完成内部名称、包身份和开发签名身份的统一
- 新增紧凑余额窗口
- 支持始终置顶
- 支持账户与币种选择
- 主窗口和紧凑窗口状态同步
- 保存窗口位置、尺寸和置顶设置
- 多显示器与屏幕外位置恢复
- MIT License 与仓库维护完善
- 不再兼容旧包身份和旧本地数据（v0.2.0 无法原地升级；账户、余额历史与 API Key 不迁移）

## v0.2.0

发布日期：2026-08-03

### 新增

- 产品更名为 **ApiMonitor**
- 应用运行期间自动刷新余额
- 本地余额历史记录
- 按币种设置低余额阈值规则
- 自动刷新状态显示（开启/关闭、间隔、下次刷新时间）
- API Key 一键安全复制（写入剪贴板，约 30 秒后尝试清理）
- 未打包数据目录与 Credential Locker 资源统一使用 ApiMonitor 标识

### 修复

- “最近成功更新”时间绑定不再显示 `{x:Bind ...}` 文本
- 账户卡片“可用状态”在刷新后实时更新
- 添加/编辑账户对话框外框与按钮圆角统一
- 低余额阈值区域在无余额数据时显示明确提示，测试连接后同步币种余额
- 主界面版本号改为从程序集动态读取，避免与包版本不一致

### 兼容性

- 兼容读取 v0.1.0（schemaVersion 1）数据并迁移到 v0.2.0（schemaVersion 2）
- 保留 MSIX Identity、发布者、开发证书与包族，支持覆盖更新

## v0.1.0

发布日期：2026-07-30（约）

- 初始 DeepSeek Provider
- API Key 保存到 Windows Credential Locker
- 手动余额查询
- 账户添加、编辑与删除
- 本地余额快照

