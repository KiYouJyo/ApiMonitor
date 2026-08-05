# Microsoft Store Association Required

Status: **Blocked: Store association required**

ApiMonitor v0.9.0 GitHub Release preparation is complete, but the Microsoft
Store submission package has **not** been generated because the local project
is not yet associated with a Partner Center product identity.

## Current state (verified 2026-08-05)

- No `Package.StoreAssociation.xml` exists in the repository.
- No Store Identity (Package Name / Publisher ID) has been configured in
  `Package.appxmanifest`; the sideload identity remains
  `Name="ApiMonitor"` / `Publisher="CN=ApiMonitorDev"` (version `0.9.0.0`).
- No `msstore` CLI configuration or Store Product ID was found in the project.
- The GitHub sideload identity (`CN=ApiMonitorDev`) must **never** be used to
  construct a Store upload package, and it is **not** a valid Store Publisher.

## Required manual step in Visual Studio

1. Open `ApiMonitor.slnx` in Visual Studio.
2. Right-click the **ApiMonitor** project → **Publish** → **Associate App with
   the Store…**.
3. Sign in with the Partner Center account that reserved the **ApiMonitor**
   product, then select that reserved product.
4. Visual Studio will generate `Package.StoreAssociation.xml` and update the
   Store Identity in the package manifest. **Do not** hand-edit the Package
   Name or Publisher ID; only accept the values written by the association
   wizard.

Do **not** guess the Package Name or Publisher ID, and do not create a fake
Store Identity.

## After association completes, re-run these commands

```powershell
# 1. Confirm the associated identity and a legal four-part version (4th part 0)
dotnet build ApiMonitor.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64

# 2. Generate the Store upload package (framework-dependent, x64)
#    Target: packaging\store\v0.9.0\ApiMonitor_0.9.0.0_x64.msixupload

# 3. Verify the Store package locally (signature/identity, resources, size)
#    and record the report at packaging\store\v0.9.0\StorePackageReport.md

# 4. Run Windows App Certification Kit (WACK) on the Store candidate build
#    and save the report to packaging\store\v0.9.0\WACK\

# 5. Run the full regression suite before uploading to Partner Center:
dotnet format --verify-no-changes
dotnet restore ApiMonitor.slnx
dotnet test tests\ApiMonitor.Tests\ApiMonitor.Tests.csproj -c Debug
dotnet build ApiMonitor.slnx -c Debug -p:Platform=x64
dotnet build ApiMonitor.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
powershell -NoProfile -ExecutionPolicy Bypass -File tests\installer\Installer.Tests.ps1
```

## Constraints

- Store package version must be a legal four-part version with the fourth part
  `0` (e.g. `0.9.0.0`); if a higher Store package version already exists,
  verify it first and never downgrade or reuse a submitted version.
- The Store package must not contain the GitHub self-signed certificate,
  `Install.cmd` / `Uninstall.cmd`, or sideload instructions, and must not be
  final-signed with `CN=ApiMonitorDev`.
- Do not modify the GitHub sideload identity to accommodate the Store.
- Nothing in this file is auto-submitted to Partner Center; all Store actions
  remain manual.
