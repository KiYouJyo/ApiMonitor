## Summary

<!-- What does this PR change and why? -->

## Checklist

- [ ] Builds with `dotnet build ApiMonitor.slnx -c Debug -p:Platform=x64` (0 warnings / 0 errors)
- [ ] All tests pass: `dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug`
- [ ] No real API keys (`sk-...`), PFX/P12 files, private keys, LocalState, user JSON, or logs are committed
- [ ] No secrets appear in logs, exceptions, or telemetry

## Security notes

<!-- Confirm that no credential material is included in this PR. -->

Never paste real API keys or Credential Locker data in the PR description or comments.
