# Provider 说明

本文档说明 ApiMonitor 支持的 Provider、认证模式与指标定义。首页只保留摘要，详细行为以本文档为准。

## AI 余额与额度 Provider

所有 AI 余额查询只调用官方 GET 接口（无副作用，不发送模型推理请求），因此查询余额不会消耗 Token 或产生费用。

### DeepSeek

- 接口：`GET https://api.deepseek.com/user/balance`
- 凭据：普通 API Key（单密钥）
- 指标：CNY 总余额（`totalBalance`）、充值余额（`toppedUpBalance`）、赠送余额（`grantedBalance`）

### OpenRouter

两种凭据模式：

- **普通 API Key**：`GET https://openrouter.ai/api/v1/key` — 密钥剩余额度 / 上限，以及累计、今日、本周、本月使用量。
- **Management Key**：`GET https://openrouter.ai/api/v1/credits` — 账户总 Credits（剩余 = 总充值 − 总使用；透支负值不钳制为 0）。Management Key 权限更高，只发送到 Credits 端点。

### Moonshot / Kimi

- 接口：`GET https://api.moonshot.cn/v1/users/me/balance`
- 凭据：普通 API Key
- 指标：可用余额（CNY，官方 `available_balance` = 现金 + 代金券）、现金余额、代金券余额。缺失字段为 null（绝不显示 0）；主指标为可用余额，现金与代金券不重复相加。

### SiliconFlow

- 接口：`GET https://api.siliconflow.cn/v1/user/info`
- 凭据：普通 API Key
- 指标：只读取余额字段（主指标 `totalBalance`，次级 `balance` / `chargeBalance` / 可选 `grantedBalance`），用户资料一律忽略。完整响应不写入日志；官方结构变化时返回“响应结构暂不支持”，不误显示 0。接口不返回币种字段，按平台计价惯例视为 CNY。

### xAI

- 接口（Management API）：`GET https://management-api.x.ai/v1/billing/teams/{team_id}/prepaid/balance`
- 凭据：Management Key + Team ID（Team ID 为非敏感配置字段）
- 指标：剩余预付费 Credits（USD）。官方“Representation of USD Cents”账务值按文档转换为美元；透支负值原样保留，不钳制、不取绝对值。普通模型 API Key 不能查询余额；Management Key 绝不发送到推理端点。

## 地图与 GIS 服务健康 Provider

地图平台通常只提供接口健康与凭据状态，**不提供公开精确剩余额度接口**。相关配额值保持未知（null），绝不伪造为 0 或百分比。每次主动探测可能消耗一次 API 调用额度（UI 明确提示）；新地图账户默认关闭自动刷新（启用后默认 6 小时、最短 1 小时）。

### AMap

- 接口：`GET https://restapi.amap.com/v3/geocode/geo`（固定公开地理编码输入）
- 凭据：Key（可选 SK 数字签名）

### Baidu Maps

- 接口：`GET https://api.map.baidu.com/geocoding/v3/`（固定公开地理编码输入）
- 凭据：服务端 AK（可选 SK）

### Tencent Location

- 接口：`GET https://apis.map.qq.com/ws/district/v1/list`（固定公开行政区划输入）
- 凭据：Key（可选 SK）

### Tianditu

- 接口：`GET https://api.tianditu.gov.cn/v2/search`（固定公开地名搜索输入）
- 凭据：Token

### SuperMap iServer（自托管）

- 接口：`{baseUrl}/iserver/services.json`（服务目录）
- 可选：预期服务检查；默认关闭的管理状态探测 `/iserver/manager/serverstatus.json`（需有权限凭据）
- 说明：HTTP 必须由用户显式确认；空服务目录不视为离线

### 通用 OGC（自托管）

- 接口：WMS 1.1.1/1.3.0、WMTS 1.0.0、WFS 1.0.0/2.0.0 的 GetCapabilities
- 默认只用 GetCapabilities，绝不调用 GetMap/GetFeature
- 安全 XML 解析：禁用 DTD/外部实体/实体扩展，限制大小与深度，不执行 XSLT
- 适用于 MapGIS Server、GeoServer、SuperMap 等

## 统一指标模型

- 资金余额、平台 Credits、密钥额度、使用量、服务健康使用统一 `BalanceMetric` 表示。
- 未知数值为 `null`（绝不用 0 表示）；无限额度绝不误触发低余额提醒。
- 地图/GIS 服务账户暴露服务可用性、延迟、凭据状态、权限状态、配额状态与计数；**绝不进入资金余额汇总**。

## 通知行为

- 低余额提醒：首次低余额、重复提醒冷却（不重复 / 6 小时 / 12 小时 / 24 小时 / 3 天）、余额恢复提醒；通知按钮“打开账户”/“暂停提醒 24 小时”。
- 服务健康通知：凭据无效、权限不足、服务未启用、配额耗尽、服务不可用、服务恢复、预期服务/图层缺失与恢复；瞬时错误连续两次后通知，恢复一次即通知，手动测试失败不通知。
- 通知中不含 API Key、Token、完整 URL、内网路径或服务目录内容。
