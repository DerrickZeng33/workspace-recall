$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$identityPath = Join-Path $projectRoot "packaging\StoreIdentity.json"
$templatePath = Join-Path $projectRoot "packaging\AppxManifest.xml.template"
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
    if (-not $template.Contains($token, [StringComparison]::Ordinal)) {
        throw "MSIX manifest template is missing token: $token"
    }
}
if (-not $template.Contains(
        '<rescap:Capability Name="runFullTrust" />',
        [StringComparison]::Ordinal)) {
    throw "MSIX manifest template must declare runFullTrust."
}
$capabilities = [regex]::Matches(
    $template,
    "<(?:[A-Za-z0-9]+:)?Capability\b",
    [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if ($capabilities.Count -ne 1) {
    throw "MSIX manifest template must declare only one capability."
}
if ($template.Contains("unvirtualizedResources",
        [StringComparison]::OrdinalIgnoreCase) -or
    $template.Contains("internetClient",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "MSIX manifest template declares an unsafe capability."
}

$mainWindow = Get-Content -LiteralPath $mainWindowPath -Raw
if (-not $mainWindow.Contains(
        'Title="Space Recorder"',
        [StringComparison]::Ordinal) -or
    -not $mainWindow.Contains(
        'Text="Space Recorder"',
        [StringComparison]::Ordinal)) {
    throw "Visible application branding is not Space Recorder."
}

$appManifest = Get-Content -LiteralPath $appManifestPath -Raw
if (-not $appManifest.Contains(
        '<requestedExecutionLevel level="asInvoker" uiAccess="false" />',
        [StringComparison]::Ordinal)) {
    throw "The application must run without administrator elevation."
}

Write-Output "MSIX source metadata verification passed."
