param(
    [Parameter(Mandatory = $true)]
    [string] $Executable,

    [Parameter(Mandatory = $true)]
    [string] $Page,

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
public static class LiveSubtitleWindowCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
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
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Page)
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $item) { throw "The page '$Page' was not exposed." }
    $selection = $null
    if (-not $item.TryGetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern,
        [ref] $selection)) {
        throw "The page '$Page' could not be selected."
    }
    $selection.Select()
    Start-Sleep -Milliseconds 600

    $rectangle = New-Object LiveSubtitleWindowCapture+Rect
    if (-not [LiveSubtitleWindowCapture]::GetWindowRect($process.MainWindowHandle, [ref]$rectangle)) {
        throw 'The control-center bounds could not be read.'
    }
    $bitmap = New-Object System.Drawing.Bitmap(
        ($rectangle.Right - $rectangle.Left),
        ($rectangle.Bottom - $rectangle.Top))
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $deviceContext = $graphics.GetHdc()
            try {
                if (-not [LiveSubtitleWindowCapture]::PrintWindow(
                    $process.MainWindowHandle,
                    $deviceContext,
                    2)) {
                    throw 'Windows could not render the control center into the audit image.'
                }
            }
            finally { $graphics.ReleaseHdc($deviceContext) }
        }
        finally { $graphics.Dispose() }

        $fullOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullOutputPath)) | Out-Null
        $bitmap.Save($fullOutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Get-Item -LiteralPath $fullOutputPath | Select-Object FullName, Length, LastWriteTime
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
