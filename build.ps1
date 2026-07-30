param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$RevitApiPath = $env:REVIT_API_PATH
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$revitProject = Join-Path $projectRoot "src\WorkspaceRecall.RevitAddin\WorkspaceRecall.RevitAddin.csproj"
$appProject = Join-Path $projectRoot "src\WorkspaceRecall.App\WorkspaceRecall.App.csproj"
$outputDirectory = Join-Path $projectRoot "dist\WorkspaceRecall-win-x64"
$revitOutput = Join-Path $projectRoot "src\WorkspaceRecall.RevitAddin\bin\$Configuration\net8.0-windows\WorkspaceRecall.RevitAddin.dll"
$packageVerifier = Join-Path $projectRoot "scripts\verify-release-package.ps1"

$resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot) +
    [IO.Path]::DirectorySeparatorChar
$resolvedOutputDirectory = [IO.Path]::GetFullPath($outputDirectory)
if (-not $resolvedOutputDirectory.StartsWith(
        $resolvedProjectRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to prepare a publish directory outside the project."
}

if (Test-Path -LiteralPath $resolvedOutputDirectory) {
    Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force
}

if ([string]::IsNullOrWhiteSpace($RevitApiPath)) {
    $registryRoots = @(
        "HKLM:\SOFTWARE\Autodesk\Revit\2026",
        "HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit\2026"
    )
    foreach ($registryRoot in $registryRoots) {
        $candidate = Get-ChildItem -Path $registryRoot -ErrorAction SilentlyContinue |
            ForEach-Object {
                (Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue).InstallationLocation
            } |
            Where-Object {
                $_ -and
                (Test-Path -LiteralPath (Join-Path $_ "RevitAPI.dll")) -and
                (Test-Path -LiteralPath (Join-Path $_ "RevitAPIUI.dll"))
            } |
            Select-Object -First 1
        if ($candidate) {
            $RevitApiPath = $candidate
            break
        }
    }
}

$revitAvailable =
    -not [string]::IsNullOrWhiteSpace($RevitApiPath) -and
    (Test-Path -LiteralPath (Join-Path $RevitApiPath "RevitAPI.dll")) -and
    (Test-Path -LiteralPath (Join-Path $RevitApiPath "RevitAPIUI.dll"))
if ($revitAvailable) {
    dotnet build $revitProject `
        --configuration $Configuration `
        --verbosity minimal `
        "-p:RevitApiPath=$RevitApiPath"
}

dotnet publish $appProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    --verbosity minimal `
    --output $outputDirectory

$revitBundleDirectory = Join-Path $outputDirectory "RevitAddin"
if ($revitAvailable) {
    New-Item -ItemType Directory -Path $revitBundleDirectory -Force | Out-Null
    Copy-Item -LiteralPath $revitOutput `
        -Destination (Join-Path $revitBundleDirectory "WorkspaceRecall.RevitAddin.OnDemand.dll") `
        -Force
    $legacyBundledHelper = Join-Path $revitBundleDirectory "WorkspaceRecall.RevitAddin.dll"
    if (Test-Path -LiteralPath $legacyBundledHelper) {
        try {
            Remove-Item -LiteralPath $legacyBundledHelper -Force
        }
        catch {
            Write-Warning "The legacy Revit helper is loaded. Restart Revit, then rebuild to remove it."
        }
    }
}
elseif (Test-Path -LiteralPath $revitBundleDirectory) {
    $resolvedPublishDirectory = [IO.Path]::GetFullPath($outputDirectory) +
        [IO.Path]::DirectorySeparatorChar
    $resolvedRevitBundleDirectory = [IO.Path]::GetFullPath($revitBundleDirectory)
    if (-not $resolvedRevitBundleDirectory.StartsWith(
            $resolvedPublishDirectory,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a Revit bundle outside the publish directory."
    }

    Remove-Item -LiteralPath $revitBundleDirectory -Recurse -Force
}

Get-ChildItem -LiteralPath $outputDirectory -Recurse -Filter *.pdb |
    Remove-Item -Force

& $packageVerifier -PackagePath $outputDirectory

Write-Output ""
Write-Output "Portable build ready:"
Write-Output (Join-Path $outputDirectory "WorkspaceRecall.exe")
if (-not $revitAvailable) {
    Write-Output "Optional Revit helper skipped because the Revit 2026 API was not found."
}
