#!/bin/zsh
# Build the app for the Vision Pro, sign it with your Apple team, install and launch it.
#   ui/tools/device.sh                 # all steps
#   ui/tools/device.sh install         # only install + launch the last build
# Settings (env): TEAM (Apple team id), BUNDLE (app id under your team), DEVICE (headset UDID; default: first connected).
# The headset must be awake, on the same network as the Mac, with Developer Mode on.
set -e
TEAM=${TEAM:-4RQU23686J}
BUNDLE=${BUNDLE:-de.medicalvr.aipatient.hakan}
UNITY=/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity
export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
PROJ="$(cd "$(dirname "$0")/../MedicalVR" && pwd)"
APP="$PROJ/Build/xcbuild-device/Release-xros/MeetYourAIPatient.app"
DEVICE=${DEVICE:-$(xcrun devicectl list devices 2>/dev/null | awk '/physical/ && /connected/ {for(i=1;i<=NF;i++) if ($i ~ /^[0-9A-F]{8}-[0-9A-F]{16}$/) print $i}' | head -1)}

if [[ "$1" != "install" ]]; then
  echo "== Unity build (visionOS device)"
  "$UNITY" -batchmode -nographics -projectPath "$PROJ" -executeMethod Setup.BuildDevice -logFile /tmp/unity-build-device.log || true
  grep -q "BUILD Succeeded" /tmp/unity-build-device.log || { grep -E "error CS|BUILD " /tmp/unity-build-device.log | head; exit 1; }
  # only the app gets your id; the embedded UnityFramework keeps its own (same id on both = install fails)
  sed -i '' "s/PRODUCT_BUNDLE_IDENTIFIER = de.medicalvr.aipatient;/PRODUCT_BUNDLE_IDENTIFIER = $BUNDLE;/" "$PROJ/Build/visionOS/Unity-VisionOS.xcodeproj/project.pbxproj"
  echo "== Xcode build + signing (team $TEAM, app $BUNDLE)"
  (cd "$PROJ/Build/visionOS" && xcodebuild -project Unity-VisionOS.xcodeproj -target Unity-VisionOS -configuration Release -sdk xros -arch arm64 \
     DEVELOPMENT_TEAM=$TEAM CODE_SIGN_STYLE=Automatic -allowProvisioningUpdates -allowProvisioningDeviceRegistration \
     SYMROOT="$PROJ/Build/xcbuild-device" build > /tmp/xcbuild-device.log 2>&1) || { grep -E "error:" /tmp/xcbuild-device.log | sort -u | head; exit 1; }
fi

[[ -n "$DEVICE" ]] || { echo "No connected headset. Wake it, check the network, then: ui/tools/device.sh install"; exit 2; }
echo "== Install on $DEVICE"
xcrun devicectl device install app --device "$DEVICE" "$APP" | tail -2
xcrun devicectl device process launch --device "$DEVICE" --terminate-existing "$BUNDLE" | tail -1
