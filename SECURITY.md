# Security Policy

## Supported versions

The latest formal release (currently v0.7.0) is the supported version for security fixes. The v0.8.0 acceptance candidate branch is under review and receives fixes through the normal review process.

## Credential handling and host whitelist

- All API keys and Management Keys are stored only in the Windows Credential Locker under the `ApiMonitor` resource; they never appear in JSON, logs, diagnostics, backups, CSV exports, tray tooltips, notification arguments, or activation parameters.
- Every credential-bearing balance request is validated against the provider's official HTTPS host whitelist before the Authorization header is attached:
  - DeepSeek key → `api.deepseek.com`
  - OpenRouter key / Management Key → `openrouter.ai`
  - Moonshot key → `api.moonshot.cn`
  - SiliconFlow key → `api.siliconflow.cn` / `api.siliconflow.com`
  - xAI Management Key → `management-api.x.ai` (never the inference endpoint)
- Non-HTTPS or non-whitelisted destinations are rejected; official providers do not allow user-customized Base URLs.
- Timeout, 429 and 5xx responses are retried a limited number of times with cancellation; 401, 403, 404 and configuration errors are never retried.
- Balance queries only call side-effect-free official GET endpoints; ApiMonitor never sends model inference requests, so a balance query cannot consume tokens or generate charges.
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
