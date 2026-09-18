#!/bin/bash
# Generates the Adaptify *.g.fs files, which are not checked in. By default only the missing
# or stale ones; --all regenerates everything, --check only reports. build.sh and the
# runTests scripts run this too. See docs/ModelTypes.md.
cd "$(dirname "$0")" || exit 1
exec dotnet fsi utilities/Adapt.fsx "$@"
