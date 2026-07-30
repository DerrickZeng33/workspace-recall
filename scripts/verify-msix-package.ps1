[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [ValidatePattern("^[1-9][0-9]{0,4}\.[0-9]{1,5}\.[0-9]{1,5}\.0$")]
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"

$resolvedPackage = Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop
if (-not (Test-Path -LiteralPath $resolvedPackage.Path -PathType Leaf)) {
    throw "MSIX package path must be a file."
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$storeIdentityPath = Join-Path $projectRoot "packaging\StoreIdentity.json"
$storeIdentity = Get-Content -LiteralPath $storeIdentityPath -Raw |
    ConvertFrom-Json

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedPackage.Path)
try {
    $entries = @($archive.Entries)
    $entriesByName =
        [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
        $normalizedName = $entry.FullName.Replace("\", "/")
        if ($entriesByName.ContainsKey($normalizedName)) {
            throw "MSIX package contains duplicate entry: $normalizedName"
        }
        $entriesByName.Add($normalizedName, $entry)
    }

    $requiredEntries = @(
        "AppxManifest.xml",
        "WorkspaceRecall.exe",
        "WorkspaceRecall.dll",
        "WorkspaceRecall.deps.json",
        "WorkspaceRecall.runtimeconfig.json",
        "Assets/StoreLogo.png",
        "Assets/Square44x44Logo.png",
        "Assets/Square150x150Logo.png",
        "Assets/Wide310x150Logo.png",
        "Assets/Square310x310Logo.png",
        "Assets/SplashScreen.png"
    )
    foreach ($entryName in $requiredEntries) {
        if (-not $entriesByName.ContainsKey($entryName)) {
            throw "MSIX package is missing: $entryName"
        }
    }
    if ($entriesByName.ContainsKey("AppxSignature.p7x")) {
        throw "The Store-upload package must remain unsigned."
    }

    $deniedExtensions = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    @(
        ".dmp",
        ".key",
        ".log",
        ".p12",
        ".pdb",
        ".pem",
        ".pfx",
        ".suo",
        ".user"
    ) | ForEach-Object { [void]$deniedExtensions.Add($_) }
    foreach ($entry in $entries) {
        $extension = [IO.Path]::GetExtension($entry.FullName)
        if ($deniedExtensions.Contains($extension)) {
            throw "MSIX package contains a denied file: $($entry.FullName)"
        }
    }

    $manifestEntry = $entriesByName["AppxManifest.xml"]
    $manifestReader = [IO.StreamReader]::new(
        $manifestEntry.Open(),
        [Text.Encoding]::UTF8)
    try {
        [xml]$manifest = $manifestReader.ReadToEnd()
    }
    finally {
        $manifestReader.Dispose()
    }

    $namespaceManager = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaceManager.AddNamespace(
        "f",
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $namespaceManager.AddNamespace(
        "uap",
        "http://schemas.microsoft.com/appx/manifest/uap/windows10")
    $namespaceManager.AddNamespace(
        "rescap",
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities")

    $identity = $manifest.SelectSingleNode(
        "/f:Package/f:Identity",
        $namespaceManager)
    $properties = $manifest.SelectSingleNode(
        "/f:Package/f:Properties",
        $namespaceManager)
    $application = $manifest.SelectSingleNode(
        "/f:Package/f:Applications/f:Application",
        $namespaceManager)
    if (-not $identity -or -not $properties -or -not $application) {
        throw "MSIX manifest is missing required identity or application nodes."
    }

    if ($identity.Name -ne $storeIdentity.packageIdentityName) {
        throw "MSIX package identity name does not match Partner Center."
    }
    if ($identity.Publisher -cne $storeIdentity.packageIdentityPublisher) {
        throw "MSIX publisher does not match Partner Center."
    }
    if ($identity.Version -ne $ExpectedVersion) {
        throw "MSIX package version does not match the requested version."
    }
    if ($identity.ProcessorArchitecture -ne "x64") {
        throw "MSIX package architecture must be x64."
    }
    if ($properties.DisplayName -ne "Space Recorder") {
        throw "MSIX display name must be Space Recorder."
    }
    if ($properties.PublisherDisplayName -ne
        $storeIdentity.publisherDisplayName) {
        throw "MSIX publisher display name does not match Partner Center."
    }
    if ($application.Executable -ne "WorkspaceRecall.exe" -or
        $application.EntryPoint -ne "Windows.FullTrustApplication") {
        throw "MSIX application entry point is invalid."
    }

    $runFullTrust = $manifest.SelectSingleNode(
        "/f:Package/f:Capabilities/rescap:Capability[@Name='runFullTrust']",
        $namespaceManager)
    if (-not $runFullTrust) {
        throw "MSIX package must declare runFullTrust for the WPF app."
    }
    $unexpectedCapabilities = $manifest.SelectNodes(
        "/f:Package/f:Capabilities/*[not(@Name='runFullTrust')]",
        $namespaceManager)
    if ($unexpectedCapabilities.Count -gt 0) {
        throw "MSIX package declares an unexpected capability."
    }

    $assetDimensions = [ordered]@{
        "Assets/StoreLogo.png" = @(50, 50)
        "Assets/Square44x44Logo.png" = @(44, 44)
        "Assets/Square150x150Logo.png" = @(150, 150)
        "Assets/Wide310x150Logo.png" = @(310, 150)
        "Assets/Square310x310Logo.png" = @(310, 310)
        "Assets/SplashScreen.png" = @(620, 300)
    }
    Add-Type -AssemblyName System.Drawing
    foreach ($assetName in $assetDimensions.Keys) {
        $assetEntry = $entriesByName[$assetName]
        $assetStream = $assetEntry.Open()
        $image = [Drawing.Image]::FromStream($assetStream)
        try {
            $expected = $assetDimensions[$assetName]
            if ($image.Width -ne $expected[0] -or
                $image.Height -ne $expected[1]) {
                throw "MSIX asset has invalid dimensions: $assetName"
            }
        }
        finally {
            $image.Dispose()
            $assetStream.Dispose()
        }
    }

    $privateContentPatterns = [Collections.Generic.List[string]]::new()
    @(
        "C:\Users\",
        "@gmail.com",
        "@hotmail.com",
        "@outlook.com"
    ) | ForEach-Object { $privateContentPatterns.Add($_) }
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $privateContentPatterns.Add($env:USERPROFILE)
    }
    $privateContentPatterns.Add([IO.Path]::GetFullPath($projectRoot))

    $textExtensions = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    @(".config", ".json", ".txt", ".xml") |
        ForEach-Object { [void]$textExtensions.Add($_) }
    $singleByteEncoding = [Text.Encoding]::GetEncoding(28591)
    foreach ($entry in $entries) {
        $extension = [IO.Path]::GetExtension($entry.FullName)
        $isAppBinary = $entry.FullName -match
            "(^|[\\/])WorkspaceRecall\.(exe|dll)$"
        if (-not $isAppBinary -and -not $textExtensions.Contains($extension)) {
            continue
        }

        $entryStream = $entry.Open()
        $memoryStream = [IO.MemoryStream]::new()
        try {
            $entryStream.CopyTo($memoryStream)
            $bytes = $memoryStream.ToArray()
        }
        finally {
            $memoryStream.Dispose()
            $entryStream.Dispose()
        }

        $singleByteContent = $singleByteEncoding.GetString($bytes)
        $unicodeContent = [Text.Encoding]::Unicode.GetString($bytes)
        foreach ($pattern in $privateContentPatterns) {
            if ($singleByteContent.Contains(
                    $pattern,
                    [StringComparison]::OrdinalIgnoreCase) -or
                $unicodeContent.Contains(
                    $pattern,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Potential private data found in: $($entry.FullName)"
            }
        }
    }
}
finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $resolvedPackage.Path -Algorithm SHA256).Hash
Write-Output "MSIX package verification passed."
Write-Output "Unsigned Store package: yes"
Write-Output "SHA256: $hash"
