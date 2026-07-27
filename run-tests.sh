#!/bin/bash
dotnet run --project src/Tests/Tests.fsproj -- --testdatasource "${TESTDATA_SOURCE:-/pro3ddata/testdata}" "$@"
