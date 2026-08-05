# ApiMonitor v0.9.0 Release Notes

简体中文 / English / 日本語

---

## 简体中文

### ApiMonitor v0.9.0（正式版）

**主要更新**

从最新公开版本 v0.7.0 升级到 v0.9.0，你将获得：

- v0.8.0 新增的余额 Provider：**Moonshot / Kimi**、**SiliconFlow**、**xAI**（AI 余额 Provider 共 5 个：DeepSeek、OpenRouter、Moonshot / Kimi、SiliconFlow、xAI）
- v0.9.0 新增的**地图与 GIS 服务健康监测**：高德开放平台、百度地图开放平台、腾讯位置服务、天地图、SuperMap iServer、通用 OGC 服务（WMS/WMTS/WFS），共 11 个 Provider

**地图与 GIS 服务健康监测（v0.9.0）**

- 四个地图开放平台使用官方 Web 服务接口的固定公开查询做健康探测（高德地理编码、百度地理编码、腾讯行政区划列表、天地图地名搜索 V2.0）；每次主动探测可能消耗一次调用额度，界面会明确提示
- 服务状态、凭据状态、权限状态、配额状态与延迟分开展示与记录；地图 Provider 绝不进入资金余额汇总
- SuperMap iServer 与通用 OGC 支持自托管地址；OGC 默认只调用 GetCapabilities（WMS/WMTS/WFS），不调用 GetMap/GetFeature
- 新地图账户默认关闭自动刷新；启用后默认 6 小时、最短 1 小时；429/配额/401/403/Key 无效不自动重试
- 地图平台没有公开精确剩余配额接口时，应用不伪造剩余次数，相关值保持“未知”

**隐私与安全**

- 所有凭据（API Key、SK、Token 等）只保存在 Windows Credential Locker；数据、历史与设置保存在本机；无遥测、无云同步、无开发者服务器
- 地图 Provider 锁定官方 HTTPS 主机；自托管 GIS 仅访问你配置的地址；日志剥离敏感查询参数
- 完全退出应用后停止自动刷新与通知

**升级兼容性**

- v0.9.0 是正式版，可以从现有兼容版本（v0.7.0 / v0.8.0）原地升级，账户、凭据、历史与设置全部保留；accounts/history JSON schema 保持 v3
- v0.8.0 未单独发布，其新增 Provider（Moonshot / SiliconFlow / xAI）随本版本一并交付

**安装方式**

- GitHub 侧载版：下载 `ApiMonitor_0.9.0.0_x64_Test.zip`，解压后双击运行 `Install.cmd`；安装过程会请求一次 UAC，用于信任开发证书
- 自动刷新只在进程运行时执行；Microsoft Store 版尚处于准备阶段，尚未上架

**已知限制**

- 四个地图平台无公开精确剩余配额接口，相关值保持未知，绝不伪造
- 天地图官方未公开 Token 无效/权限不足/调用超限的状态码，无法识别的状态码显示为带数值的安全 ProviderError
- MapGIS Server 通过通用 OGC Provider（WMS/WMTS/WFS GetCapabilities）监测

**校验信息**

- 正式资产：`ApiMonitor_0.9.0.0_x64.msix`、`ApiMonitor_0.9.0.0_x64_Test.zip`、`SHA256SUMS.txt`
- 安装前请将本机 SHA-256 与 Release 资产中的 `SHA256SUMS.txt` 逐字符比对

---

## English

### ApiMonitor v0.9.0 (stable)

**Highlights**

Upgrading from the latest public release v0.7.0 to v0.9.0 gives you:

- The v0.8.0 balance providers: **Moonshot / Kimi**, **SiliconFlow**, **xAI** (five AI balance providers in total: DeepSeek, OpenRouter, Moonshot / Kimi, SiliconFlow, xAI)
- The v0.9.0 **map and GIS service health monitoring**: AMap, Baidu Maps, Tencent Location, Tianditu, SuperMap iServer, and generic OGC services (WMS/WMTS/WFS) — 11 providers in total

**Map and GIS service health monitoring (v0.9.0)**

- The four map platforms are probed through official Web Service APIs with fixed public inputs (AMap geocoding, Baidu geocoding, Tencent district list, Tianditu place search V2.0). Each active probe may consume one API call, which is shown in the UI.
- Service availability, credential status, permission status, quota state, and latency are tracked separately; map providers never enter the monetary balance summary.
- SuperMap iServer and generic OGC support self-hosted addresses; OGC only calls GetCapabilities by default (WMS/WMTS/WFS), never GetMap/GetFeature.
- New map accounts default to auto-refresh off (6 h default, 1 h minimum when enabled); 429/quota/401/403/key-invalid responses are never auto-retried.
- When a map platform exposes no exact remaining-quota API, the app keeps those values unknown instead of inventing them.

**Privacy and security**

- All credentials (API keys, secrets, tokens) stay in the Windows Credential Locker; data, history, and settings stay on your machine; no telemetry, no cloud sync, no developer servers.
- Map providers are locked to official HTTPS hosts; self-hosted GIS only reaches the address you configure; sensitive query parameters are stripped from logs.
- Auto refresh and notifications stop when you fully exit the app.

**Upgrade compatibility**

- v0.9.0 is a stable release and upgrades in place from compatible versions (v0.7.0 / v0.8.0), preserving accounts, credentials, history, and settings; the accounts/history JSON schema stays at v3.
- v0.8.0 was not released separately; its providers (Moonshot / SiliconFlow / xAI) are delivered together in this release.

**Installation**

- GitHub sideload: download `ApiMonitor_0.9.0.0_x64_Test.zip`, extract it, and run `Install.cmd`. The installer requests one UAC prompt to trust the developer certificate.
- Auto refresh runs only while the app is running. The Microsoft Store version is still in preparation and is not yet available.

**Known limitations**

- The four map platforms expose no exact remaining-quota API; those values stay unknown and are never invented.
- Tianditu does not officially document token-invalid/permission/quota-limit status codes; unrecognized codes are shown as a safe provider error with the numeric code.
- MapGIS Server is monitored through the generic OGC provider (WMS/WMTS/WFS GetCapabilities).

**Checksums**

- Release assets: `ApiMonitor_0.9.0.0_x64.msix`, `ApiMonitor_0.9.0.0_x64_Test.zip`, `SHA256SUMS.txt`
- Compare local SHA-256 hashes against `SHA256SUMS.txt` before installing.

---

## 日本語

### ApiMonitor v0.9.0（正式版）

**主な更新**

最新の公開版 v0.7.0 から v0.9.0 へのアップグレードで得られる内容：

- v0.8.0 の残高 Provider：**Moonshot / Kimi**、**SiliconFlow**、**xAI**（AI 残高 Provider は DeepSeek、OpenRouter、Moonshot / Kimi、SiliconFlow、xAI の計 5 つ）
- v0.9.0 の**地図・GIS サービス健全性監視**：高德、百度地図、テンセント位置情報、天地図、SuperMap iServer、汎用 OGC サービス（WMS/WMTS/WFS）— 合計 11 Provider

**地図・GIS サービス健全性監視（v0.9.0）**

- 4 つの地図プラットフォームは公式 Web サービス API の固定公開入力で健全性プローブ（高德ジオコーディング、百度ジオコーディング、テンセント行政区画一覧、天地図地名検索 V2.0）。アクティブなプローブは API 呼び出しを 1 回消費する可能性があり、UI に明示されます。
- サービス状態・認証情報状態・権限状態・クォータ状態・遅延は分離して記録され、地図 Provider は資金残高サマリーに一切入りません。
- SuperMap iServer と汎用 OGC はセルフホストアドレスに対応。OGC は既定で GetCapabilities（WMS/WMTS/WFS）のみを呼び、GetMap/GetFeature は呼びません。
- 新規地図アカウントは自動更新オフが既定（有効時は既定 6 時間・最小 1 時間）。429/クォータ/401/403/キー無効は自動リトライしません。
- 地図プラットフォームが正確な残りクォータ API を公開していない場合、値を偽造せず「不明」のままにします。

**プライバシーとセキュリティ**

- すべての資格情報（API キー、シークレット、トークン）は Windows Credential Locker にのみ保存。データ・履歴・設定はローカルのみ。テレメトリなし、クラウド同期なし、開発者サーバーなし。
- 地図 Provider は公式 HTTPS ホストに固定。セルフホスト GIS は設定したアドレスのみに接続。ログから機密クエリパラメータを除去。
- アプリを完全終了すると自動更新と通知も停止します。

**アップグレード互換性**

- v0.9.0 は正式版で、互換バージョン（v0.7.0 / v0.8.0）からその場でアップグレードでき、アカウント・資格情報・履歴・設定をすべて保持。accounts/history JSON schema は v3 のまま。
- v0.8.0 は単独でリリースされておらず、その Provider（Moonshot / SiliconFlow / xAI）も本バージョンに含まれます。

**インストール方法**

- GitHub サイドロード版：`ApiMonitor_0.9.0.0_x64_Test.zip` をダウンロードし、解凍後に `Install.cmd` を実行。開発者証明書を信頼するための UAC が 1 回要求されます。
- 自動更新はプロセス実行中のみ動作。Microsoft Store 版は準備段階であり、まだ公開されていません。

**既知の制限**

- 4 つの地図プラットフォームには正確な残りクォータを照会する公開 API がなく、値は不明のままで、偽造しません。
- 天地図はトークン無効・権限不足・呼び出し超過のステータスコードを公式公開していないため、認識できないコードは数値付きの安全な ProviderError として表示します。
- MapGIS Server は汎用 OGC Provider（WMS/WMTS/WFS GetCapabilities）で監視します。

**チェックサム**

- リリース資産：`ApiMonitor_0.9.0.0_x64.msix`、`ApiMonitor_0.9.0.0_x64_Test.zip`、`SHA256SUMS.txt`
- インストール前に `SHA256SUMS.txt` と SHA-256 ハッシュを照合してください。
