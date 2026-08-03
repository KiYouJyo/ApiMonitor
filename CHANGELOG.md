# Changelog

## [0.4.0] - 2026-08-03

- 新增通知区域托盘图标（Win32 Shell_NotifyIconW，NOTIFYICON_VERSION_4，稳定 GUID，独立隐藏消息窗口）
- 左键单击托盘图标打开/激活主窗口；右键弹出原生菜单（打开 ApiMonitor / 打开紧凑窗口 / 刷新全部账户 / 自动刷新状态 / 低余额状态 / 登录时启动 / 退出 ApiMonitor）
- 关闭主窗口默认隐藏到通知区域（设置可选“退出 ApiMonitor”）；首次隐藏前显示一次说明，可选择“不再提示”并持久化
- 全应用单实例：第二次启动重定向激活到已有进程并干净退出（AppInstance，固定实例键 `ApiMonitor.MainInstance`）
- 可选的“登录 Windows 时启动”（MSIX StartupTask，`ApiMonitorStartup`，默认关闭；系统状态为权威来源，不写 Run 键/启动文件夹/计划任务）
- 登录启动仅驻留通知区域并启动自动刷新，不弹出主窗口、不抢占焦点
- Explorer 重启后通过 TaskbarCreated 消息自动恢复托盘图标，不重复注册、不重启进程
- 托盘 Tooltip 随余额状态更新（正常 / N 个币种低于阈值 / 尚无余额数据 / 正在刷新 / 最近刷新失败），不包含 API Key
- 统一退出协调器：退出流程幂等，停止调度、取消在途请求、保存设置、删除托盘图标后进程干净退出
- 设置 schemaVersion 从 3 升级到 4（新增 tray-settings.json，v0.3.1 无此文件时使用默认值，迁移幂等）
- 新增多尺寸托盘 ICO（16/20/24/32/48/256，沿用蓝底白 A 意象）
- v0.3.1 可原地升级到 v0.4.0，保留账户、余额历史、阈值、紧凑窗口设置与 Credential Locker 凭据
- 更新 PRIVACY、SUPPORT、README 与安装/卸载文档

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

