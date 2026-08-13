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

PANELS="${PANELS:-hud tech jobs recipes starmap trade tech_researching jobs_assigned trade_quotes craft}"

rm -rf "$OUT"
mkdir -p "$OUT"
for panel in $PANELS; do
    echo "--- capturing $panel"
    STARSOIL_DEMO_SHOTS="$OUT" STARSOIL_DEMO_PANEL="$panel" STARSOIL_DEMO_LANG="${LANG_OVERRIDE:-}" \
        timeout 240 xvfb-run -a -s '-screen 0 960x540x24' \
        "$BIN" -screen-width 960 -screen-height 540 -screen-fullscreen 0 -force-glcore \
        -logFile "$OUT/$panel.log" > /dev/null 2>&1 || echo "  ($panel run exited nonzero)"
done
ls -la "$OUT"/*.png

# world3d runs separately: tiny resolution + long timeout (minutes per frame on llvmpipe).
if [ "${WORLD3D:-0}" = "1" ]; then
    echo "--- capturing world3d (this takes many minutes)"
    STARSOIL_DEMO_SHOTS="$OUT" STARSOIL_DEMO_PANEL=world3d \
        timeout 1500 xvfb-run -a -s '-screen 0 640x360x24' \
        "$BIN" -screen-width 640 -screen-height 360 -screen-fullscreen 0 -force-glcore \
        -logFile "$OUT/world3d.log" > /dev/null 2>&1 || echo "  (world3d run exited nonzero)"
fi
