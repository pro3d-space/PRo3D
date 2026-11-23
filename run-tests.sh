#!/bin/bash
bash "$(dirname "$0")/adapt.sh" || exit 1
dotnet run --project src/Tests/Tests.fsproj -- --testdatasource "${TESTDATA_SOURCE:-/pro3ddata/testdata}" "$@"
