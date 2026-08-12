param(
    [Parameter(Mandatory = $true)]
    [string] $Executable,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

function Find-NamedElement {
    param(
        [System.Windows.Automation.AutomationElement] $Root,
        [string] $Name
    )
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Select-NamedItem {
    param(
        [System.Windows.Automation.AutomationElement] $Combo,
        [string] $Name
    )
    $expand = $null
    if (-not $Combo.TryGetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
        [ref] $expand)) {
        throw "The '$($Combo.Current.Name)' control did not expose ExpandCollapsePattern."
    }
    $expand.Expand()
    Start-Sleep -Milliseconds 300
    $item = $null
    $selection = $null
    $allItems = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($candidate in $allItems) {
        try {
            if ($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
                $candidate.Current.Name.StartsWith($Name, [System.StringComparison]::OrdinalIgnoreCase)) {
                $candidateSelection = $null
                if ($candidate.TryGetCurrentPattern(
                    [System.Windows.Automation.SelectionItemPattern]::Pattern,
                    [ref] $candidateSelection)) {
                    $item = $candidate
                    $selection = $candidateSelection
                    break
                }
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
    }
    if ($null -eq $item) {
        $available = @()
        foreach ($candidate in $allItems) {
            try {
                if (-not [string]::IsNullOrWhiteSpace($candidate.Current.Name)) {
                    $available += "$($candidate.Current.ControlType.ProgrammaticName):$($candidate.Current.Name)"
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
        }
        throw "The combo item '$Name' was not exposed. Available named elements: $($available -join ' | ')"
    }
    if ($null -eq $selection) {
        throw "The combo item '$Name' did not expose SelectionItemPattern."
    }
    $selection.Select()
    Start-Sleep -Milliseconds 650
}

function Get-OverlayWindow {
    param([int] $ProcessId)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($window in $windows) {
        try {
            if ($window.Current.ProcessId -eq $ProcessId -and $window.Current.Name -eq 'Live subtitle overlay') {
                return $window
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
    }
    return $null
}

function Convert-Bounds {
    param([System.Windows.Rect] $Rectangle)
    return [pscustomobject]@{
        Left = [math]::Round($Rectangle.Left, 1)
        Top = [math]::Round($Rectangle.Top, 1)
        Right = [math]::Round($Rectangle.Right, 1)
        Bottom = [math]::Round($Rectangle.Bottom, 1)
        Width = [math]::Round($Rectangle.Width, 1)
        Height = [math]::Round($Rectangle.Height, 1)
    }
}

$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$process = Start-Process -FilePath $executablePath -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'LiveSubtitle did not create its control center.' }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $appearance = Find-NamedElement -Root $root -Name 'Subtitle appearance'
    $selection = $null
    if ($null -eq $appearance -or -not $appearance.TryGetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern,
        [ref] $selection)) {
        throw 'The Subtitle appearance navigation item was unavailable.'
    }
    $selection.Select()
    Start-Sleep -Milliseconds 500

    $monitorCombo = Find-NamedElement -Root $root -Name 'Overlay monitor'
    $anchorCombo = Find-NamedElement -Root $root -Name 'Overlay snap position'
    if ($null -eq $monitorCombo -or $null -eq $anchorCombo) { throw 'Overlay placement controls were unavailable.' }

    $screens = [System.Windows.Forms.Screen]::AllScreens
    if ($screens.Count -lt 2) { throw 'This audit requires at least two active monitors.' }
    $secondaryIndex = 0..($screens.Count - 1) | Where-Object { -not $screens[$_].Primary } | Select-Object -First 1
    $primaryIndex = 0..($screens.Count - 1) | Where-Object { $screens[$_].Primary } | Select-Object -First 1
    $secondaryName = "Display $($secondaryIndex + 1)"
    $primaryName = "Display $($primaryIndex + 1)"

    Select-NamedItem -Combo $monitorCombo -Name $secondaryName
    Select-NamedItem -Combo $anchorCombo -Name 'Bottom center'
    $overlay = Get-OverlayWindow -ProcessId $process.Id
    if ($null -eq $overlay) { throw 'The desktop overlay window was unavailable.' }
    $secondaryOverlay = Convert-Bounds $overlay.Current.BoundingRectangle
    $secondaryWork = $screens[$secondaryIndex].WorkingArea
    $secondaryContained = $secondaryOverlay.Left -ge $secondaryWork.Left - 2 -and
        $secondaryOverlay.Right -le $secondaryWork.Right + 2 -and
        $secondaryOverlay.Top -ge $secondaryWork.Top - 2 -and
        $secondaryOverlay.Bottom -le $secondaryWork.Bottom + 2

    Select-NamedItem -Combo $monitorCombo -Name $primaryName
    Select-NamedItem -Combo $anchorCombo -Name 'Top center'
    $topOverlay = Convert-Bounds $overlay.Current.BoundingRectangle
    $primaryWork = $screens[$primaryIndex].WorkingArea
    $topAnchored = $topOverlay.Top -ge $primaryWork.Top - 2 -and $topOverlay.Top -le $primaryWork.Top + 100

    Select-NamedItem -Combo $anchorCombo -Name 'Bottom center'
    $bottomOverlay = Convert-Bounds $overlay.Current.BoundingRectangle
    $bottomAnchored = $bottomOverlay.Bottom -le $primaryWork.Bottom + 2 -and
        $bottomOverlay.Bottom -ge $primaryWork.Bottom - 100

    $result = [pscustomobject]@{
        Success = $secondaryContained -and $topAnchored -and $bottomAnchored
        MonitorCount = $screens.Count
        SecondaryMonitor = $screens[$secondaryIndex].DeviceName
        SecondaryBounds = $secondaryWork.ToString()
        SecondaryOverlayBounds = $secondaryOverlay
        SecondaryContained = $secondaryContained
        PrimaryMonitor = $screens[$primaryIndex].DeviceName
        TopOverlayBounds = $topOverlay
        TopAnchorPassed = $topAnchored
        BottomOverlayBounds = $bottomOverlay
        BottomAnchorPassed = $bottomAnchored
        AuditedAtUtc = [DateTimeOffset]::UtcNow
    }
    $fullResultPath = [System.IO.Path]::GetFullPath($ResultPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullResultPath)) | Out-Null
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fullResultPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 5
    if (-not $result.Success) { exit 1 }
}
finally {
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) {
            $process.Kill()
            $process.WaitForExit()
        }
    }
}
