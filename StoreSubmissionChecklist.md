# Store Submission Checklist (ApiMonitor v0.9.0)

This checklist contains **suggested values only**. Nothing here is written to
Partner Center automatically; every Store property below must be entered or
confirmed manually in the Partner Center submission.

## Suggested product properties

| Property | Suggested value |
| --- | --- |
| Category | Developer tools (if not available in Partner Center: Utilities & tools) |
| Pricing | Free |
| In-app purchases | None |
| Advertising | None |
| Architecture | x64 |
| Languages | zh-CN, en-US, ja-JP |
| Minimum OS | Use the actual manifest value (`10.0.17763.0` per `Package.appxmanifest`) |
| Internet connection | Required for configured API checks |
| Telemetry | None |
| Account required | No ApiMonitor account required; third-party API accounts are provided by the user |

## Verification checklist (local, before submission)

- [ ] GitHub v0.9.0 release is published (independent of Store submission)
- [ ] `Package.StoreAssociation.xml` exists (after Visual Studio “Associate App
      with the Store…”); otherwise the Store package is **Blocked: Store
      association required** (see `STORE_ASSOCIATION_REQUIRED.md`)
- [ ] Store package version is a legal four-part version with fourth part `0`
- [ ] No higher Store package version already submitted (never downgrade or
      reuse a submitted version)
- [ ] Store package contains no GitHub self-signed certificate, no
      `Install.cmd` / `Uninstall.cmd`, no sideload instructions
- [ ] Store package is not final-signed with `CN=ApiMonitorDev`
- [ ] Full regression passed: unit tests, installer tests, Debug x64,
      Release x64, `dotnet format`, 0 warnings / 0 errors
- [ ] WACK report saved and reviewed (no unresolved code or packaging failures)
- [ ] Trilingual listing copy complete (see `store-listing/v0.9.0/`)
- [ ] Screenshots (six suggested) captured from the real v0.9.0 UI, or
      `ScreenshotPlan.md` provided with “waiting for manual screenshots”
- [ ] Privacy policy and support URLs point to stable GitHub pages on `main`
- [ ] No real API keys, tokens, user data, or absolute local paths in any
      submission artifact

## Do NOT auto-modify

- Market availability
- Age-rating questionnaire
- Privacy-statement answers
- Release visibility
- Release date
- Staged rollout percentage

These must be decided and entered manually in Partner Center.
