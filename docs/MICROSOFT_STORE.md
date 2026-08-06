# Microsoft Store Preparation (ApiMonitor v1.0.0)

This document is a drafting aid only. It does not modify Microsoft Partner
Center, and nothing here is auto-submitted. Current Store status:
**candidate prepared — manual acceptance pending**.

## Verified Store identity (Partner Center, 2026-08-06)

| Field | Value |
| --- | --- |
| Reserved app name | ApiMonitor |
| Product ID | `9N6KR2XFMKQ2` |
| Package / Identity / Name | `JoKiy.ApiMonitor` |
| Package / Identity / Publisher | `CN=C4E4B33A-7B77-4121-897C-7D720A5471F8` |
| Publisher display name | Jo Kiyō |
| Package Family Name | `JoKiy.ApiMonitor_4wdwgytaw3v2m` |
| First published submission | None (never published) |
| Pending submission | Placeholder draft only — **not modified by this round** |

The values above come directly from `msstore apps get 9N6KR2XFMKQ2` /
`msstore submission get 9N6KR2XFMKQ2` and are stored in
[`Package.Store.appxmanifest`](../Package.Store.appxmanifest) and
[`Services/DistributionChannel.cs`](../Services/DistributionChannel.cs).
The GitHub sideload identity (`ApiMonitor` / `CN=ApiMonitorDev`) is kept
separate and is never used as a Store Publisher.

## v1.0.0 Store candidate (local build, not uploaded)

Build: `packaging/New-StorePackage.ps1 -SourceCommit <HEAD> -PackageVersion 1.0.0.0`

Result (see `packaging/output/v1.0.0/store/store-package-build.json` locally):

- `ApiMonitor_1.0.0.0_x64.msixupload` — unsigned (Store re-signs), x64,
  trilingual (zh-CN / en-US / ja-JP), `runFullTrust` capability only
- Identity validation: **Passed** (Name / Publisher / Version / languages /
  capabilities; no sideload tools, no `.cer`/`.pfx`/keys/logs/LocalState)
- Local acceptance MSIX (dev-signed, Store identity) for on-device manual
  acceptance only — never a Store upload artifact

## Submission boundaries (must stay manual)

- Uploading packages, creating/updating formal submissions, submitting for
  certification, publishing, pricing/market changes, and making the product
  page public are **not** performed by any script or workflow in this round.
- The existing placeholder draft is left untouched.
- `store-package.yml` is manual-only (`workflow_dispatch`), requires
  `STORE_1000_FROZEN` confirmation, contains no secrets, and never calls
  Partner Center.
- WACK report, listing copy, and screenshots are prepared locally under
  `artifacts/wack/v1.0.0/` and `docs/store/`.

## Checklist before a future submission (manual)

- [ ] v1.0.0 candidate accepted by the user (fresh Store-identity install,
      onboarding, accounts, notifications/tray/floating window, trilingual UI)
- [ ] WACK full validation passes with no unresolved failures
- [ ] Listing copy per language finalized (`docs/store/zh-CN|en-US|ja-JP`)
- [ ] Screenshots captured from the real UI with test data (no real keys)
- [ ] Privacy policy and support URLs reachable anonymously (GitHub pages)
- [ ] The frozen commit is tagged `v1.0.0` and its Store package is the only
      `1.0.0.0` package ever generated (no binary churn under one version)

## Must NOT claim

- ApiMonitor is official DeepSeek / OpenRouter software
- Balance data is processed by developer servers
- Monitoring continues after the app has fully exited
- The Store build preserves old GitHub sideload data
