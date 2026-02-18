#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DEFAULT_UNITY="/Applications/Unity/Hub/Editor/2022.3.27f1/Unity.app/Contents/MacOS/Unity"
METHOD_NAME="IOSBuild.BuildIOSFromCommandLine"

OUTPUT_PATH="${1:-Builds/iOS}"
LOG_PATH="${2:-/tmp/unity-ios-build.log}"

resolve_unity_bin() {
  if [ -n "${UNITY_PATH:-}" ] && [ -x "$UNITY_PATH" ]; then
    echo "$UNITY_PATH"
    return 0
  fi

  if [ -x "$DEFAULT_UNITY" ]; then
    echo "$DEFAULT_UNITY"
    return 0
  fi

  local latest
  latest="$(ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity 2>/dev/null | sort -V | tail -n 1 || true)"
  if [ -n "$latest" ] && [ -x "$latest" ]; then
    echo "$latest"
    return 0
  fi

  return 1
}

if [[ "$OUTPUT_PATH" != /* ]]; then
  OUTPUT_PATH="$PROJECT_ROOT/$OUTPUT_PATH"
fi

if ! UNITY_BIN="$(resolve_unity_bin)"; then
  echo "Unity binary not found." >&2
  echo "Set UNITY_PATH or install Unity Hub editor." >&2
  exit 1
fi

IOS_SUPPORT_DIR="$(dirname "$UNITY_BIN")/../PlaybackEngines/iOSSupport"
IOS_SUPPORT_DIR="$(cd "$IOS_SUPPORT_DIR" 2>/dev/null && pwd || true)"
if [ -z "$IOS_SUPPORT_DIR" ] || [ ! -d "$IOS_SUPPORT_DIR" ]; then
  echo "iOS Build Support is not installed for this Unity version." >&2
  echo "Install it via Unity Hub -> Installs -> Add Modules -> iOS Build Support." >&2
  exit 1
fi

if [ -f "$PROJECT_ROOT/Temp/UnityLockfile" ]; then
  echo "Project seems open in Unity Editor."
  echo "Close the editor before running this shortcut."
  exit 1
fi

mkdir -p "$(dirname "$OUTPUT_PATH")"

echo "Unity: $UNITY_BIN"
echo "Project: $PROJECT_ROOT"
echo "Output: $OUTPUT_PATH"
echo "Log: $LOG_PATH"

"$UNITY_BIN" \
  -batchmode -quit \
  -projectPath "$PROJECT_ROOT" \
  -buildTarget iOS \
  -executeMethod "$METHOD_NAME" \
  -buildOutput "$OUTPUT_PATH" \
  -logFile "$LOG_PATH"

echo "Xcode project generated."
echo "Open: $OUTPUT_PATH/Unity-iPhone.xcodeproj"
