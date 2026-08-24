#!/usr/bin/env bash
# Builds a self-contained binary that runs on a machine with no .NET installed.
#
# Two modes, because they trade different things:
#
#   --aot (default)  NativeAOT. A real native executable plus the two Skia and
#                    HarfBuzz shared objects, which cannot be linked in. Starts
#                    in ~170ms.
#   --single-file    One file, nothing beside it. The runtime unpacks itself to
#                    a temp directory on startup, which costs ~2s every launch.
#
# For a utility on a keybinding the startup difference is what matters, so AOT
# is the default; use --single-file when the artifact must literally be one file.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/src/HotKeyViewer/HotKeyViewer.csproj"
MODE="aot"
RID="linux-x64"
TARBALL=1

usage() {
  cat <<'USAGE'
Usage: packaging/build.sh [--aot|--single-file] [--rid <rid>] [--no-tarball]

  --aot           NativeAOT native binary (default). Fast startup, 3 files.
  --single-file   One self-extracting file. Slower startup.
  --rid <rid>     Target runtime identifier (default: linux-x64).
  --no-tarball    Skip creating the .tar.gz.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --aot) MODE="aot"; shift ;;
    --single-file) MODE="single-file"; shift ;;
    --rid) RID="$2"; shift 2 ;;
    --no-tarball) TARBALL=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 1 ;;
  esac
done

OUT="$REPO_ROOT/dist/$MODE/$RID"
rm -rf "$OUT"
mkdir -p "$OUT"

echo "Building $MODE for $RID…"

if [[ $MODE == "aot" ]]; then
  # NativeAOT needs a C toolchain to link the final binary.
  command -v clang >/dev/null || {
    echo "error: NativeAOT needs clang. Install it, or use --single-file." >&2
    exit 1
  }

  dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    -p:PublishAot=true \
    --output "$OUT" \
    --nologo
else
  dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    --output "$OUT" \
    --nologo
fi

# Keep the symbols next to the build for symbolicating a crash, but never ship
# them — they are larger than the binary itself.
SYMBOLS=()
while IFS= read -r -d '' symbol; do SYMBOLS+=("$symbol"); done \
  < <(find "$OUT" -maxdepth 1 -name '*.dbg' -print0)

echo
echo "Output: $OUT"
find "$OUT" -maxdepth 1 -type f ! -name '*.dbg' -printf '  %-28f %8s bytes\n' | sort

if [[ $TARBALL -eq 1 ]]; then
  ARCHIVE="$REPO_ROOT/dist/hotkeyviewer-$RID-$MODE.tar.gz"
  EXCLUDES=()
  for symbol in "${SYMBOLS[@]:-}"; do
    [[ -n $symbol ]] && EXCLUDES+=(--exclude "$(basename "$symbol")")
  done

  tar -czf "$ARCHIVE" -C "$OUT" "${EXCLUDES[@]}" .
  echo
  echo "Archive: $ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"
fi

if [[ ${#SYMBOLS[@]} -gt 0 && -n ${SYMBOLS[0]:-} ]]; then
  echo "Symbols kept out of the archive: $(basename "${SYMBOLS[0]}")"
fi
