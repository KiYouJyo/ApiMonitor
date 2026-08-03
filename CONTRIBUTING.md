# Contributing

Thanks for your interest in contributing to ApiMonitor.

## Development environment

- Windows 10/11, x64
- .NET 10 SDK and the Windows App SDK / WinUI 3 workload (Visual Studio recommended)
- Developer mode enabled on Windows for MSIX sideloading

## Branches and pull requests

- Work on a feature branch with a descriptive name (e.g., `feature/...`).
- Open a pull request against `main`.
- Keep changes focused; avoid unrelated reformatting.

## Tests

- All changes must keep the existing test suite green:

  ```powershell
  dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug
  ```

- Add tests for new behavior, especially anything touching data migration or secrets.

## Security rules

- Never commit real API keys (`sk-...`), PFX/P12 files, private keys, `LocalState`, user JSON, or app logs.
- Never paste real API keys into issues, PRs, or logs.
- When adding a provider, keep the API key out of URLs, JSON, logs, exceptions, and telemetry.

## Provider extension guidelines

- Implement `IApiBalanceProvider` and register it through `ProviderRegistry`.
- Map provider-specific responses to the common domain models.
- Keep credentials out of any persisted or logged data.
