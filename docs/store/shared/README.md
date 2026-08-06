# Store materials (v1.0.0)

This folder contains the Microsoft Store listing materials for the v1.0.0
candidate. It is a drafting aid only; nothing is uploaded to Partner Center
by any script or workflow in this repository.

## Structure

- `../zh-CN/`, `../en-US/`, `../ja-JP/` — trilingual listing copy
  (product name, short/full description, features, what's new, privacy
  summary, support, keywords, system requirements, affiliation disclaimer,
  certification notes)
- `ScreenshotPlan.md` — screenshot list and capture rules
- `AssetInventory.md` — image assets and their provenance
- `URLs.md` — public URLs used by the listing (privacy, support, etc.)

## Hard rules for all Store materials

- No real API keys, Authorization headers, account names, balances, local
  paths, logs, or debug UI in any screenshot or copy.
- Screenshots use dedicated test accounts or safe mock data only.
- No claims that ApiMonitor is official DeepSeek/OpenRouter software, that
  balances are processed by developer servers, that monitoring continues
  after full exit, or that the Store build preserves old sideload data.
- The Store package version is fixed at `1.0.0.0`; this candidate is pending
  manual acceptance before any submission.
