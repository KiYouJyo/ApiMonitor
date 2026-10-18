$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$base = Join-Path $PSScriptRoot '..\..\packaging\oneclick'
. (Join-Path $base 'payload\Bootstrap.ps1')
$work = Join-Path ([IO.Path]::GetTempPath()) ('ApiMonitor one-click installer tests ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null

function Assert-True($Condition, [string]$Label) {
    if (-not $Condition) { throw "OneClick test failure: $Label" }
}
function Assert-Rejected([scriptblock]$Operation,[string]$Label) {
    $rejected=$false
    try { & $Operation | Out-Null } catch { $rejected=$true }
    if (-not $rejected) { throw "OneClick test should reject: $Label" }
}
try {
    $version='1.3.0.1'
    $zipName='ApiMonitor_' + $version + '_x64_Test.zip'
    $meta=[pscustomobject]@{
        repository='KiYouJyo/ApiMonitor'; publisher='CN=ApiMonitorDev';
        packageName='ApiMonitor'; architecture='x64';
        displayVersion='1.3.0'; packageVersion=$version;
        releaseTag='v1.3.0'; offlineZipName=$zipName;
        offlineZipSha256=('A' * 64)
    }
    Assert-True (Test-SafeOneClickMetadata $meta) 'metadata pins official repo, version and publisher'
    $bad=$meta | ConvertTo-Json | ConvertFrom-Json
    $bad.publisher='CN=OtherPublisher'
    Assert-Rejected { Test-SafeOneClickMetadata $bad } 'different publisher'
    $bad.publisher='CN=ApiMonitorDev'; $bad.offlineZipName='../different.zip'
    Assert-Rejected { Test-SafeOneClickMetadata $bad } 'asset path injection'
    $bad.offlineZipName=$zipName; $bad.offlineZipSha256=('X'*64)
    Assert-Rejected { Test-SafeOneClickMetadata $bad } 'invalid SHA256'

    $stage=Join-Path $work ('ApiMonitor_' + $version + '_x64_Test')
    $dep=Join-Path $stage 'Dependencies\x64'
    New-Item -ItemType Directory -Path $dep -Force | Out-Null
    foreach ($name in @('Install.ps1','Uninstall.ps1','ApiMonitorDev.cer',
                       'SHA256SUMS.txt','SafeLocalStateBackup.ps1',
                       ('ApiMonitor_'+$version+'_x64.msix'))) {
        Set-Content -LiteralPath (Join-Path $stage $name) -Value $name -Encoding ASCII
    }
    Set-Content -LiteralPath (Join-Path $dep 'Microsoft.WindowsAppRuntime.2.msix') -Value 'fixture' -Encoding ASCII
    $zip=Join-Path $work $zipName
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
    Assert-True (Assert-OneClickZipLayout -ZipPath $zip -PackageVersion $version) 'valid staged ZIP has all required offline files'

    $unsafe=Join-Path $work 'unsafe.zip'
    Copy-Item -LiteralPath $zip -Destination $unsafe
    $archive=[IO.Compression.ZipFile]::Open($unsafe,[IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry=$archive.CreateEntry('ApiMonitor_'+$version+'_x64_Test/../../malicious.ps1')
        $writer=[IO.StreamWriter]::new($entry.Open())
        try { $writer.Write('not safe') } finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
    Assert-Rejected { Assert-OneClickZipLayout -ZipPath $unsafe -PackageVersion $version } 'ZIP path traversal'

    # Explicitly simulate a disconnected machine and ensure offline
    # uninstall returns without ever calling GitHub or touching packages.
    function Get-AppxPackage { param([string]$Name,[string]$ErrorAction) return }
    function Invoke-RestMethod { throw 'Network must NOT be invoked during uninstall.' }
    Assert-True ((Invoke-OneClickBootstrap -Operation Uninstall) -eq 0) 'offline uninstall does not query Release API'

    # Parser regression protects three Windows PowerShell 5.1 scripts.
    foreach ($scriptFile in @(
        (Join-Path $base 'payload\Bootstrap.ps1'),
        (Join-Path $base 'payload\TrustCertificate.ps1'),
        (Join-Path $base 'New-OneClickPackage.ps1')
    )) {
        $tokens = $null
        $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($scriptFile, [ref]$tokens, [ref]$parseErrors)
        Assert-True (($null -eq $parseErrors) -or $parseErrors.Count -eq 0) "PowerShell syntax valid: $scriptFile"
    }
    # Even when a GitHub package is installed, refusing confirmation must
    # never trigger Remove-AppxPackage.
    function Get-AppxPackage {
        param([string]$Name,[string]$ErrorAction)
        [pscustomobject]@{
            Name='ApiMonitor'; Publisher='CN=ApiMonitorDev';
            Version=[version]'1.3.0.1'; PackageFullName='ApiMonitor_1.3.0.1_x64__fixture'
        }
    }
    function Read-Host { param([string]$Prompt) return 'NO' }
    function Remove-AppxPackage { throw 'Unexpected destructive removal' }
    Assert-True ((Invoke-OneClickBootstrap -Operation Uninstall) -eq 0) 'offline uninstall refusal keeps installed package'

    $output=Join-Path $work 'publish'
    New-Item -ItemType Directory -Path $output | Out-Null
    $sha=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToUpperInvariant()
    $builder=Join-Path $base 'New-OneClickPackage.ps1'
    & $builder -DisplayVersion '1.3.0' -PackageVersion $version -PublishedOfflineZipPath $zip -PublishedOfflineZipSha256 $sha -OutputDirectory $output | Out-Null
    $root=Join-Path $output 'ApiMonitor-v1.3.0-x64-one-click-r2'
    $bootstrap=Join-Path $output 'ApiMonitor-v1.3.0-x64-one-click-r2.zip'
    Assert-True (Test-Path -LiteralPath $bootstrap) 'one-click package created'
    Assert-True ((Get-Item -LiteralPath $bootstrap).Length -lt 2MB) 'online ZIP remains lightweight'
    foreach ($name in @('1-Install-ApiMonitor.cmd','2-Uninstall-ApiMonitor.cmd','README.txt')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $root $name)) "root has $name"
    }
    Assert-True (Test-Path -LiteralPath (Join-Path $root 'payload\TrustCertificate.ps1')) 'certificate-only UAC helper included'
    $bootstrapContent = Get-Content -LiteralPath (Join-Path $root 'payload\Bootstrap.ps1') -Raw
    Assert-True ($bootstrapContent.Contains('if ($Operation -eq ''Uninstall'')')) 'offline uninstall branches before network'
    Assert-True ($bootstrapContent.Contains('return Invoke-OfflineUninstall')) 'offline uninstall never delegates to downloaded scripts'
    Assert-True ($bootstrapContent.Contains('Install-VerifiedOfflinePackageForCurrentUser')) 'MSIX installer executes for current user'
    Assert-True (-not $bootstrapContent.Contains(' -NoLaunch -Quiet')) 'no hidden original elevated installer process'
    $generated=Get-Content -LiteralPath (Join-Path $root 'payload\InstallerMetadata.json') -Raw | ConvertFrom-Json
    Assert-True ([string]$generated.offlineZipSha256 -ceq $sha) 'generated manifest pins exact published offline SHA'
    Assert-True (@(Get-ChildItem $root -Recurse -File | Where-Object { $_.Extension -in @('.msix','.pfx','.p12','.cer') }).Count -eq 0) 'online ZIP carries no binary installers or private certificates'
    Assert-Rejected { & $builder -DisplayVersion '1.3.0' -PackageVersion $version -PublishedOfflineZipPath $zip -PublishedOfflineZipSha256 ('0'*64) -OutputDirectory (Join-Path $work 'bad-output') } 'builder refuses wrong checksum'
    Write-Output 'OneClickInstaller.Tests.ps1 PASS'
}
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
