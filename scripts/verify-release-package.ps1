[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PackagePath,

    [switch]$RequireSignature
)

$ErrorActionPreference = "Stop"

$resolvedPackage = Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop
if (-not (Test-Path -LiteralPath $resolvedPackage.Path -PathType Container)) {
    throw "Package path must be a directory."
}

$packageRoot = [IO.Path]::GetFullPath($resolvedPackage.Path)
$requiredFiles = @(
    "WorkspaceRecall.exe",
    "WorkspaceRecall.dll",
    "WorkspaceRecall.deps.json",
    "WorkspaceRecall.runtimeconfig.json"
)

foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $packageRoot $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required package file is missing: $requiredFile"
    }
    if ((Get-Item -LiteralPath $requiredPath).Length -eq 0) {
        throw "Required package file is empty: $requiredFile"
    }
}

$deniedNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
@(
    ".env",
    "auth.json",
    "cookies.txt",
    "default-layout.json",
    "revit-active.json",
    "revit-request.json"
) | ForEach-Object { [void]$deniedNames.Add($_) }

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

$entries = Get-ChildItem -LiteralPath $packageRoot -Recurse -Force
$violations = [Collections.Generic.List[string]]::new()

foreach ($entry in $entries) {
    $relativePath = [IO.Path]::GetRelativePath($packageRoot, $entry.FullName)
    if ($relativePath.StartsWith("..", [StringComparison]::Ordinal)) {
        $violations.Add("Entry resolves outside the package: $relativePath")
    }
    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        $violations.Add("Package contains a link or reparse point: $relativePath")
    }
    if (-not $entry.PSIsContainer) {
        if ($deniedNames.Contains($entry.Name)) {
            $violations.Add("Denied file name: $relativePath")
        }
        if ($deniedExtensions.Contains($entry.Extension)) {
            $violations.Add("Denied file extension: $relativePath")
        }
    }
}

$privateContentPatterns = @(
    "C:\Users\",
    "Dropbox\\Apps",
    "Third Party Programs",
    "@gmail.com",
    "@hotmail.com",
    "@outlook.com"
)
$textExtensions = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
@(".config", ".json", ".txt", ".xml") |
    ForEach-Object { [void]$textExtensions.Add($_) }

foreach ($file in $entries.Where({ -not $_.PSIsContainer })) {
    if (-not $textExtensions.Contains($file.Extension)) {
        continue
    }

    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($pattern in $privateContentPatterns) {
        if ($content.Contains($pattern, [StringComparison]::OrdinalIgnoreCase)) {
            $relativePath = [IO.Path]::GetRelativePath(
                $packageRoot,
                $file.FullName)
            $violations.Add(
                "Potential private machine or contact data in: $relativePath")
            break
        }
    }
}

if ($violations.Count -gt 0) {
    throw "Release package verification failed:`n- $($violations -join "`n- ")"
}

$executablePath = Join-Path $packageRoot "WorkspaceRecall.exe"
if ($RequireSignature) {
    $signature = Get-AuthenticodeSignature -LiteralPath $executablePath
    $validStatus = [System.Management.Automation.SignatureStatus]::Valid
    if ($signature.Status -ne $validStatus) {
        throw "WorkspaceRecall.exe does not have a valid trusted signature."
    }
}

$files = Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
    Sort-Object FullName
$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum

Write-Output "Release package verification passed."
Write-Output "Files: $($files.Count)"
Write-Output "Bytes: $totalBytes"
if (-not $RequireSignature) {
    Write-Output "Signature gate was not requested; this package is not approved for public release."
}

$files | ForEach-Object {
    $relativePath = [IO.Path]::GetRelativePath($packageRoot, $_.FullName)
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    Write-Output "$hash  $relativePath"
}
