[CmdletBinding()]
param(
    [ValidatePattern("^[1-9][0-9]{0,4}\.[0-9]{1,5}\.[0-9]{1,5}\.0$")]
    [string]$PackageVersion = "1.0.0.0"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $projectRoot "build.ps1"
$manifestTemplate = Join-Path $projectRoot "packaging\AppxManifest.xml.template"
$storeIdentityPath = Join-Path $projectRoot "packaging\StoreIdentity.json"
$sourceIcon = Join-Path $projectRoot "src\WorkspaceRecall.App\Assets\workspace-recall-icon.png"
$packageVerifier = Join-Path $PSScriptRoot "verify-msix-package.ps1"
$outputRoot = Join-Path $projectRoot "dist\SpaceRecorder-msix"
$layoutPath = Join-Path $outputRoot "layout"
$assetPath = Join-Path $layoutPath "Assets"
$uploadContentsPath = Join-Path $outputRoot "upload"
$packageFileName = "SpaceRecorder_$($PackageVersion)_x64.msix"
$packagePath = Join-Path $outputRoot $packageFileName
$uploadPath = Join-Path $outputRoot "SpaceRecorder_$($PackageVersion)_x64.msixupload"

$versionParts = $PackageVersion.Split(".") | ForEach-Object { [int]$_ }
if ($versionParts.Where({ $_ -gt 65535 }).Count -gt 0) {
    throw "Every package version component must be at most 65535."
}

$resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot) +
    [IO.Path]::DirectorySeparatorChar
$resolvedOutputRoot = [IO.Path]::GetFullPath($outputRoot)
if (-not $resolvedOutputRoot.StartsWith(
        $resolvedProjectRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to prepare an MSIX directory outside the project."
}

if (Test-Path -LiteralPath $resolvedOutputRoot) {
    Remove-Item -LiteralPath $resolvedOutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $layoutPath -Force | Out-Null

& $buildScript `
    -Configuration Release `
    -OutputDirectory $layoutPath `
    -SelfContained `
    -SkipRevit
if ($LASTEXITCODE -ne 0) {
    throw "The application publish step failed."
}

$storeIdentity = Get-Content -LiteralPath $storeIdentityPath -Raw |
    ConvertFrom-Json
$requiredIdentityValues = @(
    "packageIdentityName",
    "packageIdentityPublisher",
    "publisherDisplayName",
    "packageFamilyName",
    "storeId"
)
foreach ($propertyName in $requiredIdentityValues) {
    if ([string]::IsNullOrWhiteSpace($storeIdentity.$propertyName)) {
        throw "Store identity is missing: $propertyName"
    }
}
if ($storeIdentity.storeId -notmatch "^[A-Z0-9]{12}$") {
    throw "Store identity contains an invalid Store ID."
}
if (-not $storeIdentity.packageFamilyName.StartsWith(
        "$($storeIdentity.packageIdentityName)_",
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package Family Name does not match Package/Identity/Name."
}

function ConvertTo-XmlText {
    param([Parameter(Mandatory)][string]$Value)

    return [Security.SecurityElement]::Escape($Value)
}

$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifest = $manifest.Replace(
    "__PACKAGE_IDENTITY_NAME__",
    (ConvertTo-XmlText $storeIdentity.packageIdentityName))
$manifest = $manifest.Replace(
    "__PACKAGE_IDENTITY_PUBLISHER__",
    (ConvertTo-XmlText $storeIdentity.packageIdentityPublisher))
$manifest = $manifest.Replace(
    "__PUBLISHER_DISPLAY_NAME__",
    (ConvertTo-XmlText $storeIdentity.publisherDisplayName))
$manifest = $manifest.Replace("__PACKAGE_VERSION__", $PackageVersion)
if ($manifest.Contains("__", [StringComparison]::Ordinal)) {
    throw "The generated manifest still contains an unresolved token."
}

$manifestPath = Join-Path $layoutPath "AppxManifest.xml"
[IO.File]::WriteAllText(
    $manifestPath,
    $manifest,
    [Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.Drawing

function New-PackageImage {
    param(
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][int]$Width,
        [Parameter(Mandatory)][int]$Height,
        [double]$Scale = 0.68
    )

    $source = [Drawing.Image]::FromFile($sourceIcon)
    $bitmap = [Drawing.Bitmap]::new(
        $Width,
        $Height,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml("#0B0F11"))
        $graphics.CompositingQuality =
            [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode =
            [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode =
            [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode =
            [Drawing.Drawing2D.SmoothingMode]::HighQuality

        $targetSize = [Math]::Floor([Math]::Min($Width, $Height) * $Scale)
        $ratio = [Math]::Min(
            $targetSize / $source.Width,
            $targetSize / $source.Height)
        $drawWidth = [Math]::Max(1, [int][Math]::Round($source.Width * $ratio))
        $drawHeight = [Math]::Max(1, [int][Math]::Round($source.Height * $ratio))
        $x = [int](($Width - $drawWidth) / 2)
        $y = [int](($Height - $drawHeight) / 2)
        $graphics.DrawImage($source, $x, $y, $drawWidth, $drawHeight)
        $bitmap.Save($Destination, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
        $source.Dispose()
    }
}

New-Item -ItemType Directory -Path $assetPath -Force | Out-Null
New-PackageImage `
    -Destination (Join-Path $assetPath "StoreLogo.png") `
    -Width 50 -Height 50 -Scale 0.82
New-PackageImage `
    -Destination (Join-Path $assetPath "Square44x44Logo.png") `
    -Width 44 -Height 44 -Scale 0.82
New-PackageImage `
    -Destination (Join-Path $assetPath "Square150x150Logo.png") `
    -Width 150 -Height 150
New-PackageImage `
    -Destination (Join-Path $assetPath "Wide310x150Logo.png") `
    -Width 310 -Height 150
New-PackageImage `
    -Destination (Join-Path $assetPath "Square310x310Logo.png") `
    -Width 310 -Height 310
New-PackageImage `
    -Destination (Join-Path $assetPath "SplashScreen.png") `
    -Width 620 -Height 300

function Find-WindowsSdkTool {
    param([Parameter(Mandatory)][string]$Name)

    $sdkBin = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $sdkBin -PathType Container)) {
        throw "Windows SDK tools were not found."
    }

    $candidates = Get-ChildItem -LiteralPath $sdkBin -Directory |
        ForEach-Object {
            $sdkVersion = $null
            if (-not [Version]::TryParse($_.Name, [ref]$sdkVersion)) {
                return
            }

            $toolPath = Join-Path $_.FullName "x64\$Name"
            if (Test-Path -LiteralPath $toolPath -PathType Leaf) {
                [PSCustomObject]@{
                    Version = $sdkVersion
                    Path = $toolPath
                }
            }
        } |
        Sort-Object Version -Descending

    $tool = $candidates | Select-Object -First 1
    if (-not $tool) {
        throw "$Name was not found in the Windows SDK."
    }
    return $tool.Path
}

$makeAppx = Find-WindowsSdkTool "MakeAppx.exe"
& $makeAppx pack /o /h SHA256 /d $layoutPath /p $packagePath
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed to create the MSIX package."
}

& $packageVerifier `
    -PackagePath $packagePath `
    -ExpectedVersion $PackageVersion

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $uploadContentsPath -Force | Out-Null
Copy-Item -LiteralPath $packagePath `
    -Destination (Join-Path $uploadContentsPath $packageFileName)
[IO.Compression.ZipFile]::CreateFromDirectory(
    $uploadContentsPath,
    $uploadPath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$uploadArchive = [IO.Compression.ZipFile]::OpenRead($uploadPath)
try {
    if ($uploadArchive.Entries.Count -ne 1 -or
        $uploadArchive.Entries[0].FullName -ne $packageFileName) {
        throw "The MSIX upload archive contains unexpected entries."
    }
}
finally {
    $uploadArchive.Dispose()
}

$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
$uploadHash = (Get-FileHash -LiteralPath $uploadPath -Algorithm SHA256).Hash

Write-Output ""
Write-Output "Unsigned Microsoft Store package ready for validation:"
Write-Output $uploadPath
Write-Output "MSIX SHA256: $packageHash"
Write-Output "Upload SHA256: $uploadHash"
Write-Output "The package was not installed, signed, uploaded, or approved for release."
