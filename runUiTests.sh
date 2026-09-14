#!/usr/bin/env bash
# Runs the Playwright UI tests in tests-ui/ against the real viewer (needs a GPU).
# Data comes from PRO3D_TEST_DATA (a PRo3D.Resources.TestData checkout) and
# PRO3D_SPICE_KERNELS (a HERA kernel tree matching the frames' epoch) -- see
# tests-ui/README.md. Builds the viewer and pro3d-tool in Release first.
# Extra args are passed through to Playwright, e.g. ./runUiTests.sh projection-e2e
set -e
cd "$(dirname "$0")"
if [ -z "$PRO3D_TEST_DATA" ] && [ -z "${PRO3D_SCENE+x}" ]; then
    echo "PRO3D_TEST_DATA is not set: point it at a PRo3D.Resources.TestData checkout (see tests-ui/README.md)" >&2
    exit 1
fi
dotnet build src/PRo3D.Viewer/PRo3D.Viewer.fsproj -c Release
dotnet build src/PRo3D.Tool/PRo3D.Tool.fsproj -c Release
cd tests-ui
[ -d node_modules ] || npm install
npx playwright install chromium
npx playwright test "$@"
