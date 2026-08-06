# 安全架构

本文档汇总 ApiMonitor 的凭据处理、网络与安全边界。首页只保留摘要，详细行为以本文档为准。

## 凭据存储

- 所有凭据（API Key、Management Key、SK、Token、Basic 用户名/密码等）只保存在 **Windows Credential Locker** 的 `ApiMonitor` 资源下。
- 多槽位凭据（Key+SK、Basic、Bearer Token、Query Token）作为独立条目保存；账户 JSON 只记录“是否存在”标志。
- 凭据绝不写入 JSON、日志、诊断信息、备份、CSV、托盘 Tooltip/菜单、通知参数、命令行参数、StartupTask 或激活参数。

## 请求主机白名单

每个携带凭据的请求在发送前都会校验目标：

- DeepSeek → `api.deepseek.com`
- OpenRouter 普通 Key / Management Key → `openrouter.ai`
- Moonshot → `api.moonshot.cn`
- SiliconFlow → `api.siliconflow.cn` / `api.siliconflow.com`
- xAI Management Key → `management-api.x.ai`（绝不发送到推理端点）
- AMap → `restapi.amap.com`（固定公开探测，不允许自定义 Base URL）
- Baidu Maps → `api.map.baidu.com`（同上）
- Tencent Location → `apis.map.qq.com`（同上）
- Tianditu → `api.tianditu.gov.cn`（同上）
- SuperMap iServer / 通用 OGC → 仅用户配置的自托管地址（http/https；明文 HTTP 需显式确认）

非白名单或非 HTTPS 目标一律拒绝，凭据不会附加。

## 网络与重试

- AI 余额查询：超时 / 429 / 5xx 有限重试且支持取消；401 / 403 / 404 / 配置错误绝不重试。
- 地图/GIS 探测：429 / QPS 超限 / 配额耗尽 / 401 / 403 / Key 无效绝不自动重试。
- 不跟随重定向：凭据绝不跨 Origin 转发；自托管凭据不跨主机、不跨端口、不 HTTPS→HTTP 降级。
- 不抓取厂商控制台、不扫描局域网、不探测其他端口；自托管服务地址绝不上传。
- 日志剥离 `key/ak/tk/sig/sn/token` 等敏感查询参数；异常不含完整请求 URI。

## 更新检查

- GitHub 侧载版：只在点击“检查更新”时访问 GitHub Releases API；User-Agent 仅含版本号；不上传账户/余额/设备数据；不自动下载/安装。
- Microsoft Store 版：只在点击“检查更新”时通过 StoreContext 查询；可主动请求 Store 下载/安装；绝不打开 GitHub 下载页。
- 开发构建：显示“当前为开发构建”，不检查正式更新。

## 无遥测、无广告、无开发者服务器

- 无遥测、无广告、无崩溃上传、无云同步、无开发者云端服务器。
- 通知由本机进程生成；退出应用后停止。
- 便携备份与 CSV 导出不包含任何凭据。

## 数据目录安全

- 账户/历史/设置文件使用“临时文件 + 原子替换”写入；损坏文件自动备份并回退默认值。
- 不把 API Key 写入 JSON；不因磁盘只读/空间不足/拒绝访问而崩溃。
- Store 版按全新安装处理：不读取旧侧载 LocalState、不枚举旧包 Credential Locker、不做跨包迁移。
