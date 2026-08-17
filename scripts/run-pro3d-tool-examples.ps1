<#
.SYNOPSIS
Runnable usage examples for pro3d-tool, against the public PRo3D test data.

.DESCRIPTION
Demonstrates every pro3d-tool verb on data anyone can obtain, so the documented
examples are known to work rather than merely plausible.

Test data lives in its own repository, so that a plain PRo3D clone stays small:

    git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git

Pass the resulting path via -TestData.

IMPORTANT: `kdtree --forcekdtreerebuild` rewrites the .aakd files inside the OPC
hierarchy, and the test data repository tracks those files without a .gitignore. So by
default this script works on a COPY under -WorkDir and leaves your clone untouched.
Use -InPlace only if you accept a dirty test data working tree.

The tool is run from source via `dotnet run`, so this works in a checkout before
anything has been published to NuGet.

.EXAMPLE
  .\run-pro3d-tool-examples.ps1 -TestData C:\data\PRo3D.Resources.TestData
  .\run-pro3d-tool-examples.ps1 -TestData C:\data\PRo3D.Resources.TestData -Fresh
  .\run-pro3d-tool-examples.ps1 -TestData C:\data\PRo3D.Resources.TestData -InPlace
#>
[CmdletBinding()]
param(
    # Path to a clone of https://github.com/pro3d-space/PRo3D.Resources.TestData
    [Parameter(Mandatory = $true)]
    [string] $TestData,

    # Where the working copy of the test data is placed. Ignored with -InPlace.
    [string] $WorkDir = (Join-Path $env:TEMP 'pro3d-tool-examples'),

    # Re-copy the test data even if a working copy already exists.
    [switch] $Fresh,

    # Operate directly on -TestData instead of on a copy. Leaves the clone dirty.
    [switch] $InPlace
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\PRo3D.Tool\PRo3D.Tool.fsproj'

if (-not (Test-Path $TestData)) {
    throw "test data not found: $TestData`n" +
          "clone it with: git clone https://github.com/pro3d-space/PRo3D.Resources.TestData.git"
}

$opcName = '1087_004779_MSLMST_0011'
$opcSource = Join-Path $TestData $opcName
if (-not (Test-Path $opcSource)) {
    throw "expected OPC fixture not found: $opcSource`nIs -TestData really a PRo3D.Resources.TestData clone?"
}

function Invoke-Pro3DTool {
    param([string[]] $ToolArgs)
    Write-Host "`n> pro3d-tool $($ToolArgs -join ' ')" -ForegroundColor Cyan
    & dotnet run --project $proj -- @ToolArgs
    if ($LASTEXITCODE -ne 0) { throw "pro3d-tool exited with $LASTEXITCODE" }
}

# --- prepare the OPC the examples operate on -------------------------------------

if ($InPlace) {
    Write-Host "running IN PLACE against $opcSource -- this rewrites tracked .aakd files" -ForegroundColor Yellow
    $opc = $opcSource
} else {
    $opc = Join-Path $WorkDir $opcName
    if ($Fresh -and (Test-Path $opc)) {
        Write-Host "removing existing working copy $opc" -ForegroundColor DarkGray
        Remove-Item -Recurse -Force $opc
    }
    if (-not (Test-Path $opc)) {
        Write-Host "copying test OPC to $opc (this takes a moment; ~170 MB)" -ForegroundColor DarkGray
        New-Item -ItemType Directory -Force $WorkDir | Out-Null
        Copy-Item -Recurse -Force $opcSource $WorkDir
    } else {
        Write-Host "reusing working copy $opc (pass -Fresh to re-copy)" -ForegroundColor DarkGray
    }
}

# --- kdtree ----------------------------------------------------------------------

Write-Host "`n=== kdtree: validate an OPC and use its cached kd-trees ===" -ForegroundColor Green
Invoke-Pro3DTool -ToolArgs @('kdtree', $opc)

Write-Host "`n=== kdtree: force a full rebuild ===" -ForegroundColor Green
Invoke-Pro3DTool -ToolArgs @('kdtree', '--forcekdtreerebuild', $opc)

# --- sun-angles ------------------------------------------------------------------

# Not yet implemented (planned; see plans/pro3dToolSunAngles.md). Even once it is, the
# test data repository currently carries only the ASPECT instrument image -- there is no
# Didymos OPC and no SPICE kernel in it -- so this example cannot be run end to end
# until those fixtures are added. Say so rather than pretending to cover it.
$aspect = Join-Path $TestData 'HERA\Instrument Data'
Write-Host "`n=== sun-angles: SKIPPED ===" -ForegroundColor Yellow
if (Test-Path $aspect) {
    Write-Host "  instrument image is present: $aspect" -ForegroundColor DarkGray
}
Write-Host "  the verb is not implemented yet, and the test data has no body OPC and no" -ForegroundColor DarkGray
Write-Host "  SPICE kernels, which sun-angles also requires. See plans/pro3dToolSunAngles.md." -ForegroundColor DarkGray

Write-Host "`nall available examples completed." -ForegroundColor Green
