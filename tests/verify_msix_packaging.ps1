$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$identityPath = Join-Path $projectRoot "packaging\StoreIdentity.json"
$templatePath = Join-Path $projectRoot "packaging\AppxManifest.xml.template"
$packageVerifierPath = Join-Path $projectRoot "scripts\verify-msix-package.ps1"
$mainWindowPath = Join-Path $projectRoot "src\WorkspaceRecall.App\MainWindow.xaml"
$appManifestPath = Join-Path $projectRoot "src\WorkspaceRecall.App\app.manifest"

$identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$requiredProperties = @(
    "packageIdentityName",
    "packageIdentityPublisher",
    "publisherDisplayName",
    "packageFamilyName",
    "storeId"
)
foreach ($propertyName in $requiredProperties) {
    if ([string]::IsNullOrWhiteSpace($identity.$propertyName)) {
        throw "Store identity is missing: $propertyName"
    }
}
$actualProperties = @($identity.PSObject.Properties.Name | Sort-Object)
$expectedProperties = @($requiredProperties | Sort-Object)
if ((Compare-Object $actualProperties $expectedProperties).Count -gt 0) {
    throw "Store identity contains an unexpected property."
}
$identityValues = $identity.PSObject.Properties.Value -join "`n"
if ($identityValues -match "(?i)[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}" -or
    $identityValues -match "(?i)([A-Z]:\\|\\\\)" -or
    $identityValues -match "(?i)https?://") {
    throw "Store identity contains contact details, a path, or a URL."
}
if ($identity.packageIdentityName -notmatch "^[A-Za-z0-9.-]{3,50}$") {
    throw "Package/Identity/Name is invalid."
}
if ($identity.packageIdentityPublisher -notmatch
    "(?i)^CN=[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$") {
    throw "Package/Identity/Publisher is invalid."
}
if ($identity.storeId -notmatch "^[A-Z0-9]{12}$") {
    throw "Store ID is invalid."
}
if ($identity.packageFamilyName -notmatch
    "^[A-Za-z0-9.-]+_[a-hj-km-np-tv-z0-9]{13}$") {
    throw "Package Family Name is invalid."
}
if (-not $identity.packageFamilyName.StartsWith(
        "$($identity.packageIdentityName)_",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package Family Name does not match Package/Identity/Name."
}

$template = Get-Content -LiteralPath $templatePath -Raw
$requiredTokens = @(
    "__PACKAGE_IDENTITY_NAME__",
    "__PACKAGE_IDENTITY_PUBLISHER__",
    "__PUBLISHER_DISPLAY_NAME__",
    "__PACKAGE_VERSION__"
)
foreach ($token in $requiredTokens) {
    if ($template.IndexOf($token, [StringComparison]::Ordinal) -lt 0) {
        throw "MSIX manifest template is missing token: $token"
    }
}
if ($template.IndexOf(
        '<rescap:Capability Name="runFullTrust" />',
        [StringComparison]::Ordinal) -lt 0) {
    throw "MSIX manifest template must declare runFullTrust."
}
$capabilities = [regex]::Matches(
    $template,
    "<(?:[A-Za-z0-9]+:)?Capability\b",
    [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if ($capabilities.Count -ne 1) {
    throw "MSIX manifest template must declare only one capability."
}
if ($template.IndexOf("unvirtualizedResources",
        [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
    $template.IndexOf("internetClient",
        [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw "MSIX manifest template declares an unsafe capability."
}

$mainWindow = Get-Content -LiteralPath $mainWindowPath -Raw
if ($mainWindow.IndexOf(
        'Title="Space Recorder"',
        [StringComparison]::Ordinal) -lt 0 -or
    $mainWindow.IndexOf(
        'Text="Space Recorder"',
        [StringComparison]::Ordinal) -lt 0) {
    throw "Visible application branding is not Space Recorder."
}

$appManifest = Get-Content -LiteralPath $appManifestPath -Raw
if ($appManifest.IndexOf(
        '<requestedExecutionLevel level="asInvoker" uiAccess="false" />',
        [StringComparison]::Ordinal) -lt 0) {
    throw "The application must run without administrator elevation."
}

function Assert-ForbiddenPackageEntry {
    param(
        [Parameter(Mandatory)][string]$EntryName,
        [Parameter(Mandatory)][string]$ExpectedMessage
    )

    $temporaryRoot = Join-Path (
        [IO.Path]::GetTempPath()) (
        "workspace-recall-msix-policy-$([Guid]::NewGuid().ToString('N'))")
    $layoutPath = Join-Path $temporaryRoot "layout"
    $packagePath = Join-Path $temporaryRoot "test.msix"
    try {
        $requiredEntries = @(
            "AppxManifest.xml",
            "WorkspaceRecall.exe",
            "WorkspaceRecall.dll",
            "WorkspaceRecall.deps.json",
            "WorkspaceRecall.runtimeconfig.json",
            "Assets\StoreLogo.png",
            "Assets\Square44x44Logo.png",
            "Assets\Square150x150Logo.png",
            "Assets\Wide310x150Logo.png",
            "Assets\Square310x310Logo.png",
            "Assets\SplashScreen.png",
            $EntryName
        )
        foreach ($entry in $requiredEntries) {
            $entryPath = Join-Path $layoutPath $entry
            [IO.Directory]::CreateDirectory(
                [IO.Path]::GetDirectoryName($entryPath)) | Out-Null
            [IO.File]::WriteAllBytes($entryPath, [byte[]]@(0))
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory(
            $layoutPath,
            $packagePath)

        $rejection = $null
        try {
            & $packageVerifierPath `
                -PackagePath $packagePath `
                -ExpectedVersion "1.0.0.0"
        }
        catch {
            $rejection = $_.Exception.Message
        }
        if ($rejection -notmatch [regex]::Escape($ExpectedMessage)) {
            throw "Unexpected package-policy result for ${EntryName}: $rejection"
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) {
            [IO.Directory]::Delete($temporaryRoot, $true)
        }
    }
}

Assert-ForbiddenPackageEntry `
    -EntryName "default-layout.json" `
    -ExpectedMessage "private layout data"
Assert-ForbiddenPackageEntry `
    -EntryName "previews\example.png" `
    -ExpectedMessage "excluded helper or preview"
Assert-ForbiddenPackageEntry `
    -EntryName "RevitAddin\WorkspaceRecall.RevitAddin.OnDemand.dll" `
    -ExpectedMessage "excluded helper or preview"
Assert-ForbiddenPackageEntry `
    -EntryName "certificate.cer" `
    -ExpectedMessage "denied file"
Assert-ForbiddenPackageEntry `
    -EntryName "SpaceRecorder.snk" `
    -ExpectedMessage "denied file"

Write-Output "MSIX source metadata verification passed."
