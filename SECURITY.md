# Security Policy

## Supported versions

The latest release (currently v0.2.0) is the only supported version for security fixes.

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
