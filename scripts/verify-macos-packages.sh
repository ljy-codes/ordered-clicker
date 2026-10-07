#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${ORDERED_CLICKER_VERSION:-2.1.0}"
VERIFY_ROOT="$(mktemp -d /private/tmp/ordered-clicker-verify.XXXXXX)"
MOUNT="$VERIFY_ROOT/mount"
mkdir -p "$MOUNT" "$ROOT/artifacts/macos/final-install"
trap 'hdiutil detach "$MOUNT" >/dev/null 2>&1 || true' EXIT
for arch in arm64 x64; do
  dmg="$ROOT/releases/macos/ordered-clicker-macos-$arch-v$VERSION.dmg"
  hdiutil attach "$dmg" -readonly -nobrowse -mountpoint "$MOUNT" >/dev/null
  app="$MOUNT/有序连点器.app"
  codesign --verify --deep --strict "$app"
  test -L "$MOUNT/Applications"
  test -s "$MOUNT/安装与使用.html"
  test -s "$app/Contents/Resources/使用说明.html"
  expected="$arch"
  [[ "$arch" == x64 ]] && expected=x86_64
  count=0
  while IFS= read -r -d '' binary; do
    if file -b "$binary" | grep -q 'Mach-O'; then
      lipo "$binary" -verify_arch "$expected"
      count=$((count+1))
    fi
  done < <(find "$app/Contents/MacOS" -maxdepth 1 -type f -print0)
  if [[ "$(uname -m)" == "$expected" ]]; then
    installed="$VERIFY_ROOT/installed/有序连点器.app"
    mkdir -p "$VERIFY_ROOT/installed"
    ditto "$app" "$installed"
    hdiutil detach "$MOUNT" >/dev/null
    codesign --verify --deep --strict "$installed"
    "$installed/Contents/MacOS/OrderedClicker.Mac" --render-ui "$ROOT/artifacts/macos/final-install" --native-probe
    test -s "$ROOT/artifacts/macos/final-install/mac-page-5.png"
    test -s "$ROOT/artifacts/macos/final-install/native-probe.json"
    echo "PASS $arch: DMG, signatures, $count native binaries, copied self-contained app launch, five screens, native probe"
  else
    hdiutil detach "$MOUNT" >/dev/null
    echo "PASS $arch: DMG, signatures, $count native binaries (execution requires matching hardware)"
  fi
done
(cd "$ROOT/releases/macos" && shasum -a 256 -c SHA256SUMS.txt)
echo "Verification evidence: $ROOT/artifacts/macos/final-install"
