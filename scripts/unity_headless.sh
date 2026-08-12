#!/usr/bin/env bash
# Headless Unity driver for VM / local automation.
#   scripts/unity_headless.sh activate     — activate license from env (UNITY_LICENSE ulf content, or UNITY_EMAIL/UNITY_PASSWORD[/UNITY_SERIAL])
#   scripts/unity_headless.sh test         — run EditMode tests, results to /tmp/unity_editmode_results.xml
#   scripts/unity_headless.sh build-linux  — player build to Builds/Linux64/
#   scripts/unity_headless.sh build-win    — player build to Builds/Win64/
# Unity binary resolution: $UNITY_PATH, else /opt/unity/Editor/Unity.
set -euo pipefail

UNITY_BIN="${UNITY_PATH:-/opt/unity/Editor/Unity}"
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

if [ ! -x "$UNITY_BIN" ]; then
    echo "Unity editor not found at $UNITY_BIN (set UNITY_PATH to override)." >&2
    exit 2
fi

run_unity() {
    # -logFile - streams the editor log to stdout so CI captures it.
    "$UNITY_BIN" -batchmode -nographics -logFile - "$@"
}

case "${1:-}" in
activate)
    if [ -n "${UNITY_LICENSE:-}" ]; then
        ulf=/tmp/Unity_license.ulf
        printf '%s' "$UNITY_LICENSE" > "$ulf"
        run_unity -quit -manualLicenseFile "$ulf"
    elif [ -n "${UNITY_EMAIL:-}" ] && [ -n "${UNITY_PASSWORD:-}" ]; then
        if [ -n "${UNITY_SERIAL:-}" ]; then
            run_unity -quit -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" -serial "$UNITY_SERIAL"
        else
            run_unity -quit -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD"
        fi
    else
        echo "No credentials: set UNITY_LICENSE (ulf content) or UNITY_EMAIL/UNITY_PASSWORD[/UNITY_SERIAL]." >&2
        echo "To create an activation request: see docs/unity-license/README.md." >&2
        exit 2
    fi
    ;;
test)
    run_unity -projectPath "$PROJECT_DIR" -runTests -testPlatform EditMode \
        -testResults /tmp/unity_editmode_results.xml
    echo "Results: /tmp/unity_editmode_results.xml"
    ;;
build-linux)
    run_unity -projectPath "$PROJECT_DIR" -quit -executeMethod Game.EditorTools.HeadlessBuild.Linux64
    ;;
build-win)
    run_unity -projectPath "$PROJECT_DIR" -quit -executeMethod Game.EditorTools.HeadlessBuild.Win64
    ;;
*)
    echo "Usage: $0 {activate|test|build-linux|build-win}" >&2
    exit 2
    ;;
esac
