# Security Policy

## Supported versions

The latest formal release (currently v0.9.0) is the supported version for security fixes. Older releases should upgrade in place to the latest release.

## Credential handling and host whitelist

- All credentials (API keys, Management Keys, secrets, tokens) are stored only in the Windows Credential Locker under the `ApiMonitor` resource; they never appear in JSON, logs, diagnostics, backups, CSV exports, tray tooltips, notification arguments, or activation parameters. Multi-slot credentials (Key+SK, Basic username/password, Bearer token, query token) are stored as independent entries; account JSON only records presence flags.
- Every credential-bearing request is validated against the target before any credential is attached:
  - DeepSeek key → `api.deepseek.com`
  - OpenRouter key / Management Key → `openrouter.ai`
  - Moonshot key → `api.moonshot.cn`
  - SiliconFlow key → `api.siliconflow.cn` / `api.siliconflow.com`
  - xAI Management Key → `management-api.x.ai` (never the inference endpoint)
  - AMap key → `restapi.amap.com` (fixed public geocoding probe)
  - Baidu Maps AK → `api.map.baidu.com` (fixed public geocoding probe)
  - Tencent Location key → `apis.map.qq.com` (fixed public district probe)
  - Tianditu token → `api.tianditu.gov.cn` (fixed public place-search probe)
  - SuperMap iServer / generic OGC → only the user-configured self-hosted address (`http`/`https`; plaintext HTTP requires explicit confirmation)
- Official map providers are locked to their HTTPS hosts and do not allow custom Base URLs. Redirects are never followed, so credentials cannot be forwarded to another origin; self-hosted credentials never follow cross-host, cross-port, or HTTPS→HTTP redirects.
- OGC responses are parsed with a hardened XML reader (DTD/external entities/entity expansion disabled, size and depth limits, no XSLT); non-XML responses are rejected safely. Logs strip sensitive query parameters (`key`, `ak`, `tk`, `sig`, `sn`, `token`, …), and exceptions never contain full request URIs.
- No vendor console is scraped, no LAN scanning or port probing is performed, and self-hosted service addresses are never uploaded anywhere.
- For AI balance providers, timeout / 429 / 5xx responses are retried a limited number of times with cancellation; 401, 403, 404 and configuration errors are never retried. For map/GIS health probes, 429/quota-exceeded/401/403/key-invalid responses are never auto-retried.
- Balance queries only call side-effect-free official GET endpoints; ApiMonitor never sends model inference requests, so a balance query cannot consume tokens or generate charges. Map health probes use fixed public inputs, and each active probe may consume one API call (shown in the UI); new map accounts default to auto-refresh off.
- Do not paste real API keys, Management Keys, Credential Locker data, or unredacted logs into issues, backups, or pull requests.

## Reporting a vulnerability

Please use **GitHub Private Vulnerability Reporting** on this repository for any security issue. If private reporting is not available, open a private discussion or contact the maintainers through the repository issues and ask for a private channel.

Do **not** create a public issue that includes:

- Real API keys (`sk-...`) or credential material
- Credential Locker data or LocalState dumps
- Logs before you have checked them for secrets

## Response expectations

- Acknowledgment within 7 days
- Triage and an initial assessment within 14 days
- Coordinated disclosure after a fix is available

## Notes for reporters

- Never submit real API keys anywhere.
- Never commit PFX/P12 private keys, `LocalState`, user JSON, or app logs.
- If a log file is needed, redact everything that could be a secret first.
