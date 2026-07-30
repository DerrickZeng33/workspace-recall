[CmdletBinding()]
param(
    [string]$Repository = "DerrickZeng33/workspace-recall"
)

$ErrorActionPreference = "Stop"

if ($Repository -notmatch "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") {
    throw "Repository must use the owner/name format."
}

function Invoke-GitHubJson {
    param(
        [Parameter(Mandatory)]
        [string]$Endpoint
    )

    $raw = & gh api $Endpoint
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI request failed for: $Endpoint"
    }

    return $raw | ConvertFrom-Json
}

$repositoryData = Invoke-GitHubJson -Endpoint "repos/$Repository"
$views = Invoke-GitHubJson -Endpoint "repos/$Repository/traffic/views"
$clones = Invoke-GitHubJson -Endpoint "repos/$Repository/traffic/clones"
$referrers = @(
    Invoke-GitHubJson -Endpoint "repos/$Repository/traffic/popular/referrers")
$popularPaths = @(
    Invoke-GitHubJson -Endpoint "repos/$Repository/traffic/popular/paths")
$releases = @(
    Invoke-GitHubJson -Endpoint "repos/$Repository/releases?per_page=100")

$releaseSummary = @(
    $releases | ForEach-Object {
        [pscustomobject]@{
            tag = $_.tag_name
            publishedAt = $_.published_at
            assets = @(
                $_.assets | ForEach-Object {
                    [pscustomobject]@{
                        name = $_.name
                        downloads = $_.download_count
                    }
                })
        }
    })

$report = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    repository = $Repository
    stars = $repositoryData.stargazers_count
    forks = $repositoryData.forks_count
    subscribers = $repositoryData.subscribers_count
    openIssues = $repositoryData.open_issues_count
    views14Days = [ordered]@{
        total = $views.count
        unique = $views.uniques
        daily = @($views.views)
    }
    clones14Days = [ordered]@{
        total = $clones.count
        unique = $clones.uniques
        daily = @($clones.clones)
    }
    topReferrers = $referrers
    popularPaths = $popularPaths
    releases = $releaseSummary
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$metricsDirectory = [IO.Path]::GetFullPath(
    (Join-Path $projectRoot "artifacts\metrics"))
$expectedPrefix = $projectRoot + [IO.Path]::DirectorySeparatorChar
if (-not $metricsDirectory.StartsWith(
        $expectedPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write metrics outside the project."
}

New-Item -ItemType Directory -Path $metricsDirectory -Force | Out-Null
$timestamp = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ")
$outputPath = Join-Path $metricsDirectory "$timestamp.json"
$report | ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM

Write-Output "GitHub growth snapshot saved:"
Write-Output $outputPath
Write-Output ""
Write-Output "Stars: $($report.stars)"
Write-Output "Unique visitors (14 days): $($report.views14Days.unique)"
Write-Output "Unique cloners (14 days): $($report.clones14Days.unique)"
Write-Output "Release assets: $(($releaseSummary.assets | Measure-Object).Count)"
