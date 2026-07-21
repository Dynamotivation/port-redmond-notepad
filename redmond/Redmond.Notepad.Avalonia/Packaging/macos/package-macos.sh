#!/bin/sh
set -eu

project_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
publish_dir=${1:-"$project_dir/bin/Release/net10.0/osx-arm64/publish"}
publish_dir=$(CDPATH= cd -- "$publish_dir" && pwd)
bundle="$publish_dir/Redmond Notepad.app"
contents="$bundle/Contents"

if [ -e "$bundle" ]; then
    echo "Bundle already exists: $bundle" >&2
    exit 1
fi

if [ ! -f "$publish_dir/Redmond Notepad" ]; then
    echo "Redmond Notepad executable not found in: $publish_dir" >&2
    exit 1
fi

mkdir -p "$contents/MacOS" "$contents/Resources"
cp "$project_dir/Packaging/macos/Info.plist" "$contents/Info.plist"
cp "$project_dir/Assets/RedmondNotepadIcon.icns" "$contents/Resources/RedmondNotepadIcon.icns"

find "$publish_dir" -maxdepth 1 -type f -exec cp {} "$contents/MacOS/" \;
if [ -d "$publish_dir/Resources" ]; then
    cp -R "$publish_dir/Resources" "$contents/MacOS/Resources"
fi

if command -v codesign >/dev/null 2>&1; then
    codesign --force --deep --sign - "$bundle"
fi

echo "$bundle"
