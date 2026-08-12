param(
    [Parameter(Mandatory = $true)]
    [string] $Executable,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$process = Start-Process -FilePath $executablePath -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq 0) {
        throw 'LiveSubtitle did not create a main window within 20 seconds.'
    }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $trueCondition = [System.Windows.Automation.Condition]::TrueCondition
    $pages = @('Live', 'Audio sources', 'Subtitle appearance', 'Recognition', 'Hotkeys', 'Saved sessions', 'General', 'Diagnostics')
    $pageResults = @()

    foreach ($page in $pages) {
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $page)
        $navigationItem = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $nameCondition)
        if ($null -eq $navigationItem) {
            throw "Navigation item '$page' was not exposed to UI Automation."
        }

        $pattern = $null
        if (-not $navigationItem.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [ref] $pattern)) {
            throw "Navigation item '$page' did not expose SelectionItemPattern."
        }
        $pattern.Select()
        Start-Sleep -Milliseconds 250

        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $trueCondition)
        $visibleFocusable = @()
        $unnamedFocusable = @()
        $hiddenFocusable = @()
        foreach ($element in $all) {
            try {
                if (-not $element.Current.IsKeyboardFocusable) { continue }
                $name = $element.Current.Name
                if ($element.Current.IsOffscreen) {
                    $hiddenFocusable += $name
                }
                else {
                    $visibleFocusable += $name
                    if ([string]::IsNullOrWhiteSpace($name)) {
                        $unnamedFocusable += $element.Current.AutomationId
                    }
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
        }

        $pageResults += [pscustomobject]@{
            Page = $page
            VisibleFocusableCount = $visibleFocusable.Count
            NamedVisibleFocusableCount = @($visibleFocusable | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
            UnnamedVisibleFocusable = @($unnamedFocusable)
            HiddenFocusableCount = $hiddenFocusable.Count
        }
    }

    $allElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $trueCondition)
    $result = [pscustomobject]@{
        Success = @($pageResults | Where-Object { $_.UnnamedVisibleFocusable.Count -gt 0 }).Count -eq 0
        ProcessId = $process.Id
        WindowName = $root.Current.Name
        TotalAutomationElements = $allElements.Count
        NavigationPages = $pages
        Pages = $pageResults
        AuditedAtUtc = [DateTimeOffset]::UtcNow
    }

    $fullResultPath = [System.IO.Path]::GetFullPath($ResultPath)
    $resultDirectory = [System.IO.Path]::GetDirectoryName($fullResultPath)
    if (-not [string]::IsNullOrWhiteSpace($resultDirectory)) {
        [System.IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fullResultPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 6
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
