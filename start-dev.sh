#!/usr/bin/env bash
# Starts the Chitra Studio API (http://localhost:5080) and web UI (http://localhost:4200) in the background.
# Logs: .dev/logs, pids: .dev/pids. Stop with ./stop-dev.sh. ComfyUI is not started here (see SETUP.md).
# Usage: ./start-dev.sh [--api-only]
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
DEV="$ROOT/.dev"
mkdir -p "$DEV/logs"

command -v dotnet >/dev/null || { echo "dotnet not found. Install the .NET 10 SDK."; exit 1; }
API_ONLY=false
[[ "${1:-}" == "--api-only" ]] && API_ONLY=true
if [[ "$API_ONLY" == false ]]; then command -v npm >/dev/null || { echo "npm not found. Install Node.js 22+."; exit 1; }; fi

[[ -f "$DEV/pids" ]] && "$ROOT/stop-dev.sh"

echo "Building the API..."
dotnet build "$ROOT/backend/Chitra.slnx" -nologo -v quiet

nohup dotnet run --no-build --launch-profile http --project "$ROOT/backend/src/Chitra.Api" >"$DEV/logs/api.log" 2>&1 &
echo "api=$!" >"$DEV/pids"

if [[ "$API_ONLY" == false ]]; then
  if [[ ! -d "$ROOT/frontend/node_modules" ]]; then
    echo "Installing frontend packages (first run)..."
    (cd "$ROOT/frontend" && npm install --no-audit --no-fund)
  fi
  (cd "$ROOT/frontend" && nohup npx ng serve --port 4200 >"$DEV/logs/web.log" 2>&1 & echo "web=$!" >>"$DEV/pids")
fi

echo "Waiting for the API..."
for _ in $(seq 1 60); do
  if curl -fs http://localhost:5080/api/health >/dev/null; then
    echo
    curl -s http://localhost:5080/api/system/status | sed -n 's/.*"aiProvider":{[^}]*"message":"\([^"]*\)".*/ComfyUI: \1/p' || true
    echo "Chitra Studio is starting:"
    echo "  API  http://localhost:5080"
    [[ "$API_ONLY" == false ]] && echo "  UI   http://localhost:4200 (first compile takes ~20 s)"
    echo "  Logs $DEV/logs   Stop ./stop-dev.sh"
    exit 0
  fi
  sleep 1
done
echo "The API did not start; see $DEV/logs/api.log"
exit 1
