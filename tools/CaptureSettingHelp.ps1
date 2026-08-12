param(
    [Parameter(Mandatory = $true)]
    [string] $Executable,

    [Parameter(Mandatory = $true)]
    [string] $Page,

    [Parameter(Mandatory = $true)]
    [string] $HelpName,

    [ValidateSet('Focus', 'Hover')]
    [string] $Activation = 'Focus',

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LiveSubtitleHelpCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
}
"@

$process = Start-Process -FilePath (Resolve-Path -LiteralPath $Executable).Path -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'LiveSubtitle did not create its control center.' }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $pageCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Page)
    $navigationItem = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $pageCondition)
    if ($null -eq $navigationItem) { throw "The page '$Page' was not exposed." }

    $selection = $null
    if (-not $navigationItem.TryGetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern,
        [ref] $selection)) {
        throw "The page '$Page' could not be selected."
    }
    $selection.Select()
    Start-Sleep -Milliseconds 400

    $helpCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $HelpName)
    $helpButton = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $helpCondition)
    if ($null -eq $helpButton) { throw "The help button '$HelpName' was not exposed." }

    $scrollItem = $null
    if ($helpButton.TryGetCurrentPattern(
        [System.Windows.Automation.ScrollItemPattern]::Pattern,
        [ref] $scrollItem)) {
        $scrollItem.ScrollIntoView()
    }

    [LiveSubtitleHelpCapture]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 250
    if ($Activation -eq 'Hover') {
        $bounds = $helpButton.Current.BoundingRectangle
        [LiveSubtitleHelpCapture]::SetCursorPos(
            [int]($bounds.Left + ($bounds.Width / 2)),
            [int]($bounds.Top + ($bounds.Height / 2))) | Out-Null
    }
    else {
        $helpButton.SetFocus()
    }
    Start-Sleep -Milliseconds $(if ($Activation -eq 'Hover') { 1600 } else { 700 })

    $tooltipCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ToolTip)
    $tooltip = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $tooltipCondition)
    if ($null -eq $tooltip -or [string]::IsNullOrWhiteSpace($tooltip.Current.Name)) {
        throw "$Activation did not expose the setting tooltip."
    }

    $rectangle = New-Object LiveSubtitleHelpCapture+Rect
    if (-not [LiveSubtitleHelpCapture]::GetWindowRect($process.MainWindowHandle, [ref]$rectangle)) {
        throw 'The control-center bounds could not be read.'
    }
    $width = $rectangle.Right - $rectangle.Left
    $height = $rectangle.Bottom - $rectangle.Top
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen(
                $rectangle.Left,
                $rectangle.Top,
                0,
                0,
                (New-Object System.Drawing.Size($width, $height)),
                [System.Drawing.CopyPixelOperation]::SourceCopy)
        }
        finally { $graphics.Dispose() }

        $fullOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullOutputPath)) | Out-Null
        $bitmap.Save($fullOutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        [pscustomobject]@{
            OutputPath = $fullOutputPath
            HelpButton = $helpButton.Current.Name
            HelpText = $helpButton.Current.HelpText
            ToolTipName = $tooltip.Current.Name
            Activation = $Activation
            KeyboardFocusable = $helpButton.Current.IsKeyboardFocusable
        }
    }
    finally { $bitmap.Dispose() }
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
