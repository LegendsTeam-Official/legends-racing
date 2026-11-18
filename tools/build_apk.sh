#!/usr/bin/env bash
# Builds Builds/LegendsRacing.apk headless. If Unity's own Gradle step fails on
# network/TLS (proxy environments), re-runs the generated Gradle project directly.
set -u
UNITY=${UNITY:-/opt/unity/Editor/Unity}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
"$UNITY" -batchmode -nographics -projectPath "$ROOT" -executeMethod ProjectSetup.BuildAndroid -quit -logFile /tmp/buildandroid.log
if [ -f "$ROOT/Builds/LegendsRacing.apk" ]; then echo "APK: $ROOT/Builds/LegendsRacing.apk"; exit 0; fi
AP=$(dirname "$UNITY")/Data/PlaybackEngines/AndroidPlayer
G="$ROOT/Library/Bee/Android/Prj/IL2CPP/Gradle"
(cd "$G" && "$AP/OpenJDK/bin/java" -classpath "$(ls "$AP"/Tools/gradle/lib/gradle-launcher-*.jar)" org.gradle.launcher.GradleMain assembleRelease) || exit 1
mkdir -p "$ROOT/Builds" && cp "$G/launcher/build/outputs/apk/release/launcher-release.apk" "$ROOT/Builds/LegendsRacing.apk"
echo "APK: $ROOT/Builds/LegendsRacing.apk"
