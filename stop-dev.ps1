#Requires -Version 5.1
<# Stops the API and web UI started by start-dev.ps1 (including child processes). #>
$pidFile = Join-Path $PSScriptRoot '.dev\pids.txt'
if (-not (Test-Path $pidFile)) {
    Write-Host 'No dev session is running (no .dev\pids.txt).'
    exit 0
}

foreach ($line in Get-Content $pidFile) {
    $name, $id = $line -split '=', 2
    if ($id -and (Get-Process -Id $id -ErrorAction SilentlyContinue)) {
        # /T also stops child processes (dotnet run -> Chitra.Api, cmd -> node).
        taskkill /PID $id /T /F | Out-Null
        Write-Host "Stopped $name (pid $id)"
    }
}
Remove-Item $pidFile -Force
