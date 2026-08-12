param(
    [Parameter(Mandatory = $true)]
    [int]$RootProcessId,

    [ValidateRange(1, 3600)]
    [int]$DurationSeconds = 60,

    [int[]]$AdditionalProcessIds = @(),

    [Parameter(Mandatory = $true)]
    [string]$ResultPath
)

$ErrorActionPreference = 'Stop'

$startedAtUtc = [DateTimeOffset]::UtcNow
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
$observations = [System.Collections.Generic.List[object]]::new()
$samplingErrors = [System.Collections.Generic.List[string]]::new()
$sampleCount = 0
$observedProcessIds = [System.Collections.Generic.HashSet[int]]::new()
$processIds = @($RootProcessId) + @($AdditionalProcessIds) | Select-Object -Unique

while ([DateTimeOffset]::UtcNow -lt $deadline) {
    try {
        foreach ($processId in $processIds) {
            if (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
                [void]$observedProcessIds.Add($processId)
            }
        }

        $netstatLines = @(& "$env:SystemRoot\System32\netstat.exe" -ano)
        foreach ($line in $netstatLines) {
            $columns = @($line.Trim() -split '\s+')
            if ($columns.Count -lt 4 -or ($columns[0] -ne 'TCP' -and $columns[0] -ne 'UDP')) {
                continue
            }

            $processIdColumn = if ($columns[0] -eq 'TCP') { 4 } else { 3 }
            if ($columns.Count -le $processIdColumn) {
                continue
            }

            $ownerProcessId = 0
            if (-not [int]::TryParse($columns[$processIdColumn], [ref]$ownerProcessId) -or $processIds -notcontains $ownerProcessId) {
                continue
            }

            $observations.Add([pscustomobject]@{
                TimestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
                Protocol = $columns[0]
                ProcessId = $ownerProcessId
                State = if ($columns[0] -eq 'TCP') { $columns[3] } else { $null }
                LocalEndpoint = $columns[1]
                RemoteEndpoint = $columns[2]
            })
        }

        $sampleCount++
    }
    catch {
        $samplingErrors.Add($_.Exception.Message)
    }

    if ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 1000
    }
}

$result = [ordered]@{
    Success = $observations.Count -eq 0 -and $samplingErrors.Count -eq 0
    RootProcessId = $RootProcessId
    ObservedProcessIds = @($observedProcessIds | Sort-Object)
    StartedAtUtc = $startedAtUtc.ToString('O')
    FinishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    RequestedDurationSeconds = $DurationSeconds
    SampleCount = $sampleCount
    SocketObservationCount = $observations.Count
    SocketObservations = @($observations)
    SamplingErrors = @($samplingErrors | Select-Object -Unique)
}

$fullResultPath = [IO.Path]::GetFullPath($ResultPath)
$resultDirectory = [IO.Path]::GetDirectoryName($fullResultPath)
if ($resultDirectory) {
    [IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
}

$temporaryPath = "$fullResultPath.tmp"
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
Move-Item -LiteralPath $temporaryPath -Destination $fullResultPath -Force

if (-not $result.Success) {
    exit 1
}
