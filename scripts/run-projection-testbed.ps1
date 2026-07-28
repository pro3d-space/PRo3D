<#
.SYNOPSIS
Project an instrument image onto an OPC and score the render against the real image.

.DESCRIPTION
Defaults to the Didymos / Milani-ASPECT scenario. Every parameter is overridable so the
same harness covers other bodies and instruments -- see the Mars/HSH example below.

.EXAMPLE
  .\run-projection-testbed.ps1
  .\run-projection-testbed.ps1 -FlipSweep -Out .\out\didymos
  .\run-projection-testbed.ps1 -Interactive
  .\run-projection-testbed.ps1 -Opc 'C:\data\Mars_OPC' -Body MARS -Frame IAU_MARS `
                               -Observer HERA -Images 'C:\data\HSH'
#>
[CmdletBinding()]
param(
    [string] $Opc,
    [string] $Body,
    [string] $Frame,
    [string] $Observer,
    [string] $Images,
    [string] $Image,
    [int]    $Channel   = -1,
    [string] $Kernel,
    [string] $KernelRoot,
    [ValidateSet('spice','mbi')]
    [string] $Method,
    [double] $Near      = 0,
    [double] $Far       = 0,
    [int]    $Width     = 0,
    [int]    $Height    = 0,
    [string] $Out,
    [switch] $Interactive,
    [switch] $ThirdPerson,
    [switch] $FlipSweep
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\PRo3D.ProjectionTestbed\PRo3D.ProjectionTestbed.fsproj'

$cliArgs = @()
if ($Opc)         { $cliArgs += '--opc';         $cliArgs += $Opc }
if ($Body)        { $cliArgs += '--body';        $cliArgs += $Body }
if ($Frame)       { $cliArgs += '--frame';       $cliArgs += $Frame }
if ($Observer)    { $cliArgs += '--observer';    $cliArgs += $Observer }
if ($Images)      { $cliArgs += '--images';      $cliArgs += $Images }
if ($Image)       { $cliArgs += '--image';       $cliArgs += $Image }
if ($Channel -ge 0) { $cliArgs += '--channel';   $cliArgs += $Channel }
if ($Kernel)      { $cliArgs += '--kernel';      $cliArgs += $Kernel }
if ($KernelRoot)  { $cliArgs += '--kernel-root'; $cliArgs += $KernelRoot }
if ($Method)      { $cliArgs += '--method';      $cliArgs += $Method }
if ($Near -gt 0)  { $cliArgs += '--near';        $cliArgs += $Near }
if ($Far -gt 0)   { $cliArgs += '--far';         $cliArgs += $Far }
if ($Width -gt 0) { $cliArgs += '--width';       $cliArgs += $Width }
if ($Height -gt 0){ $cliArgs += '--height';      $cliArgs += $Height }
if ($Out)         { $cliArgs += '--out';         $cliArgs += $Out }
if ($Interactive) { $cliArgs += '--interactive' }
if ($ThirdPerson) { $cliArgs += '--third-person' }
if ($FlipSweep)   { $cliArgs += '--flip-sweep' }

Write-Host "running: dotnet run --project $proj -- $($cliArgs -join ' ')" -ForegroundColor Cyan
& dotnet run --project $proj -- @cliArgs
exit $LASTEXITCODE
