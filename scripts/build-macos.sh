#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
DOTNET="${ORDERED_CLICKER_DOTNET:-dotnet}"
VERSION="${ORDERED_CLICKER_VERSION:-2.1.0}"
OUTPUT="${ORDERED_CLICKER_OUTPUT:-$ROOT/releases/macos}"
SIGNING_IDENTITY="${ORDERED_CLICKER_MAC_SIGNING_IDENTITY:--}"
NOTARY_PROFILE="${ORDERED_CLICKER_NOTARY_PROFILE:-}"
SIGNING_OPTIONS=0
[[ "$SIGNING_IDENTITY" != - ]] && SIGNING_OPTIONS=runtime
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
export DOTNET_CLI_USE_MSBUILD_SERVER=0
mkdir -p "$OUTPUT" "$ROOT/publish/macos"
if [[ "${ORDERED_CLICKER_RELEASE:-0}" == 1 && ( "$SIGNING_IDENTITY" == - || -z "$NOTARY_PROFILE" ) ]]; then
  echo '正式公证发布需要 ORDERED_CLICKER_MAC_SIGNING_IDENTITY 和 ORDERED_CLICKER_NOTARY_PROFILE。' >&2
  exit 1
fi
restore_args=(-p:NuGetAudit=false)
if [[ -n "${ORDERED_CLICKER_NUGET_SOURCE:-}" ]]; then restore_args+=(--source "$ORDERED_CLICKER_NUGET_SOURCE"); fi
for arch in arm64 x64; do
  rid="osx-$arch"
  stage="$ROOT/publish/macos/$rid/staging"
  app="$stage/有序连点器.app"
  # All deletions stay inside this script's disposable build staging directory.
  rm -rf "$stage"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  "$DOTNET" publish src/OrderedClicker.Mac/OrderedClicker.Mac.csproj -c Release -r "$rid" \
    --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false \
    -p:UseSharedCompilation=false -p:Version="$VERSION" "${restore_args[@]}" -o "$app/Contents/MacOS"
  native_arch="$arch"
  [[ "$arch" == x64 ]] && native_arch=x86_64
  xcrun clang -dynamiclib -fobjc-arc -Wall -Wextra -Wno-unused-parameter -arch "$native_arch" \
    -mmacosx-version-min=13.0 -framework Cocoa -framework Carbon -framework ScreenCaptureKit \
    -framework CoreMedia -framework CoreVideo -framework QuartzCore \
    src/OrderedClicker.Mac/Native/OrderedClickerNative.m \
    -o "$app/Contents/MacOS/libOrderedClickerNative.dylib"
  cp installer/macos/Info.plist "$app/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$app/Contents/Info.plist"
  cp installer/macos/OrderedClicker.icns "$app/Contents/Resources/OrderedClicker.icns"
  cp docs/macos/产品说明.html "$app/Contents/Resources/使用说明.html"
  cp docs/macos/THIRD-PARTY-NOTICES.txt "$app/Contents/Resources/THIRD-PARTY-NOTICES.txt"
  # Apple treats regular files in MacOS as nested code. Keep managed/data files
  # in Resources and provide relative links for the .NET apphost resolver.
  mkdir -p "$app/Contents/Resources/runtime"
  while IFS= read -r -d '' item; do
    if ! /usr/bin/file -b "$item" | /usr/bin/grep -q 'Mach-O'; then
      filename="$(basename "$item")"
      mv "$item" "$app/Contents/Resources/runtime/$filename"
      ln -s "../Resources/runtime/$filename" "$item"
    fi
  done < <(find "$app/Contents/MacOS" -maxdepth 1 -type f -print0)
  # The apphost resolves the managed entry assembly's real path. Make native
  # runtime libraries reachable there too without moving executable code into Resources.
  while IFS= read -r -d '' item; do
    filename="$(basename "$item")"
    ln -s "../../MacOS/$filename" "$app/Contents/Resources/runtime/$filename"
  done < <(find "$app/Contents/MacOS" -maxdepth 1 -type f -print0)
  # Sign every nested Mach-O first; don't use --deep to sign.
  while IFS= read -r -d '' item; do
    if /usr/bin/file -b "$item" | /usr/bin/grep -q 'Mach-O'; then
      args=(--force --options "$SIGNING_OPTIONS" --entitlements installer/macos/entitlements.plist --sign "$SIGNING_IDENTITY")
      [[ "$SIGNING_IDENTITY" != - ]] && args+=(--timestamp)
      codesign "${args[@]}" "$item"
    fi
  done < <(find "$app/Contents/MacOS" -type f -print0)
  args=(--force --options "$SIGNING_OPTIONS" --entitlements installer/macos/entitlements.plist --sign "$SIGNING_IDENTITY")
  [[ "$SIGNING_IDENTITY" != - ]] && args+=(--timestamp)
  codesign "${args[@]}" "$app"
  codesign --verify --deep --strict "$app"
  if [[ -n "$NOTARY_PROFILE" ]]; then
    zip="$ROOT/publish/macos/$rid/notarize.zip"
    ditto -c -k --keepParent "$app" "$zip"
    xcrun notarytool submit "$zip" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$app"
    xcrun stapler validate "$app"
    spctl --assess --type execute "$app"
  fi
  ln -s /Applications "$stage/Applications"
  cp docs/macos/产品说明.html "$stage/安装与使用.html"
  dmg="$OUTPUT/ordered-clicker-macos-$arch-v$VERSION.dmg"
  hdiutil create -volname "有序连点器 $VERSION $arch" -srcfolder "$stage" -format UDZO -ov "$dmg"
  if [[ -n "$NOTARY_PROFILE" ]]; then
    codesign --force --timestamp --sign "$SIGNING_IDENTITY" "$dmg"
    xcrun notarytool submit "$dmg" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$dmg"
    xcrun stapler validate "$dmg"
  fi
  hdiutil verify "$dmg"
done
(cd "$OUTPUT" && shasum -a 256 ./*.dmg > SHA256SUMS.txt)
echo "macOS packages: $OUTPUT"
