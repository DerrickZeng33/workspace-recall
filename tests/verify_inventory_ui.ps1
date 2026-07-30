param(
    [string]$AppPath = (Join-Path $PSScriptRoot '..\dist\WorkspaceRecall-win-x64\WorkspaceRecall.exe'),
    [string]$LayoutPath = (Join-Path $env:LOCALAPPDATA 'WorkspaceRecall\default-layout.json')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Find-ElementByAutomationId {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Wait-ForElement {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId,
        [int]$TimeoutMilliseconds = 8000
    )

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        $element = Find-ElementByAutomationId -Root $Root -AutomationId $AutomationId
        if ($null -ne $element) {
            return $element
        }

        Start-Sleep -Milliseconds 100
    }

    throw "Timed out waiting for UI element '$AutomationId'."
}

function Wait-ForSelectedMonitorTile {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId,
        [int]$TimeoutMilliseconds = 3000
    )

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        $tile = Find-ElementByAutomationId -Root $Root -AutomationId $AutomationId
        if ($null -ne $tile -and $tile.Current.ItemStatus -eq 'Selected') {
            return
        }

        Start-Sleep -Milliseconds 100
    }

    throw "Selecting the inventory row did not highlight monitor tile '$AutomationId'."
}

function Get-ExpectedStatus {
    param($Window)

    if ([bool]$Window.excluded) {
        return 'Excluded'
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Window.filePath) -and
        (Test-Path -LiteralPath ([string]$Window.filePath))) {
        return 'File identified'
    }

    if ($Window.detection -eq 'ProgramOnly' -and
        -not [string]::IsNullOrWhiteSpace([string]$Window.executablePath) -and
        (Test-Path -LiteralPath ([string]$Window.executablePath) -PathType Leaf)) {
        return 'Program only'
    }

    return 'Needs review'
}

function Get-ExpectedCapturedItem {
    param($Window)

    if (-not [string]::IsNullOrWhiteSpace([string]$Window.filePath)) {
        return [System.IO.Path]::GetFileName(
            ([string]$Window.filePath).TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar))
    }

    $title = [string]$Window.windowTitle
    if ([string]::IsNullOrWhiteSpace($title)) {
        return 'Unknown file'
    }

    $candidates = @(
        foreach ($separator in @(' - ', ' — ', ' | ')) {
            ($title -split [regex]::Escape($separator))[0].Trim()
        }
    )
    return ($candidates | Sort-Object Length | Select-Object -First 1)
}

function Get-ExpectedApplicationName {
    param($Window)

    $applicationName = [string]$Window.applicationName
    $processName = [string]$Window.processName
    if (-not [string]::IsNullOrWhiteSpace($applicationName) -and
        $applicationName -ne $processName) {
        return $applicationName
    }

    $internalName = if (-not [string]::IsNullOrWhiteSpace($applicationName)) {
        $applicationName
    }
    else {
        $processName
    }
    if (-not [string]::IsNullOrWhiteSpace($internalName)) {
        return (($internalName -replace '(?i)(\.WinUI|\.exe)$', '') `
                -replace '[._-]', ' ' `
                -creplace '(?<=[a-z0-9])(?=[A-Z])', ' ').Trim()
    }

    $title = [string]$Window.windowTitle
    if ([string]::IsNullOrWhiteSpace($title)) {
        return 'Unknown program'
    }

    foreach ($separator in @(' - ', ' — ', ' | ')) {
        $segments = @($title -split [regex]::Escape($separator) |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ })
        if ($segments.Count -gt 1) {
            return $segments[-1]
        }
    }

    return $title
}

if (-not (Test-Path -LiteralPath $AppPath -PathType Leaf)) {
    throw "Published app not found: $AppPath"
}

if (-not (Test-Path -LiteralPath $LayoutPath -PathType Leaf)) {
    throw "Saved layout not found: $LayoutPath"
}

$layout = Get-Content -LiteralPath $LayoutPath -Raw | ConvertFrom-Json
$expectedWindows = @($layout.windows)
if ($expectedWindows.Count -eq 0) {
    throw 'The saved layout contains no captured windows to verify.'
}

$process = Start-Process -FilePath $AppPath -PassThru
try {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($process.MainWindowHandle -eq [IntPtr]::Zero -and
           $timer.ElapsedMilliseconds -lt 10000) {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    }

    if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
        throw 'Workspace Recall did not create a main window.'
    }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle(
        $process.MainWindowHandle)
    Wait-ForElement -Root $root -AutomationId 'CapturedWindowInventory' | Out-Null

    foreach ($window in $expectedWindows) {
        $rowId = "CapturedWindowRow_$($window.id)"
        $tileId = "MonitorWindowTile_$($window.id)"
        $row = Wait-ForElement -Root $root -AutomationId $rowId
        Wait-ForElement -Root $root -AutomationId $tileId | Out-Null
        $expectedApplicationName = Get-ExpectedApplicationName -Window $window
        $expectedCapturedItem = Get-ExpectedCapturedItem -Window $window
        $expectedStatus = Get-ExpectedStatus -Window $window
        $expectedRowName =
            "Captured window: $expectedApplicationName, $expectedCapturedItem, $expectedStatus, $($window.placementLabel)"
        if ($row.Current.Name -ne $expectedRowName) {
            throw "Row '$rowId' exposed '$($row.Current.Name)' instead of '$expectedRowName'."
        }

        $needsReview =
            $expectedStatus -eq 'Needs review'
        if ($needsReview) {
            Wait-ForElement -Root $root -AutomationId "ChooseFile_$($window.id)" | Out-Null
            Wait-ForElement -Root $root -AutomationId "ProgramOnly_$($window.id)" | Out-Null
        }

        $invokePattern = $row.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern)
        $invokePattern.Invoke()
        Wait-ForSelectedMonitorTile -Root $root -AutomationId $tileId
    }

    $rowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $buttons = $root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $rowCondition)
    $actualRowCount = @(
        $buttons |
            Where-Object { $_.Current.AutomationId -like 'CapturedWindowRow_*' }
    ).Count
    if ($actualRowCount -ne $expectedWindows.Count) {
        throw "Expected $($expectedWindows.Count) inventory rows, found $actualRowCount."
    }

    Write-Output "PASS  Inventory renders all $actualRowCount captured windows."
    Write-Output 'PASS  Every row exposes the expected program, item, status, and placement.'
    Write-Output 'PASS  Every row selects and highlights its monitor tile.'
    Write-Output 'PASS  Every needs-review row exposes direct file and program-only actions.'
}
finally {
    if (-not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) {
            Stop-Process -Id $process.Id
        }
    }
}
