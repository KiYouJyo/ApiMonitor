# 数据存储

本文档说明 ApiMonitor 在本机保存的数据文件与恢复行为。

## 数据目录

打包运行时使用 `ApplicationData.LocalFolder`（`%LOCALAPPDATA%\Packages\<PackageFamily>\LocalState`）；未打包调试运行时回退到 `%LOCALAPPDATA%\ApiMonitor`。

## 数据文件

| 文件 | 内容 | schema |
| --- | --- | --- |
| `accounts.json` | 账户元数据（AccountId、ProviderId、显示名、凭据存在标志、监控与阈值设置） | v3 |
| `balance-records.json` | 余额快照与历史（最新成功快照 + 历史条目，含指标明细） | v3 |
| `notification-settings.json` | 全局通知设置 | — |
| `notification-state.json` | 通知去重/暂停状态 | — |
| `tray-settings.json` | 托盘与登录启动设置 | — |
| `floating-window-settings.json` | 悬浮窗账户与位置 | — |
| `appearance-settings.json` | 主题与语言 | v1 |
| `onboarding.json` | 首次启动引导完成状态 | v1 |

账户 ID（AccountId）为稳定主键；显示名变化不影响数据关联。

## 写入与恢复

- 所有 JSON 采用“临时文件 + 原子替换”写入，避免半写文件。
- 读取失败（损坏/拒绝访问/磁盘问题）时把损坏文件备份为 `*.corrupt-<时间戳>.json` 并回退默认值，不阻塞启动。
- 单一设置文件损坏不影响账户；历史损坏不删除 Credential Locker。
- schema 升级幂等：升级前先备份原文件（`*.migrated-backup-*.json`），多次加载不会重复迁移。
- 不把 API Key 写入任何 JSON；磁盘只读/空间不足/拒绝访问时不崩溃（返回可理解的错误）。
- 数据操作支持取消并正确释放文件句柄。

## 凭据存储位置

API Key 等凭据只保存在 Windows Credential Locker（`ApiMonitor` 资源），从不写入本目录。卸载应用通常也会移除该应用在 Credential Locker 中的凭据。

## 跨渠道数据

Microsoft Store 版使用独立的 Package Family 与独立数据目录，按全新安装处理：不读取旧 GitHub 侧载版 LocalState，不迁移账户、历史、设置或凭据。两条渠道不能相互覆盖升级。
