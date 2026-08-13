#!/usr/bin/env bash
# Visual QA screenshot loop: one player process per panel under xvfb.
#   scripts/demo_shots.sh [outdir] [build]
# Defaults: outdir=/tmp/shots, build=Builds/Linux64/Starsoil.x86_64.
set -euo pipefail

OUT="${1:-/tmp/shots}"
BIN="${2:-$(cd "$(dirname "$0")/.." && pwd)/Builds/Linux64/Starsoil.x86_64}"

if [ ! -x "$BIN" ]; then
    echo "player build not found: $BIN (run scripts/unity_headless.sh build-linux)" >&2
    exit 2
fi

rm -rf "$OUT"
mkdir -p "$OUT"
for panel in hud tech jobs recipes starmap trade; do
    echo "--- capturing $panel"
    STARSOIL_DEMO_SHOTS="$OUT" STARSOIL_DEMO_PANEL="$panel" \
        timeout 180 xvfb-run -a -s '-screen 0 960x540x24' \
        "$BIN" -screen-width 960 -screen-height 540 -screen-fullscreen 0 -force-glcore \
        -logFile "$OUT/$panel.log" > /dev/null 2>&1 || echo "  ($panel run exited nonzero)"
done
ls -la "$OUT"/*.png
