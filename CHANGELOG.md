# Changelog

## [Unreleased]

## [0.7.0.4] - 2026-08-04（v0.7.0 无边框悬浮窗候选包）

### Added

- 悬浮余额窗替代原紧凑窗口：轻量、始终置顶、不在任务栏占位的小窗口，只显示一个选定账户的主额度数字（账户名、Provider、单位、状态与最近更新时间），全局单实例，关闭不退出应用
- 账户卡片新增“设为悬浮窗”入口：记录该账户为悬浮窗账户并显示/切换；托盘菜单“打开紧凑窗口”改为“打开悬浮窗”
- 主额度数字统一选择规则集中封装（MainBalanceMetricSelector）：DeepSeek 用可用总余额、OpenRouter 普通 API Key 用剩余额度、OpenRouter Management Key 用剩余 Credits、无剩余值时选最合理可用指标、累计使用量不作为主数字
- floating-window-settings.json 持久化（位置/尺寸/选中账户/置顶），旧 compact-window-settings.json 首次启动一次性幂等迁移，不删除旧文件、不影响启动
- 应用图标整体替换为 TerminalShare 资产包：多尺寸 ICO（EXE/托盘）、Square44x44/Square71x71/Square150x150/Wide310x150/Square310x310/StoreLogo/SplashScreen 全 scale 与 targetsize/altform 变体、Store 列表图标 300×300

### Changed

- 主页 UI 整理：删除顶部副标题行与“打开紧凑窗口”按钮，保留主标题“ApiMonitor”与主要操作，压缩顶部间距
- 便携备份使用 floating-window-settings.json；兼容读取含旧 compact-window-settings.json 的 v0.6.0 备份
- 安装/卸载工具与备份校验接受 floating-window-settings.json（同时兼容旧文件名）
- 版本：DisplayVersion 0.7.0 / PackageVersion 0.7.0.4（集中版本来源 Directory.Build.props 与 Package.appxmanifest）
- 修复启动语言偏好未应用以及多语言资源未进入 MSIX 候选包的问题。
- 进一步将悬浮窗调整为黑白主题的小方块额度窗，整理主页顶部间距，并显式应用自定义图标到标题栏/窗口图标层。

### Fixed

- 清理所有旧“紧凑窗口”用户可见文案与入口（主页按钮、托盘菜单、设置/文档/备份说明），统一为“悬浮窗”

## [0.6.0.1] - 2026-08-03（v0.6.0 主题统一修订包）

### Fixed

- 主题未实际应用：AppearanceSettingsViewModel 构造函数在读取持久化设置前把默认主题 System 写回文件，导致手动选择的 Light/Dark 永远无法恢复；改为构造期直接赋值后备字段
- 应用外壳背景不统一：标题栏（原生 AppWindowTitleBar 浅青）、NavigationView Pane（Acrylic 深色）、页面内容三处各色；新增统一语义资源（AppShellBackgroundBrush/AppCardBackgroundBrush 等，Light/Dark/HighContrast 三字典），NavigationView Pane 覆盖为外壳背景，页面根继承统一背景
- 新增 WindowThemeCoordinator：统一管理窗口根元素主题与原生标题栏颜色同步（含紧凑窗口），高对比度回退系统默认标题栏
- 卡片背景统一为 AppCardBackgroundBrush（设置/账户/关于页卡片），与外壳背景保持层级

## [0.6.0] - 2026-08-03

### Added

- 数据洞察页（导航主菜单）：账户/指标/时间范围选择、轻量本地趋势图（Canvas/Polyline，无图表框架）、当前值/区间变化/首末与极值、可折叠历史表、CSV 导出
- 消费估算服务：按连续快照计算日消耗中位数与预计可用天数，至少 3 个有效区间与 24 小时跨度；数据不足/未观察到消耗/最近充值/不支持指标/当前值未知等明确原因
- 便携备份（.apimonitor-backup，ZIP+JSON）：manifest（backupFormatVersion=1、文件大小/SHA-256、containsSecrets=false）、导出不含 API Key/凭据/日志、导入安全合并（保留本机凭据、新账户标记需要凭据、历史按 Id 去重、失败回滚）、路径穿越/超大文件拒绝
- 主题设置：跟随系统/浅色/深色，立即生效并应用到主窗口与紧凑窗口，持久化
- 三语支持：简体中文/English/日本語，x:Uid + ResourceLoader + 统一字符串服务；语言切换保存 PrimaryLanguageOverride 并提示重启（AppInstance.Restart）
- 完整“关于”页：产品信息（DisplayVersion 与 PackageVersion 分离）、当前能力（Provider 注册表动态）、隐私与安全摘要、项目链接、本地文档（离线查看）、手动检查更新（GitHub REST、15 秒超时、语义版本比较）、复制诊断信息（非敏感）、打开本地数据文件夹
- 集中版本来源 Directory.Build.props：DisplayVersion=0.6.0 / PackageVersion=0.6.0.0，不再“强制 major.minor.0”
- 统一元数据服务 AppInfo：打包/未打包安全回退，不因 Package.Current 不可用崩溃

### Changed

- 分离 DisplayVersion 与 PackageVersion（集中版本来源 Directory.Build.props）
- 数据洞察历史按需加载与图表分桶抽样（约 500 点，保留首末与极值）
- 统一标题栏、导航面板与页面背景（详见 0.6.0.1 修订条目）
- 高 DPI、高对比度与键盘无障碍改进
- 本地化扩展到对话框、托盘菜单、系统通知与 Provider 错误文案
- 导航结构：主页/数据洞察为主菜单，设置/关于移到底部区域；四页共享同一账户服务与状态，切换不重启调度器/不重复订阅/不重复读取 Credential Locker
- 托盘菜单与系统通知文本接入 IAppStrings 本地化
- Package.appxmanifest 声明 zh-CN/en-US/ja-JP 三种语言；版本升级到 0.6.0.0

### Fixed

- 修复了“查看趋势”入口：从账户卡片可进入数据洞察并自动选择该账户（以 AccountId 为主键）
- 修复缺失或混杂语言的界面字符串（x:Uid 失效，改用 Loc 附加属性本地化）
- 修复页面重叠回归与语言/主题状态显示
- 页面与对话框主题同步、外壳配色不一致（详见 0.6.0.1 修订条目）

### Security

- 便携备份明确 containsSecrets=false；CSV/备份/诊断信息不含 API Key、凭据、Authorization、日志与本机路径
- 更新检查仅手动触发、不上传任何账户/余额/设备数据、不自动下载安装、User-Agent=ApiMonitor/&lt;DisplayVersion&gt;

## [0.5.0.1] - 2026-08-03（v0.5.0 验收候选修订包；修复安装/备份安全）

- 禁止同版本破坏性替换：已安装版本与待安装版本相同时默认停止（退出码 15），绝不自动卸载/重置包/删除 LocalState/操作 Credential Locker
- 新增统一 LocalState 备份/校验/恢复工具（packaging/tools/SafeLocalStateBackup.ps1）：动态解析 Package Family、逐项 -LiteralPath 复制、数量/字节/哈希/JSON/非零/清单全量校验
- 破坏性重装仅在显式参数 -ForceDestructiveReinstall + 人工确认后执行，且必须先行通过备份校验（备份失败停止，退出码 16）
- 升级前尽力备份 LocalState；备份失败不阻塞安全的标准 MSIX 原地升级
- 候选包版本策略：MSIX 使用 0.5.0.1（后续 0.5.0.2/0.5.0.3…），应用界面与 GitHub 版本仍为 v0.5.0
- Install.ps1/Uninstall.ps1 退出码表补充 15/16；Installer 测试新增 39 项断言

## [0.5.0-UI] - 2026-08-03（验收前主界面信息架构修正，仍在 0.5.0.0）

- 主窗口改为 NavigationView 导航外壳：主页（账户仪表盘）与独立设置页
- 恢复醒目的“添加账户”入口（顶部主按钮 + 空状态“添加第一个账户”）
- 账户卡片恢复完整信息展示（指标/监测状态/通知状态/统一操作栏），修复卡片过矮裁切
- 汇总与筛选框增加明确标签；通知激活强制回到主页并定位账户
- 删除账户确认包含 Provider、删除范围与不可撤销提示

## [0.5.0] - 2026-08-03

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

