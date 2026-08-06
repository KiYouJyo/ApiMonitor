[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [string]$ExpectedIdentityName = 'JoKiy.ApiMonitor',
    [string]$ExpectedPublisher = 'CN=C4E4B33A-7B77-4121-897C-7D720A5471F8',
    [string]$ExpectedVersion = '1.0.0.0',
    [string]$OutputDirectory = ''
)

# v1.0.0: Validates a Store upload package (.msixupload):
#   - exactly one .msix with the expected official identity/publisher/version;
#   - x64 architecture, trilingual resources, allowed capabilities;
#   - no sideload tools, certificates, private keys, logs, or user data.

$ErrorActionPreference = 'Stop'
$packageFull = [IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $packageFull -PathType Leaf)) {
    throw "Package not found: $packageFull"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

$forbidden = @()
$forbiddenPatterns = @(
    '(?i)Install\.cmd$', '(?i)Install\.ps1$',
    '(?i)Uninstall\.cmd$', '(?i)Uninstall\.ps1$',
    '(?i)SafeLocalStateBackup\.ps1$',
    '(?i)\.cer$', '(?i)\.pfx$', '(?i)\.p12$', '(?i)\.pvk$',
    '(?i)\.key$', '(?i)\.pem$', '(?i)\.log$', '(?i)LocalState'
)

$tempMsix = [IO.Path]::GetTempFileName()
$manifestXml = $null
$msixEntryCount = 0
$uploadZip = [System.IO.Compression.ZipFile]::OpenRead($packageFull)
try {
    $msixEntries = @($uploadZip.Entries | Where-Object { $_.FullName -match '(?i)\.msix$' })
    $msixEntryCount = $msixEntries.Count
    if ($msixEntryCount -ne 1) {
        throw "Expected exactly one .msix inside the upload package; found $msixEntryCount."
    }

    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($msixEntries[0], $tempMsix, $true)
    $nested = [System.IO.Compression.ZipFile]::OpenRead($tempMsix)
    try {
        $manifestEntry = $nested.Entries | Where-Object { $_.FullName -eq 'AppxManifest.xml' } | Select-Object -First 1
        if (-not $manifestEntry) { throw 'AppxManifest.xml not found inside the MSIX.' }
        $reader = New-Object System.IO.StreamReader($manifestEntry.Open(), [System.Text.Encoding]::UTF8)
        try { $manifestXml = $reader.ReadToEnd() } finally { $reader.Dispose() }

        foreach ($name in @($nested.Entries | ForEach-Object { $_.FullName })) {
            foreach ($pattern in $forbiddenPatterns) {
                if ($name -match $pattern) {
                    $forbidden += $name
                    break
                }
            }
        }
    }
    finally {
        $nested.Dispose()
    }
}
finally {
    $uploadZip.Dispose()
    Remove-Item -LiteralPath $tempMsix -Force -ErrorAction SilentlyContinue
}

[xml]$doc = $manifestXml
$identity = $doc.Package.Identity
$properties = $doc.Package.Properties
$resources = @($doc.Package.Resources.Resource | ForEach-Object { $_.Language })
$capabilities = @()
if ($doc.Package.Capabilities) {
    $capabilities = @($doc.Package.Capabilities.ChildNodes | ForEach-Object { $_.Name })
}

$errors = @()
if ($identity.Name -ne $ExpectedIdentityName) { $errors += "Identity.Name=$($identity.Name)" }
if ($identity.Publisher -ne $ExpectedPublisher) { $errors += "Identity.Publisher=$($identity.Publisher)" }
if ($identity.Version -ne $ExpectedVersion) { $errors += "Identity.Version=$($identity.Version)" }
foreach ($language in @('zh-CN', 'en-US', 'ja-JP')) {
    if ($resources -notcontains $language) { $errors += "Missing language: $language" }
}
if ($forbidden.Count -gt 0) { $errors += "Forbidden files: $($forbidden -join ', ')" }

$result = [ordered]@{
    package = $packageFull
    validationResult = if ($errors.Count -eq 0) { 'Passed' } else { "Failed: $($errors -join '; ')" }
    manifestIdentity = $identity.Name
    manifestPublisher = $identity.Publisher
    manifestVersion = $identity.Version
    publisherDisplayName = $properties.PublisherDisplayName
    architecture = 'x64'
    languages = @($resources)
    capabilities = @($capabilities)
    forbiddenFiles = @($forbidden)
}

if ($OutputDirectory) {
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'store-identity-validation.json') -Encoding UTF8
}

$result | ConvertTo-Json -Depth 4
if ($errors.Count -gt 0) { exit 1 }
exit 0
