#!/bin/bash

cd "$(dirname "$0")/.."

VERSION="1.0.0"

echo "Compiling macOS Intel (osx-x64) build for version $VERSION..."

rm -rf "Saints Row Reborn.app" ./temp_backend ./temp_launcher

dotnet publish SaintsRowReborn.csproj -c Release -r osx-x64 --self-contained true -p:UseAppHost=true -p:Version=$VERSION -o ./temp_backend
dotnet publish SaintsRowReborn.Launcher/SaintsRowReborn.Launcher.csproj -c Release -r osx-x64 --self-contained true -p:UseAppHost=true -p:Version=$VERSION -o ./temp_launcher

mkdir -p "Saints Row Reborn.app/Contents/MacOS"
mkdir -p "Saints Row Reborn.app/Contents/Backend"
mkdir -p "Saints Row Reborn.app/Contents/Resources"

cp -R ./temp_launcher/* "Saints Row Reborn.app/Contents/MacOS/"
cp -R ./temp_backend/* "Saints Row Reborn.app/Contents/Backend/"
cp saintsrow.pfx "Saints Row Reborn.app/Contents/Backend/"
cp saintsrow_rpcs3.pfx "Saints Row Reborn.app/Contents/Backend/"
cp Assets/AppIcon.icns "Saints Row Reborn.app/Contents/Resources/"

chmod +x "Saints Row Reborn.app/Contents/MacOS/SaintsRowReborn.Launcher"
chmod +x "Saints Row Reborn.app/Contents/Backend/SaintsRowReborn"

cat > "Saints Row Reborn.app/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>SaintsRowReborn.Launcher</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleName</key>
    <string>Saints Row Reborn</string>
    <key>CFBundleDisplayName</key>
    <string>Saints Row Reborn</string>
    <key>CFBundleIdentifier</key>
    <string>com.adamstark.saintsrowreborn</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
</dict>
</plist>
EOF

rm -rf ./temp_backend ./temp_launcher
xattr -cr "Saints Row Reborn.app"

codesign --force --sign - "Saints Row Reborn.app/Contents/Backend/SaintsRowReborn"
codesign --force --sign - "Saints Row Reborn.app/Contents/MacOS/SaintsRowReborn.Launcher"
codesign --force --deep --sign - "Saints Row Reborn.app"

xattr -cr "Saints Row Reborn.app"
touch "Saints Row Reborn.app"

echo "MacOS Intel build compiled. Happy trophy hunting! :)"