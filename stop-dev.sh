#!/usr/bin/env bash
# Stops the processes started by start-dev.sh (and their children).
ROOT="$(cd "$(dirname "$0")" && pwd)"
PIDS="$ROOT/.dev/pids"
[[ -f "$PIDS" ]] || { echo "No dev session is running."; exit 0; }

while IFS='=' read -r name pid; do
  if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
    pkill -TERM -P "$pid" 2>/dev/null || true
    kill "$pid" 2>/dev/null || true
    echo "Stopped $name (pid $pid)"
  fi
done <"$PIDS"
rm -f "$PIDS"
