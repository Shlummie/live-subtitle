param(
    [Parameter(Mandatory = $true)]
    [string] $Executable,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$requiredByPage = [ordered]@{
    'Live' = @('About Active profile', 'About Save this session')
    'Audio sources' = @('About Label', 'About Type', 'About Route', 'About Gain', 'About Source enabled')
    'Subtitle appearance' = @(
        'About Font family', 'About Font size', 'About Font weight', 'About Monitor',
        'About Snap position', 'About Width', 'About Caption lines', 'About Show source labels',
        'About Click through when locked', 'About Text', 'About Draft text', 'About Background',
        'About Outline', 'About Background opacity', 'About Outline thickness', 'About Corner radius',
        'About Shadow opacity', 'About Shadow blur', 'About Shadow depth', 'About Screen capture')
    'Recognition' = @(
        'About Speech model', 'About Preset', 'About Backend', 'About Draft interval',
        'About Finalize after silence', 'About Rolling overlap', 'About Maximum utterance',
        'About Audio sensitivity', 'About Silero VAD threshold',
        'About Names or jargon to bias recognition (comma-separated)')
    'Hotkeys' = @(
        'About Listening shortcut', 'About Overlay visibility shortcut', 'About Overlay lock shortcut',
        'About Next profile shortcut', 'About Save session shortcut')
    'General' = @(
        'About Theme', 'About Reduce motion', 'About Start with Windows',
        'About Minimize to tray while listening', 'About Active profile',
        'About Create a profile from the current settings')
}

$process = Start-Process -FilePath (Resolve-Path -LiteralPath $Executable).Path -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'LiveSubtitle did not create its control center.' }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $pageResults = @()
    $allSuccessful = $true

    foreach ($page in $requiredByPage.Keys) {
        $pageCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $page)
        $navigationItem = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $pageCondition)
        if ($null -eq $navigationItem) { throw "Navigation item '$page' was not exposed." }

        $selection = $null
        if (-not $navigationItem.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [ref] $selection)) {
            throw "Navigation item '$page' did not expose SelectionItemPattern."
        }
        $selection.Select()
        Start-Sleep -Milliseconds 250

        $buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
        $helpButtons = @()
        foreach ($button in $buttons) {
            try {
                if (-not $button.Current.Name.StartsWith('About ', [System.StringComparison]::Ordinal)) { continue }
                $helpButtons += [pscustomobject]@{
                    Name = $button.Current.Name
                    HelpText = $button.Current.HelpText
                    KeyboardFocusable = $button.Current.IsKeyboardFocusable
                    Offscreen = $button.Current.IsOffscreen
                }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
        }

        $required = @($requiredByPage[$page])
        $missing = @($required | Where-Object { $_ -notin $helpButtons.Name })
        $invalid = @($helpButtons | Where-Object {
            [string]::IsNullOrWhiteSpace($_.HelpText) -or -not $_.KeyboardFocusable
        })
        $success = $missing.Count -eq 0 -and $invalid.Count -eq 0
        if (-not $success) { $allSuccessful = $false }

        $pageResults += [pscustomobject]@{
            Page = $page
            RequiredCount = $required.Count
            ExposedCount = $helpButtons.Count
            Missing = $missing
            Invalid = $invalid
            Buttons = $helpButtons
            Success = $success
        }
    }

    $result = [pscustomobject]@{
        Success = $allSuccessful
        ProcessId = $process.Id
        WindowName = $root.Current.Name
        Pages = $pageResults
        AuditedAtUtc = [DateTimeOffset]::UtcNow
    }

    $fullResultPath = [System.IO.Path]::GetFullPath($ResultPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullResultPath)) | Out-Null
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fullResultPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 8
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
