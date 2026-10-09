#Requires -Version 5.1
<#
.SYNOPSIS
  Starts the Chitra Studio API (http://localhost:5080) and web UI (http://localhost:4200) for local development.
.DESCRIPTION
  Both run in the background; logs go to .dev\logs and process ids to .dev\pids.txt. Stop them with .\stop-dev.ps1.
  ComfyUI is NOT started by this script — run it yourself (see SETUP.md). The app works without it but cannot generate.
.PARAMETER ApiOnly
  Start only the backend.
#>
param([switch]$ApiOnly)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dev = Join-Path $root '.dev'
$logs = Join-Path $dev 'logs'
$pidFile = Join-Path $dev 'pids.txt'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

function Require-Command($name, $hint) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { throw "$name was not found. $hint" }
}

Require-Command dotnet 'Install the .NET 10 SDK: https://dotnet.microsoft.com/download'
if (-not $ApiOnly) { Require-Command npm 'Install Node.js 22+ from https://nodejs.org' }

if (Test-Path $pidFile) {
    Write-Host 'Stopping a previous dev session first...' -ForegroundColor Yellow
    & (Join-Path $root 'stop-dev.ps1')
}

Write-Host 'Building the API...' -ForegroundColor Cyan
dotnet build (Join-Path $root 'backend\Chitra.slnx') -nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }

$api = Start-Process dotnet -ArgumentList 'run', '--no-build', '--launch-profile', 'http', '--project', (Join-Path $root 'backend\src\Chitra.Api') `
    -RedirectStandardOutput (Join-Path $logs 'api.log') -RedirectStandardError (Join-Path $logs 'api.err.log') -WindowStyle Hidden -PassThru
"api=$($api.Id)" | Set-Content $pidFile

if (-not $ApiOnly) {
    $frontend = Join-Path $root 'frontend'
    if (-not (Test-Path (Join-Path $frontend 'node_modules'))) {
        Write-Host 'Installing frontend packages (first run)...' -ForegroundColor Cyan
        Push-Location $frontend; try { npm install --no-audit --no-fund } finally { Pop-Location }
    }
    $web = Start-Process cmd.exe -ArgumentList '/c', 'npx ng serve --port 4200' -WorkingDirectory $frontend `
        -RedirectStandardOutput (Join-Path $logs 'web.log') -RedirectStandardError (Join-Path $logs 'web.err.log') -WindowStyle Hidden -PassThru
    "web=$($web.Id)" | Add-Content $pidFile
}

Write-Host 'Waiting for the API...' -ForegroundColor Cyan
$ok = $false
for ($i = 0; $i -lt 60; $i++) {
    try { Invoke-RestMethod 'http://localhost:5080/api/health' -TimeoutSec 2 | Out-Null; $ok = $true; break } catch { Start-Sleep -Seconds 1 }
}
if (-not $ok) { Write-Warning "The API did not start. See $logs\api.err.log"; exit 1 }

try {
    $status = Invoke-RestMethod 'http://localhost:5080/api/system/status' -TimeoutSec 15
    $color = if ($status.aiProvider.isAvailable) { 'Green' } else { 'Yellow' }
    Write-Host "ComfyUI: $($status.aiProvider.message)" -ForegroundColor $color
    Write-Host "FFmpeg:  $($status.ffmpeg.message)"
    Write-Host "TTS:     $($status.tts.message)"
} catch { }

Write-Host ''
Write-Host 'Chitra Studio is starting:' -ForegroundColor Green
Write-Host '  API  http://localhost:5080  (OpenAPI: /openapi/v1.json)'
if (-not $ApiOnly) { Write-Host '  UI   http://localhost:4200  (first compile takes ~20 s)' }
Write-Host "  Logs $logs"
Write-Host '  Stop .\stop-dev.ps1'
