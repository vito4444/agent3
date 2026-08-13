#!/usr/bin/env bash
# One-shot Unity provisioning for a fresh cloud VM (idempotent).
# Downloads Unity 6000.3.21f1 (Linux), activates from env credentials, then runs
# EditMode tests. Requires UNITY_EMAIL+UNITY_PASSWORD (or UNITY_LICENSE) injected
# into the VM (Cursor Dashboard → Cloud Agents → Secrets).
#   scripts/unity_vm_setup.sh            — install + activate + EditMode tests
#   scripts/unity_vm_setup.sh install    — install only
set -euo pipefail

UNITY_VERSION="6000.3.21f1"
UNITY_CHANGESET="c02631ffc030"
UNITY_ROOT="/opt/unity"
UNITY_BIN="$UNITY_ROOT/Editor/Unity"
TARBALL_URL="https://download.unity3d.com/download_unity/$UNITY_CHANGESET/LinuxEditorInstaller/Unity-$UNITY_VERSION.tar.xz"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

install_unity() {
    if [ -x "$UNITY_BIN" ]; then
        echo "Unity already installed at $UNITY_BIN"
        return
    fi
    echo "Downloading Unity $UNITY_VERSION (~4.4GB)..."
    sudo mkdir -p "$UNITY_ROOT"
    sudo chown "$(whoami)" "$UNITY_ROOT"
    curl -sL -o "$UNITY_ROOT/unity.tar.xz" "$TARBALL_URL"
    echo "Extracting (~8.5GB)..."
    tar -xJf "$UNITY_ROOT/unity.tar.xz" -C "$UNITY_ROOT"
    rm "$UNITY_ROOT/unity.tar.xz"
    [ -x "$UNITY_BIN" ] || { echo "install failed: $UNITY_BIN missing" >&2; exit 1; }
    echo "Installed: $UNITY_BIN"
}

case "${1:-all}" in
install)
    install_unity
    ;;
all)
    install_unity
    "$SCRIPT_DIR/unity_headless.sh" activate
    "$SCRIPT_DIR/unity_headless.sh" test
    ;;
*)
    echo "Usage: $0 [install|all]" >&2
    exit 2
    ;;
esac
