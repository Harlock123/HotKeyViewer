#!/usr/bin/env bash
# Installs the monolithic build to ~/.local and registers the desktop entry.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"
INSTALL_DIR="$PREFIX/lib/hotkeyviewer"
BIN_DIR="$PREFIX/bin"
DESKTOP_DIR="$PREFIX/share/applications"
MODE="${MODE:-aot}"
RID="${RID:-linux-x64}"

"$REPO_ROOT/packaging/build.sh" "--$MODE" --rid "$RID" --no-tarball

BUILD_DIR="$REPO_ROOT/dist/$MODE/$RID"

rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$DESKTOP_DIR"

# The .dbg is only useful next to the build tree, for symbolicating a crash.
find "$BUILD_DIR" -maxdepth 1 -type f ! -name '*.dbg' -exec install -m 755 {} "$INSTALL_DIR/" \;

# A symlink, not a copy: the binary loads libSkiaSharp.so and libHarfBuzzSharp.so
# from its own directory, which it resolves through /proc/self/exe, so the real
# file has to stay beside them.
ln -sf "$INSTALL_DIR/hotkeyviewer" "$BIN_DIR/hotkeyviewer"
install -m 644 "$REPO_ROOT/packaging/hotkeyviewer.desktop" "$DESKTOP_DIR/hotkeyviewer.desktop"

command -v update-desktop-database >/dev/null && update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true

echo
echo "Installed $MODE build to $INSTALL_DIR ($(du -sh "$INSTALL_DIR" | cut -f1))"
echo "Run: hotkeyviewer   (ensure $BIN_DIR is on your PATH)"
