#!/bin/zsh
# Builds LeXi for the iOS Simulator (a preview; no Apple developer account is needed): Lexi.Core as a
# NativeAOT library (Lexi.Embed), the SwiftUI app, and the signed app bundle. Needs Xcode 26+ with
# the iOS Simulator and the .NET 10 SDK.
#   apple/scripts/build-ios.sh [--run [device]] [output-folder]   (default: artifacts/ios)
# --run boots the named simulator if needed, then installs and opens the app (default: the booted one).
set -euo pipefail
root=${0:A:h:h:h}
run=false; device=booted
if [[ ${1:-} == --run ]]; then
  run=true; shift
  [[ $# -gt 0 && ${1:-} != /* && ${1:-} != .* ]] && { device=$1; shift }
fi
out=${1:-$root/artifacts/ios}
work=$root/.cache/ios-build
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")
rm -rf "$work/embed" && mkdir -p "$work" "$out"
dotnet=${DOTNET:-$(command -v dotnet || true)}
for candidate in $HOME/.dotnet/dotnet /usr/local/share/dotnet/dotnet /opt/homebrew/bin/dotnet; do
  [[ -z $dotnet && -x $candidate ]] && dotnet=$candidate
done
[[ -n $dotnet ]] || { echo "The .NET 10 SDK (dotnet) is required." >&2; exit 1 }

# Lexi.Core runs inside the app: iOS cannot start the lexi-host process the Mac app uses.
"$dotnet" publish "$root/Lexi.Embed/Lexi.Embed.csproj" -c Release -r iossimulator-arm64 \
  --source https://api.nuget.org/v3/index.json -o "$work/embed"
(cd "$root/apple/iOS" && xcodebuild -quiet -scheme LeXiMobile -destination 'generic/platform=iOS Simulator' -configuration Release \
  -derivedDataPath "$work/derived" -skipPackagePluginValidation build)
binary=$work/derived/Build/Products/Release-iphonesimulator/LeXiMobile

app=$out/LeXi.app
rm -rf "$app"
mkdir -p "$app/Frameworks"
cp "$binary" "$app/LeXi"
cp "$work/embed/LexiEmbed.dylib" "$app/Frameworks/LexiEmbed.dylib"
cp -R "$work/embed/assets" "$app/assets"
sed "s/__VERSION__/$version/g" "$root/apple/iOS/Resources/Info.plist" > "$app/Info.plist"
# iOS takes the app icon from a compiled asset catalog: one full-bleed 1024-pixel image of the brand mark.
icons=$work/Assets.xcassets/AppIcon.appiconset
rm -rf "$work/Assets.xcassets" && mkdir -p "$icons"
swift "$root/apple/scripts/make-icon.swift" "$root/docs/brandmark.svg" "$icons/AppIcon.png" --ios
cat > "$icons/Contents.json" <<'JSON'
{"images":[{"filename":"AppIcon.png","idiom":"universal","platform":"ios","size":"1024x1024"}],"info":{"author":"xcode","version":1}}
JSON
xcrun actool "$work/Assets.xcassets" --compile "$app" --platform iphonesimulator --minimum-deployment-target 26.0 \
  --app-icon AppIcon --output-partial-info-plist "$work/icons.plist" >/dev/null
/usr/libexec/PlistBuddy -c "Delete :CFBundleIcons" "$app/Info.plist" >/dev/null 2>&1 || true
/usr/libexec/PlistBuddy -c "Merge $work/icons.plist" "$app/Info.plist" >/dev/null
cp "$root/LICENSE" "$root/THIRD_PARTY_NOTICES.md" "$app/"
cp -R "$root/licenses" "$app/licenses"

# Ad-hoc signatures run in the Simulator only. A device build needs an Apple developer account.
codesign --force --sign - "$app/Frameworks/LexiEmbed.dylib"
# The entitlements are linked into the binary (apple/iOS/Package.swift), as Xcode does for the Simulator.
codesign --force --sign - "$app"
echo "Built $app ($version, iOS Simulator)"

if $run; then
  # Boot the named simulator if it is not running yet, and show it.
  [[ $device != booted ]] && { xcrun simctl boot "$device" 2>/dev/null || true }
  xcrun simctl bootstatus "$device" -b >/dev/null
  open "$(xcode-select -p)/Applications/Simulator.app" 2>/dev/null || true
  xcrun simctl install "$device" "$app"
  xcrun simctl launch "$device" io.github.oqpsk.lexi
fi
