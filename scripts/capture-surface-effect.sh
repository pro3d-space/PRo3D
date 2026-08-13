#!/bin/zsh
# Launch PRo3D on the most recent scene with a chosen subset of surfaceEffect stages,
# screenshot the result, and shut it down. One bisect round, unattended.
#
# System screenshot rather than PRo3D's own snapshot feature on purpose: the internal
# one is broken on this branch, and capturing the real window tests the image actually on
# screen rather than a second render of it.
#
#   scripts/capture-surface-effect.sh <name> [ADD=a,b,c | DROP=a,b,c | minimal | full]
#
#   scripts/capture-surface-effect.sh baseline full
#   scripts/capture-surface-effect.sh half-a ADD=contourLines,crossSectionClip
#   scripts/capture-surface-effect.sh no-contour DROP=contourLines
#
# Stage names and the env vars behind them: see `surfaceStages` in
# src/PRo3D.Viewer/Viewer/Viewer-Utils.fs and docs/OpcViewer-Screenshot-Harness.md.
#
# Requires: Screen Recording permission for the terminal running this
# (System Settings -> Privacy & Security -> Screen Recording), else screencapture
# fails with "could not create image from display".

set -e

name="${1:?usage: capture-surface-effect.sh <name> [ADD=... | DROP=... | minimal | full]}"
selection="${2:-full}"

root="${0:a:h}/.."
out="${CAPTURE_OUT:-$root/capture-output}"
viewer="$root/bin/Release/net9.0/PRo3D.Viewer.dll"
# The LOD tree keeps refining for a while after the window appears; capturing too early
# photographs a coarse level and reads as a rendering difference that isn't one.
settle="${CAPTURE_SETTLE:-25}"

mkdir -p "$out"

unset PRO3D_SURFACE_EFFECT PRO3D_SURFACE_EFFECT_ADD PRO3D_SURFACE_EFFECT_DROP
case "$selection" in
    full)     ;;
    minimal)  export PRO3D_SURFACE_EFFECT=minimal ;;
    ADD=*)    export PRO3D_SURFACE_EFFECT_ADD="${selection#ADD=}" ;;
    DROP=*)   export PRO3D_SURFACE_EFFECT_DROP="${selection#DROP=}" ;;
    *) echo "unknown selection '$selection' (full | minimal | ADD=... | DROP=...)" >&2; exit 2 ;;
esac

pkill -f PRo3D.Viewer 2>/dev/null || true
pkill -f Aardium 2>/dev/null || true
sleep 1

log="$out/$name.log"
dotnet "$viewer" -loadRecent > "$log" 2>&1 &
pid=$!

# Fail loudly instead of screenshotting a desktop with no viewer on it.
for _ in $(seq 1 60); do
    grep -q "RenderControl Resized" "$log" 2>/dev/null && break
    kill -0 $pid 2>/dev/null || { echo "PRo3D exited early; see $log" >&2; exit 1; }
    sleep 1
done

echo "[stages] $(grep 'surfaceEffect:' "$log" || echo 'all stages active')"
sleep "$settle"

if ! screencapture -x "$out/$name.png"; then
    echo "screencapture failed -- grant Screen Recording permission to this terminal" >&2
    kill $pid 2>/dev/null || true
    exit 1
fi

kill $pid 2>/dev/null || true
pkill -f Aardium 2>/dev/null || true
echo "[out] $out/$name.png"
