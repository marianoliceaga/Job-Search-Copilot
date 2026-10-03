# Starts JobPilot (if it is not already running) and opens it in the browser.
# Used by the desktop shortcut; the server runs in its own console window (Ctrl+C there stops it).
param(
    [int]$Port = 5080,
    [int]$TimeoutSeconds = 120
)

$url = "http://localhost:$Port"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Test-JobPilotListening {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        return $true
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

if (-not (Test-JobPilotListening)) {
    # dotnet run uses the "JobPilot" launch profile (Development), which serves the static web assets.
    Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', 'src/JobPilot.Web' `
        -WorkingDirectory $repoRoot

    Write-Host "Starting JobPilot on $url ..."
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-JobPilotListening)) {
        if ((Get-Date) -gt $deadline) {
            Write-Host "JobPilot did not start within $TimeoutSeconds seconds. Check the server window for errors."
            Read-Host 'Press Enter to close'
            exit 1
        }
        Start-Sleep -Milliseconds 500
    }
}

$chrome = "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
if (Test-Path $chrome) {
    Start-Process -FilePath $chrome -ArgumentList $url
} else {
    Start-Process $url
}
